using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat.Attacks;
using VikingLib.Configuration;
using VikingLib.SkillSystem;
using VikingLib.Stats;

namespace VikingLib.Patches
{
    /// <summary>
    /// Harmony patches governing skill acquisition, XP interception, death penalties,
    /// and routing native skill queries through VanillaSkillIntegration channels and CharacterStatManager.
    /// </summary>
    [HarmonyPatch]
    public static class SkillLifecyclePatch
    {
        // --- 1. Skill Acquisition & XP Interception ---

        [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        [HarmonyPrefix]
        public static bool PrefixRaiseSkill(Skills __instance, Skills.SkillType skillType, float factor)
        {
            var player = __instance.GetComponent<Player>();
            if (player != null && !SkillEvents.ShouldRaiseSkill(player, skillType, factor))
            {
                return false; // Cancel native XP gain
            }
            return true;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSkillLevelup))]
        [HarmonyPostfix]
        public static void PostfixOnSkillLevelup(Player __instance, Skills.SkillType skill, float level)
        {
            SkillEvents.InvokeSkillLevelChanged(__instance, skill, level);
            VanillaEquipmentAdapter.SyncEquipment(__instance);
        }

        // --- 2. Death Penalty Interception ---

        [HarmonyPatch(typeof(Skills), nameof(Skills.OnDeath))]
        [HarmonyPrefix]
        public static bool PrefixOnDeath(Skills __instance)
        {
            var player = __instance.GetComponent<Player>();
            if (player != null && !SkillEvents.ShouldApplyDeathPenalty(player))
            {
                return false; // Cancel native skill loss on death
            }
            return true;
        }

        // --- 2b. Corrupt & Unregistered Skill Defense & Save Sanitizer ---

        private static readonly AccessTools.FieldRef<Skills, Dictionary<Skills.SkillType, Skills.Skill>> _skillDataRef =
            AccessTools.FieldRefAccess<Skills, Dictionary<Skills.SkillType, Skills.Skill>>("m_skillData");

        private static readonly System.Reflection.MethodInfo _getSkillDefMethod =
            AccessTools.Method(typeof(Skills), "GetSkillDef", new[] { typeof(Skills.SkillType) });

        [HarmonyPatch(typeof(Skills), nameof(Skills.Save))]
        [HarmonyPrefix]
        public static void PrefixSaveSkills(Skills __instance)
        {
            var dict = _skillDataRef(__instance);
            if (dict == null) return;

            List<Skills.SkillType>? invalidKeys = null;
            foreach (var kvp in dict)
            {
                if (kvp.Value == null || kvp.Value.m_info == null)
                {
                    invalidKeys ??= new List<Skills.SkillType>();
                    invalidKeys.Add(kvp.Key);
                }
            }

            if (invalidKeys != null)
            {
                foreach (var k in invalidKeys)
                {
                    dict.Remove(k);
                }
            }
        }

        [HarmonyPatch(typeof(Skills), "GetSkill")]
        [HarmonyPrefix]
        public static bool PrefixGetSkill(Skills __instance, Skills.SkillType skillType, ref Skills.Skill __result)
        {
            var dict = _skillDataRef(__instance);
            if (dict != null && dict.TryGetValue(skillType, out var skill))
            {
                if (skill != null && skill.m_info != null)
                {
                    __result = skill;
                    return false;
                }
                // Existing entry had null or null m_info; remove it to prevent corrupting saves
                dict.Remove(skillType);
            }

            var skillDef = (Skills.SkillDef?)_getSkillDefMethod.Invoke(__instance, new object[] { skillType });
            if (skillDef == null)
            {
                // SkillType has no registered SkillDef in the player's skills list (e.g. unused enums like Cooking/Crafting/Dodge)
                // Return a safe ephemeral skill with a synthetic SkillDef instead of polluting m_skillData with new Skill(null)
                var fallbackDef = new Skills.SkillDef { m_skill = skillType };
                __result = new Skills.Skill(fallbackDef);
                return false;
            }

            return true;
        }

        // --- 3. Ranged Draw & Reload Duration Scaling ---

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackDrawPercentage))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileGetAttackDrawPercentage(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(CalculateBowDrawDuration))));
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateBowDrawDuration(float minDuration, float maxDuration, float skillFactor, Humanoid humanoid)
        {
            if (humanoid != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float speed = CharacterStatManager.GetStat(humanoid, CharacterStatType.BowDrawSpeed);
                return minDuration / Mathf.Max(0.01f, speed);
            }

            float factor = VanillaSkillIntegration.EnableRangedDrawScaling ? skillFactor : 0f;
            return Mathf.Lerp(minDuration, maxDuration, factor);
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeaponLoadingTime))]
        [HarmonyPrefix]
        public static bool PrefixGetWeaponLoadingTime(ItemDrop.ItemData __instance, ref float __result)
        {
            if (__instance.m_shared?.m_attack?.m_requiresReload != true)
            {
                __result = 1f;
                return false;
            }

            float reloadTime = __instance.m_shared.m_attack.m_reloadTime;
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float reloadSpeed = CharacterStatManager.GetStat(Player.m_localPlayer, CharacterStatType.CrossbowReloadSpeed);
                __result = reloadTime / Mathf.Max(0.01f, reloadSpeed);
                return false;
            }

            if (VanillaSkillIntegration.EnableRangedDrawScaling && Player.m_localPlayer != null)
            {
                float factor = Player.m_localPlayer.GetSkillFactor(__instance.m_shared.m_skillType);
                reloadTime = Mathf.Lerp(reloadTime, reloadTime * 0.5f, factor);
            }

            __result = reloadTime;
            return false;
        }

        private static readonly AccessTools.FieldRef<Player, List<Player.MinorActionData>> _actionQueueRef =
            AccessTools.FieldRefAccess<Player, List<Player.MinorActionData>>("m_actionQueue");

        /// <summary>
        /// Intercepts crossbow reload minor actions to scale stamina drain by CrossbowReloadStaminaEfficiency.
        /// </summary>
        [HarmonyPatch(typeof(Player), "QueueReloadAction")]
        [HarmonyPostfix]
        public static void PostfixQueueReloadAction(Player __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;

            var queue = _actionQueueRef(__instance);
            if (queue == null || queue.Count == 0) return;

            var lastAction = queue[queue.Count - 1];
            if (lastAction != null && lastAction.m_type == Player.MinorActionData.ActionType.Reload)
            {
                if (lastAction.m_staminaDrain > 0f)
                {
                    lastAction.m_staminaDrain = CharacterStatManager.GetEffectiveActionCost(
                        lastAction.m_staminaDrain,
                        __instance,
                        CharacterStatType.CrossbowReloadStaminaEfficiency
                    );
                }
            }
        }

        // --- 4. Damage Roll Range Scaling ---

        [HarmonyPatch(typeof(Skills), nameof(Skills.GetRandomSkillRange))]
        [HarmonyPrefix]
        public static bool PrefixGetRandomSkillRange(ref float min, ref float max, Skills.SkillType skillType)
        {
            if (!VanillaSkillIntegration.EnableDamageRollScaling)
            {
                min = VanillaSkillIntegration.FixedDamageRollLowerBound;
                max = VanillaSkillIntegration.FixedDamageRollUpperBound;
                return false;
            }
            return true;
        }

        // --- 5. Jump Height Physics & Mount Locomotion Scaling ---

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileJump(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldc_R4, 1f),
                new CodeMatch(OpCodes.Ldloc_2),
                new CodeMatch(OpCodes.Ldc_R4, 0.4f),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Add),
                new CodeMatch(OpCodes.Stloc_S));

            if (matcher.IsValid)
            {
                var labels = matcher.Labels;
                matcher.RemoveInstructions(5);
                matcher.Insert(
                    new CodeInstruction(OpCodes.Ldarg_0) { labels = labels },
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetJumpMultiplier)))
                );
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetJumpMultiplier(Character character)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                float skillFactor = VanillaSkillIntegration.EnableLocomotionSpeedScaling && character.GetSkills() != null
                    ? character.GetSkills().GetSkillFactor(Skills.SkillType.Jump)
                    : 0f;
                return 1f + skillFactor * 0.4f;
            }

            float baseVelocity = Mathf.Max(0.01f, character.m_jumpForce);
            float targetVelocity = CharacterStatManager.GetEffectiveJumpVelocity(character);
            return targetVelocity / baseVelocity;
        }

        [HarmonyPatch(typeof(Sadle), "ApplyControlls")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileSadleApplyControlls(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Skills), nameof(Skills.GetSkillFactor), new[] { typeof(Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetRideSkillFactor));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                    break;
                }
            }
            return list;
        }

        public static float GetRideSkillFactor(Skills skills, Skills.SkillType skillType)
        {
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                return Mathf.Clamp01(CharacterStatManager.GetStat(Player.m_localPlayer, CharacterStatType.RidingControl));
            }

            if (!VanillaSkillIntegration.EnableLocomotionSpeedScaling) return 0f;
            return skills.GetSkillFactor(skillType);
        }

        [HarmonyPatch(typeof(Character), "GetRunSpeedFactor")]
        [HarmonyPostfix]
        public static void PostfixGetRunSpeedFactor(Character __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            if (__instance.HaveRider())
            {
                var rider = (Player.m_localPlayer != null && Player.m_localPlayer.IsRiding()) ? Player.m_localPlayer : null;
                if (rider != null)
                {
                    __result = CharacterStatManager.GetStat(rider, CharacterStatType.RidingSpeed);
                }
            }
        }

        [HarmonyPatch(typeof(Sadle), "UpdateRiding")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileSadleUpdateRiding(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) });
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false, new CodeMatch(OpCodes.Call, targetMethod));

            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetMountStaminaDrainFactor))));
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetMountStaminaDrainFactor(float a, float b, float skill, Sadle sadle)
        {
            if (ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                var rider = (sadle.HaveValidUser() && Player.m_localPlayer != null && Player.m_localPlayer.IsRiding())
                    ? Player.m_localPlayer
                    : null;
                if (rider != null)
                {
                    float eff = Mathf.Max(0f, CharacterStatManager.GetStat(rider, CharacterStatType.RidingStaminaEfficiency));
                    return 1f / (1f + eff);
                }
            }
            return Mathf.Lerp(a, b, skill);
        }

        // --- 6. Block Power Skill Scaling ---

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileBlockAttack(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            var targetGetBlockPower = AccessTools.Method(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), new[] { typeof(float) });
            var targetGetSkillLevel = AccessTools.Method(typeof(Character), nameof(Character.GetSkillLevel), new[] { typeof(Skills.SkillType) });

            matcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, targetGetBlockPower));
            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetEffectiveBlockPower))));
            }

            var list = new List<CodeInstruction>(matcher.InstructionEnumeration());
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetGetSkillLevel))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetBlockSkillLevel)));
                }
            }
            return list;
        }

        public static float GetEffectiveBlockPower(ItemDrop.ItemData item, float skillFactor, Humanoid humanoid)
        {
            float baseBlock = item.GetBaseBlockPower(item.m_quality);
            if (ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                return baseBlock * CharacterStatManager.GetStat(humanoid, CharacterStatType.BlockPowerMultiplier);
            }

            float factor = VanillaSkillIntegration.EnableBlockPowerScaling ? skillFactor : 0f;
            return item.GetBlockPower(factor);
        }

        public static float GetBlockSkillLevel(Character character, Skills.SkillType skillType)
        {
            if (!VanillaSkillIntegration.EnableBlockPowerScaling) return 0f;
            return character.GetSkillLevel(skillType);
        }

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        [HarmonyPostfix]
        public static void PostfixBlockAttack(Humanoid __instance, bool __result)
        {
            if (__result && ModConfig.ModEnabled.Value)
            {
                VikingLib.StatusEffects.StatusEffectRegistry.ConsumeCharges(__instance, VikingLib.StatusEffects.ChargeConsumeTrigger.BlockParry);
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPowerTooltip))]
        [HarmonyPrefix]
        public static bool PrefixGetBlockPowerTooltip(ItemDrop.ItemData __instance, int quality, ref float __result)
        {
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float baseBlock = __instance.GetBaseBlockPower(quality);
                __result = baseBlock * CharacterStatManager.GetStat(Player.m_localPlayer, CharacterStatType.BlockPowerMultiplier);
                return false;
            }

            if (!VanillaSkillIntegration.EnableBlockPowerScaling)
            {
                __result = __instance.GetBlockPower(quality, 0f);
                return false;
            }
            return true;
        }

        // --- 7. Projectile Precision & Weapon Hold Drains ---

        [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileFireProjectileBurst(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Attack), "m_skillAccuracy")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                    matcher.Advance(1);
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(CalculateProjectileSpread))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateProjectileSpread(float minSpread, float maxSpread, float skillFactor, Attack attack)
        {
            float factor = VanillaSkillIntegration.EnableDamageRollScaling ? skillFactor : 0f;
            float spread = Mathf.Lerp(minSpread, maxSpread, factor);
            var character = HumanoidAttackHelper.GetCharacter(attack);
            if (character != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float precisionMultiplier = CharacterStatManager.GetEffectiveSpreadMultiplier(character);
                spread *= precisionMultiplier;
            }
            return spread;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDrawStaminaDrain))]
        [HarmonyPrefix]
        public static bool PrefixGetDrawStaminaDrain(ItemDrop.ItemData __instance, ref float __result)
        {
            float drain = __instance.m_shared?.m_attack?.m_drawStaminaDrain ?? 0f;
            if (drain <= 0f)
            {
                __result = 0f;
                return false;
            }

            if (VanillaSkillIntegration.EnableAttackCostScaling && Player.m_localPlayer != null && __instance.m_shared != null)
            {
                float factor = Player.m_localPlayer.GetSkillFactor(__instance.m_shared.m_skillType);
                drain -= drain * 0.33f * factor;
            }

            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                drain = CharacterStatManager.GetEffectiveActionCost(drain, Player.m_localPlayer, CharacterStatType.BowDrawStaminaEfficiency);
            }

            __result = Mathf.Max(0f, drain);
            return false;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDrawEitrDrain))]
        [HarmonyPrefix]
        public static bool PrefixGetDrawEitrDrain(ItemDrop.ItemData __instance, ref float __result)
        {
            float drain = __instance.m_shared?.m_attack?.m_drawEitrDrain ?? 0f;
            if (drain <= 0f)
            {
                __result = 0f;
                return false;
            }

            if (VanillaSkillIntegration.EnableAttackCostScaling && Player.m_localPlayer != null && __instance.m_shared != null)
            {
                float factor = Player.m_localPlayer.GetSkillFactor(__instance.m_shared.m_skillType);
                drain -= drain * 0.33f * factor;
            }

            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                drain = CharacterStatManager.GetEffectiveActionCost(drain, Player.m_localPlayer, CharacterStatType.BowDrawEitrEfficiency);
            }

            __result = Mathf.Max(0f, drain);
            return false;
        }

        // --- 8. Stealth Concealment Scaling ---

        [HarmonyPatch(typeof(Player), "UpdateStealth")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileUpdateStealth(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })),
                new CodeMatch(OpCodes.Stfld, AccessTools.Field(typeof(Player), "m_stealthFactorTarget")));

            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(CalculateStealthFactorTarget))));
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateStealthFactorTarget(float minFactor, float maxFactor, float skillFactor, Player player)
        {
            float factor = VanillaSkillIntegration.EnableStealthScaling ? skillFactor : 0f;
            float target = Mathf.Lerp(minFactor, maxFactor, factor);
            if (player != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float stealthMultiplier = CharacterStatManager.GetEffectiveStealthFactor(player);
                target *= stealthMultiplier;
            }
            return target;
        }

        // --- 9. Building, Crafting, Farming & Harvesting Scaling ---

        [HarmonyPatch(typeof(Player), "GetBuildStamina")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileGetBuildStamina(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetBuildSkillFactor));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                    break;
                }
            }
            return list;
        }

        [HarmonyPatch(typeof(Player), "GetPlaceDurability")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileGetPlaceDurability(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetBuildSkillFactor));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                    break;
                }
            }
            return list;
        }

        public static float GetBuildSkillFactor(Character character, Skills.SkillType skillType)
        {
            if (!VanillaSkillIntegration.EnableBuildAndCraftScaling) return 0f;
            return character?.GetSkillFactor(skillType) ?? 0f;
        }

        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileDoMeleeAttack(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Attack), "m_harvestRadiusMaxLevel")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(CalculateHarvestRadius))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        [HarmonyPatch(typeof(Piece), "Awake")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspilePieceAwake(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Piece), "m_harvestRadiusMaxLevel")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(CalculateHarvestRadius))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.OnPlaced))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspilePieceOnPlaced(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Piece), "m_harvestRadiusMaxLevel")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(CalculateHarvestRadius))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateHarvestRadius(float minRadius, float maxRadius, float skillFactor)
        {
            float factor = VanillaSkillIntegration.EnableBuildAndCraftScaling ? skillFactor : 0f;
            float radius = Mathf.Lerp(minRadius, maxRadius, factor);
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float mod = CharacterStatManager.GetStat(Player.m_localPlayer, CharacterStatType.HarvestRadius);
                radius *= Mathf.Max(0.1f, mod);
            }
            return radius;
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileDoCrafting(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetCraftingSkillFactor));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                    break;
                }
            }
            return list;
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileUpdateRecipe(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetCraftingSkillFactor));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                    break;
                }
            }
            return list;
        }

        public static float GetCraftingSkillFactor(Character character, Skills.SkillType skillType)
        {
            if (character != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float yield = CharacterStatManager.GetStat(character, CharacterStatType.CraftingYield);
                return (yield - 1.0f) / 0.25f;
            }

            if (!VanillaSkillIntegration.EnableDropYieldScaling && !VanillaSkillIntegration.EnableBuildAndCraftScaling) return 0f;
            return character?.GetSkillFactor(skillType) ?? 0f;
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspilePickableInteract(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetForagingSkillFactor));

            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, targetMethod));
            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, helperMethod));
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetForagingSkillFactor(Character character, Skills.SkillType skillType, Pickable pickable)
        {
            if (character != null && pickable != null && pickable.m_maxLevelBonusChance > 0f && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float yield = CharacterStatManager.GetStat(character, CharacterStatType.ForagingYield);
                return (yield - 1.0f) / pickable.m_maxLevelBonusChance;
            }

            if (!VanillaSkillIntegration.EnableDropYieldScaling && !VanillaSkillIntegration.EnableBuildAndCraftScaling) return 0f;
            return character?.GetSkillFactor(skillType) ?? 0f;
        }

        [HarmonyPatch(typeof(CookingStation), "OnInteract")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileCookingStationOnInteract(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillLifecyclePatch), nameof(GetCookingSkillFactor));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                    break;
                }
            }
            return list;
        }

        public static float GetCookingSkillFactor(Character character, Skills.SkillType skillType)
        {
            if (character != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float yield = CharacterStatManager.GetStat(character, CharacterStatType.CookingYield);
                return (yield - 1.0f) / 0.25f;
            }

            if (!VanillaSkillIntegration.EnableDropYieldScaling && !VanillaSkillIntegration.EnableBuildAndCraftScaling) return 0f;
            return character?.GetSkillFactor(skillType) ?? 0f;
        }

        [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileFishingFloatFixedUpdate(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(FishingFloat), "m_hookedStaminaPerSecMaxSkill")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLifecyclePatch), nameof(CalculateFishingStaminaDrain))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateFishingStaminaDrain(float minDrain, float maxDrain, float skillFactor)
        {
            float factor = VanillaSkillIntegration.EnableBuildAndCraftScaling ? skillFactor : 0f;
            float drain = Mathf.Lerp(minDrain, maxDrain, factor);
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                drain = CharacterStatManager.GetEffectiveActionCost(drain, Player.m_localPlayer, CharacterStatType.FishingStaminaEfficiency);
            }
            return drain;
        }

        // --- 10. Monster Slay Loot Yield (CharacterDrop Multi-Drop Scaling) ---

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        [HarmonyPostfix]
        public static void PostfixGenerateDropList(CharacterDrop __instance, ref List<KeyValuePair<GameObject, int>> __result)
        {
            if (__result == null || __result.Count == 0) return;
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            float yield = CharacterStatManager.GetStat(player, CharacterStatType.MonsterDropYield);
            if (Mathf.Abs(yield - 1.0f) <= 0.0001f) return;

            for (int i = 0; i < __result.Count; i++)
            {
                var entry = __result[i];
                if (entry.Key == null) continue;
                var itemDrop = entry.Key.GetComponent<ItemDrop>();
                if (itemDrop?.m_itemData != null && CharacterStatManager.CanScaleDrop(itemDrop.m_itemData))
                {
                    int scaled = CharacterStatManager.ResolveDropCount(yield, itemDrop.m_itemData, entry.Value);
                    __result[i] = new KeyValuePair<GameObject, int>(entry.Key, scaled);
                }
            }
        }
    }
}
