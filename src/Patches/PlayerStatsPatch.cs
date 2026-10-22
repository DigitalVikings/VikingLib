using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VikingLib.Configuration;
using VikingLib.Stats;

namespace VikingLib.Patches
{
    /// <summary>
    /// Surgical Harmony patches intercepting player health, stamina, eitr, recovery delay, and carry weight.
    /// </summary>
    [HarmonyPatch]
    public static class PlayerStatsPatch
    {
        private class DamageCooldownTracker
        {
            public float LastDamageTime = -999f;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Character, DamageCooldownTracker> _damageCooldowns = new();

        /// <summary>
        /// Records that a character has taken health damage, starting the combat regen pause timer.
        /// </summary>
        public static void RecordDamageTaken(Character character)
        {
            if (character == null) return;
            _damageCooldowns.GetOrCreateValue(character).LastDamageTime = Time.time;
        }

        /// <summary>
        /// Resets the combat damage timer for a character (e.g. upon spawn or death).
        /// </summary>
        public static void ResetDamageTimer(Character character)
        {
            if (character == null) return;
            if (_damageCooldowns.TryGetValue(character, out var tracker))
            {
                tracker.LastDamageTime = -999f;
            }
        }

        /// <summary>
        /// Returns the remaining health regeneration combat delay in seconds for the given character.
        /// Returns 0 if not currently in cooldown.
        /// </summary>
        public static float GetRemainingRegenDelay(Character character)
        {
            if (character == null) return 0f;
            float delay = CharacterStatManager.GetStat(character, CharacterStatType.HealthRegenDelay);
            if (delay <= 0.001f) return 0f;

            if (_damageCooldowns.TryGetValue(character, out var tracker))
            {
                float elapsed = Time.time - tracker.LastDamageTime;
                return Mathf.Max(0f, delay - elapsed);
            }
            return 0f;
        }

        /// <summary>
        /// Intercepts Character.ApplyDamage to record when actual health damage was taken,
        /// triggering the combat health regen delay. Fully blocked/parried hits (0 damage) do not trigger this.
        /// </summary>
        [HarmonyPatch(typeof(Character), "ApplyDamage")]
        [HarmonyPostfix]
        public static void PostfixApplyDamage(Character __instance, HitData hit)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            if (__instance == null || hit == null) return;

            // Only trigger if damage actually pierced defenses/armor and hit health (ignores sub-1.0 chip/rounding residue)
            if (hit.GetTotalDamage() >= 1.0f)
            {
                RecordDamageTaken(__instance);
            }
        }

        /// <summary>
        /// Continuous health regeneration tick running every physics update for both players and creatures.
        /// Suppressed while under the HealthRegenDelay combat cooldown.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        [HarmonyPostfix]
        public static void PostfixCustomFixedUpdate(Character __instance, float dt)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            if (__instance == null || __instance.IsDead()) return;

            var nview = CharacterStatsHelper.GetNView(__instance);
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            float currentHp = __instance.GetHealth();
            float maxHp = __instance.GetMaxHealth();
            if (currentHp >= maxHp) return;

            float regenDelay = CharacterStatManager.GetStat(__instance, CharacterStatType.HealthRegenDelay);
            if (regenDelay > 0.001f && _damageCooldowns.TryGetValue(__instance, out var tracker))
            {
                if (Time.time - tracker.LastDamageTime < regenDelay)
                {
                    return; // In combat cooldown, suppress health regeneration
                }
            }

            float regenRate = CharacterStatManager.GetStat(__instance, CharacterStatType.HealthRegen);
            if (regenRate > 0.0001f)
            {
                float newHp = Mathf.Min(maxHp, currentHp + regenRate * dt);
                __instance.SetHealth(newHp);
            }
        }

        /// <summary>
        /// Replaces native stamina and eitr regen calculations inside Player.UpdateStats with 3-tier evaluation.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdateStats", new[] { typeof(float) })]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileUpdateStats(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            // 1. Target stamina regen calculation: from ldfld m_staminaRegen (IL_0085) to second stloc.3 (IL_00C2)
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Player), "m_staminaRegen")),
                new CodeMatch(OpCodes.Ldc_R4, 1f));

            if (matcher.IsValid)
            {
                // matcher.Pos is at IL_0085 (ldfld m_staminaRegen).
                // Do NOT advance back to ldarg.0 (IL_0084)! IL_0084 carries the branch target label from IL_007C.
                // ldarg.0 already pushes `player` onto the evaluation stack.

                var endMatcher = matcher.Clone();
                endMatcher.MatchForward(false,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen), new[] { typeof(float).MakeByRefType() })));

                if (endMatcher.IsValid)
                {
                    endMatcher.MatchForward(false, new CodeMatch(OpCodes.Stloc_3));
                    if (endMatcher.IsValid)
                    {
                        int count = (endMatcher.Pos - matcher.Pos) + 1; // Include stloc.3
                        var labels = matcher.Labels;
                        matcher.RemoveInstructions(count);
                        matcher.Insert(
                            new CodeInstruction(OpCodes.Ldloc_2) { labels = labels }, // action factor V_2
                            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlayerStatsPatch), nameof(CalculateStaminaRegenRate))),
                            new CodeInstruction(OpCodes.Stloc_3) // store in V_3
                        );
                    }
                }
            }

            // 2. Target eitr regen calculation: from ldfld m_eiterRegen (IL_01DC) to second stloc.s V_11 (IL_0221)
            matcher.Start();
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Player), "m_eiterRegen")),
                new CodeMatch(OpCodes.Ldc_R4, 1f));

            if (matcher.IsValid)
            {
                // matcher.Pos is at IL_01DC (ldfld m_eiterRegen).
                // Do NOT advance back to ldarg.0 (IL_01DB)! IL_01DB carries the branch target label from IL_01D2.
                // ldarg.0 already pushes `player` onto the evaluation stack.

                var endMatcher = matcher.Clone();
                endMatcher.MatchForward(false,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(SEMan), nameof(SEMan.ModifyEitrRegen), new[] { typeof(float).MakeByRefType() })));

                if (endMatcher.IsValid)
                {
                    endMatcher.MatchForward(false, new CodeMatch(OpCodes.Stloc_S, (byte)11));
                    if (endMatcher.IsValid)
                    {
                        int count = (endMatcher.Pos - matcher.Pos) + 1; // Include stloc.s 11
                        var labels = matcher.Labels;
                        matcher.RemoveInstructions(count);
                        matcher.Insert(
                            new CodeInstruction(OpCodes.Ldloc_S, (byte)10) { labels = labels }, // action factor V_10
                            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlayerStatsPatch), nameof(CalculateEitrRegenRate))),
                            new CodeInstruction(OpCodes.Stloc_S, (byte)11) // store in V_11
                        );
                    }
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateStaminaRegenRate(Player player, float actionFactor)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                // Vanilla fallback calculation
                float maxStam = Mathf.Max(1f, player.GetMaxStamina());
                float dyn = player.m_staminaRegen + (1f - (player.GetStamina() / maxStam)) * player.m_staminaRegen * player.m_staminaRegenTimeMultiplier;
                dyn *= actionFactor;
                float seMult = 1f;
                player.GetSEMan()?.ModifyStaminaRegen(ref seMult);
                return dyn * seMult;
            }

            float regen = CharacterStatManager.GetStat(player, CharacterStatType.StaminaRegen);
            return regen * actionFactor;
        }

        public static float CalculateEitrRegenRate(Player player, float actionFactor)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                // Vanilla fallback calculation
                float maxEitr = Mathf.Max(1f, player.GetMaxEitr());
                float dyn = player.m_eiterRegen + (1f - (player.GetEitr() / maxEitr)) * player.m_eiterRegen;
                dyn *= actionFactor;
                float seMult = 1f;
                player.GetSEMan()?.ModifyEitrRegen(ref seMult);
                seMult += player.GetEquipmentEitrRegenModifier();
                return dyn * seMult;
            }

            float regen = CharacterStatManager.GetStat(player, CharacterStatType.EitrRegen);
            return regen * actionFactor;
        }

        /// <summary>
        /// Neutralizes vanilla 10-second pulsed heal in Player.UpdateFood when continuous health regen is active.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdateFood", new[] { typeof(float), typeof(bool) })]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileUpdateFood(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            // Locate call to Character::Heal(Single, Boolean) at IL_020A
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Character), nameof(Character.Heal), new[] { typeof(float), typeof(bool) })));

            if (matcher.IsValid)
            {
                // Replace call with our intercept that skips healing if continuous regen is active
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(PlayerStatsPatch), nameof(InterceptPulsedFoodHeal))));
            }

            return matcher.InstructionEnumeration();
        }

        public static void InterceptPulsedFoodHeal(Character character, float amount, bool showText)
        {
            // If continuous health regen is active in VikingLib, skip the 10s pulse heal to prevent double healing
            if (ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                return;
            }

            character.Heal(amount, showText);
        }

        /// <summary>
        /// Hooks Player.RPC_UseStamina to govern stamina exhaustion delay via CharacterStatType.StaminaRegenDelay.
        /// </summary>
        [HarmonyPatch(typeof(Player), "RPC_UseStamina")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileRPCUseStamina(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Player), "m_staminaRegenDelay")));

            if (matcher.IsValid)
            {
                matcher.Set(OpCodes.Call, AccessTools.Method(typeof(PlayerStatsPatch), nameof(GetStaminaRegenDelay)));
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetStaminaRegenDelay(Player player)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                return player.m_staminaRegenDelay;
            }

            return CharacterStatManager.GetStat(player, CharacterStatType.StaminaRegenDelay);
        }

        /// <summary>
        /// Evaluates max pools (MaxHealth, MaxStamina, MaxEitr) through 3-tier modifiers including active foods.
        /// </summary>
        [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
        [HarmonyPrefix]
        public static bool PrefixGetTotalFoodValue(Player __instance, ref float hp, ref float stamina, ref float eitr)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;

            VanillaFoodAdapter.SyncFood(__instance);
            hp = CharacterStatManager.GetStat(__instance, CharacterStatType.MaxHealth);
            stamina = CharacterStatManager.GetStat(__instance, CharacterStatType.MaxStamina);
            eitr = CharacterStatManager.GetStat(__instance, CharacterStatType.MaxEitr);
            return false;
        }

        /// <summary>
        /// Synchronizes food modifiers whenever food decays or is updated.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdateFood", new[] { typeof(float), typeof(bool) })]
        [HarmonyPostfix]
        public static void PostfixUpdateFood(Player __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            VanillaFoodAdapter.SyncFood(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ClearFood))]
        [HarmonyPostfix]
        public static void PostfixClearFood(Player __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            VanillaFoodAdapter.SyncFood(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        [HarmonyPostfix]
        public static void PostfixOnSpawned(Player __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            VanillaFoodAdapter.SyncFood(__instance);
            VanillaEquipmentAdapter.SyncEquipment(__instance);
            ResetDamageTimer(__instance);
        }

        /// <summary>
        /// Routes player body armor evaluation through 3-tier modifiers.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
        [HarmonyPrefix]
        public static bool PrefixGetBodyArmor(Player __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;

            __result = CharacterStatManager.GetStat(__instance, CharacterStatType.BodyArmor);
            return false;
        }

        /// <summary>
        /// Evaluates maximum carry weight through 3-tier modifiers.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
        [HarmonyPrefix]
        public static bool PrefixGetMaxCarryWeight(Player __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;

            float limit = CharacterStatManager.GetStat(__instance, CharacterStatType.MaxCarryWeight);
            limit *= Game.m_carryWeightRate;
            __result = limit;
            return false;
        }
    }
}
