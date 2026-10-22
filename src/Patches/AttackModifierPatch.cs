using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Combat.Attacks;
using VikingLib.Configuration;

namespace VikingLib.Patches
{
    /// <summary>
    /// Neutralizes vanilla Valheim's native attack damage multipliers (hardcoded 2x finisher and m_damageMultiplier)
    /// and captures attack step metadata so VikingLib's 3-Tier modifier pipeline can take full ownership.
    /// </summary>
    [HarmonyPatch]
    public static class AttackModifierPatch
    {
        private static readonly ConditionalWeakTable<Projectile, WeaponAttackInfo> _projectileAttacks = new();

        [ThreadStatic]
        private static WeaponAttackInfo? _currentProjectileAttack;

        /// <summary>
        /// Neutralizes vanilla m_damageMultiplier inside Attack.ModifyDamage and registers attack info.
        /// </summary>
        [HarmonyPatch(typeof(Attack), "ModifyDamage")]
        [HarmonyPrefix]
        public static void PrefixModifyDamage(Attack __instance, HitData hitData, out float __state)
        {
            __state = __instance.m_damageMultiplier;

            if (!ModConfig.ModEnabled.Value || hitData == null) return;

            // Neutralize native multiplier so vanilla does not pre-bake it into HitData
            __instance.m_damageMultiplier = 1.0f;

            // Determine attack step key and human-readable display name
            var character = HumanoidAttackHelper.GetCharacter(__instance);
            bool isSecondary = HumanoidAttackHelper.IsCurrentAttackSecondary(character);
            int chainLevel = HumanoidAttackHelper.GetCurrentAttackChainLevel(__instance);
            var weapon = HumanoidAttackHelper.GetWeapon(__instance);

            string stepKey;
            string displayName;

            if (isSecondary)
            {
                stepKey = "secondary";
                displayName = "Secondary Attack";
            }
            else
            {
                int step = chainLevel + 1;
                stepKey = $"primary_{step}";
                bool isFinisher = __instance.m_attackChainLevels > 0 && chainLevel == __instance.m_attackChainLevels - 1;
                displayName = isFinisher
                    ? $"Combo Finisher (Hit {step}/{__instance.m_attackChainLevels})"
                    : $"Primary Hit {step}";
            }

            var profile = WeaponAttackRegistry.GetProfile(weapon);
            var stepMods = profile.GetStep(stepKey);

            // Scale base damage directly on HitData at attack origin for this specific attack step
            ApplyAttackStepBaseDamage(ref hitData.m_damage, stepMods);

            if (WeaponAttackRegistry.TryGetHitAttack(hitData, out var existingInfo))
            {
                existingInfo.StepKey = stepKey;
                existingInfo.DisplayName = displayName;
                existingInfo.Weapon = weapon;
                existingInfo.Profile = profile;
            }
            else
            {
                var info = new WeaponAttackInfo(stepKey, displayName, weapon, profile);
                WeaponAttackRegistry.RegisterHitAttack(hitData, info);
            }
        }

        [HarmonyPatch(typeof(Attack), "ModifyDamage")]
        [HarmonyPostfix]
        public static void PostfixModifyDamage(Attack __instance, float __state)
        {
            __instance.m_damageMultiplier = __state;
        }

        /// <summary>
        /// Neutralizes m_lastChainDamageMultiplier inside DoAreaAttack so it does not double-multiply.
        /// </summary>
        [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
        [HarmonyPrefix]
        public static void PrefixDoAreaAttack(Attack __instance, out float __state)
        {
            __state = __instance.m_lastChainDamageMultiplier;
            if (ModConfig.ModEnabled.Value)
            {
                __instance.m_lastChainDamageMultiplier = 1.0f;
            }
        }

        [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
        [HarmonyPostfix]
        public static void PostfixDoAreaAttack(Attack __instance, float __state)
        {
            __instance.m_lastChainDamageMultiplier = __state;
        }

        /// <summary>
        /// Returns 1.0x if VikingLib is enabled (so pipeline owns combo finisher scaling),
        /// or 2.0x if disabled (vanilla behavior).
        /// </summary>
        public static float GetMeleeComboMultiplier()
        {
            return ModConfig.ModEnabled.Value ? 1.0f : 2.0f;
        }

        /// <summary>
        /// Determines whether vanilla's native multi-hit damage division (factor /= hitPoints.Count * 0.75f)
        /// should execute. Returns false when VikingLib is active to let the 3-Tier pipeline apply configurable cleave scaling.
        /// </summary>
        public static bool ShouldVanillaLowerDamagePerHit(Attack attack)
        {
            if (ModConfig.ModEnabled.Value)
            {
                return false;
            }
            return attack.m_lowerDamagePerHit;
        }

        /// <summary>
        /// Analyzes the melee sweep's hit points, identifies blade-sweep ordering,
        /// calculates pickaxe chunk split and multi-target counts, and registers runtime
        /// WeaponAttackInfo metadata onto HitData without modifying native HitData fields.
        /// </summary>
        public static void AssignHitTargetCount(Attack attack, HitData hitData, List<Attack.HitPoint> hitPoints, IDestructible? target, Attack.HitPoint hitPoint)
        {
            if (!ModConfig.ModEnabled.Value || hitData == null || hitPoints == null || attack == null) return;

            bool isPickaxe = attack.m_pickaxeSpecial;
            int pickaxeChunkCount = 0;
            if (isPickaxe)
            {
                for (int i = 0; i < hitPoints.Count; i++)
                {
                    var go = hitPoints[i].go;
                    if (go != null && (go.GetComponent<MineRock5>() != null || go.GetComponent<MineRock>() != null))
                    {
                        pickaxeChunkCount++;
                    }
                }
            }

            bool includeTerrain = ModConfig.TerrainReducesCleaveDamage.Value;

            // Count valid hit targets: IDestructibles, and optionally Heightmap if configured
            var validHitPoints = new List<Attack.HitPoint>();
            for (int i = 0; i < hitPoints.Count; i++)
            {
                var hp = hitPoints[i];
                if (hp.go != null)
                {
                    if (hp.go.GetComponent<IDestructible>() != null)
                    {
                        validHitPoints.Add(hp);
                    }
                    else if (includeTerrain && hp.go.GetComponent<Heightmap>() != null)
                    {
                        validHitPoints.Add(hp);
                    }
                }
            }

            int count = validHitPoints.Count;
            if (count < 1) count = 1;

            int index = validHitPoints.IndexOf(hitPoint);
            if (index < 0)
            {
                if (target is Component comp)
                {
                    var go = comp.gameObject;
                    for (int i = 0; i < validHitPoints.Count; i++)
                    {
                        var hp = validHitPoints[i];
                        // Exact collider match first (essential for multi-collider objects like MineRock5)
                        if (hitData.m_hitCollider != null && hp.collider == hitData.m_hitCollider)
                        {
                            index = i;
                            break;
                        }
                        if (hp.go == go)
                        {
                            index = i;
                            break;
                        }
                    }
                }
            }
            if (index < 0) index = 0;

            bool isTargetMineRock = target is Component c && (c.GetComponent<MineRock5>() != null || c.GetComponent<MineRock>() != null);
            bool isPickaxeChunkHit = isPickaxe && pickaxeChunkCount > 0 && isTargetMineRock;

            if (WeaponAttackRegistry.TryGetHitAttack(hitData, out var info))
            {
                info.TargetCount = count;
                info.TargetIndex = index;
                info.IsPickaxeSpecial = isPickaxeChunkHit;
                info.PickaxeChunkCount = pickaxeChunkCount;
            }
            else
            {
                var weapon = HumanoidAttackHelper.GetWeapon(attack);
                var profile = WeaponAttackRegistry.GetProfile(weapon);
                var character = HumanoidAttackHelper.GetCharacter(attack);
                string stepKey = HumanoidAttackHelper.IsCurrentAttackSecondary(character) ? "secondary" : "primary_1";
                info = new WeaponAttackInfo(stepKey, "Primary Hit", weapon, profile)
                {
                    TargetCount = count,
                    TargetIndex = index,
                    IsPickaxeSpecial = isPickaxeChunkHit,
                    PickaxeChunkCount = pickaxeChunkCount
                };
                WeaponAttackRegistry.RegisterHitAttack(hitData, info);
            }
        }

        /// <summary>
        /// Emits a VikingLib combat damage trace when a melee swing strikes terrain/ground.
        /// Accounts for the blade-sweep cleave progression if TerrainReducesCleaveDamage is enabled.
        /// </summary>
        public static void LogTerrainHit(Attack attack, GameObject terrainGo, Attack.HitPoint hitPoint, List<Attack.HitPoint> hitPoints)
        {
            if (!ModConfig.ModEnabled.Value || attack == null || terrainGo == null || hitPoints == null) return;

            var attacker = HumanoidAttackHelper.GetCharacter(attack);
            if (!DamagePipeline.ShouldLogCombatTrace(attacker, null)) return;

            bool includeTerrain = ModConfig.TerrainReducesCleaveDamage.Value;

            var validHitPoints = new List<Attack.HitPoint>();
            for (int i = 0; i < hitPoints.Count; i++)
            {
                var hp = hitPoints[i];
                if (hp.go != null)
                {
                    if (hp.go.GetComponent<IDestructible>() != null)
                    {
                        validHitPoints.Add(hp);
                    }
                    else if (includeTerrain && hp.go.GetComponent<Heightmap>() != null)
                    {
                        validHitPoints.Add(hp);
                    }
                }
            }

            int count = validHitPoints.Count;
            if (count < 1) count = 1;

            int index = validHitPoints.IndexOf(hitPoint);
            if (index < 0)
            {
                if (includeTerrain)
                {
                    for (int i = 0; i < validHitPoints.Count; i++)
                    {
                        var hp = validHitPoints[i];
                        if (hitPoint.collider != null && hp.collider == hitPoint.collider)
                        {
                            index = i;
                            break;
                        }
                        if (hp.go == terrainGo)
                        {
                            index = i;
                            break;
                        }
                    }
                }
            }
            if (index < 0) index = 0;

            var weapon = HumanoidAttackHelper.GetWeapon(attack);
            var profile = WeaponAttackRegistry.GetProfile(weapon);
            string stepKey = HumanoidAttackHelper.IsCurrentAttackSecondary(attacker) ? "secondary" : "primary_1";

            HitData hitData = new HitData();
            hitData.m_point = hitPoint.count > 0 ? (hitPoint.avgPoint / hitPoint.count) : hitPoint.firstPoint;
            hitData.m_hitCollider = hitPoint.collider;
            if (attacker != null) hitData.SetAttacker(attacker);
            hitData.m_damage = weapon != null ? weapon.GetDamage() : default;
            hitData.m_skill = weapon != null ? weapon.m_shared.m_skillType : Skills.SkillType.None;

            var info = new WeaponAttackInfo(stepKey, "Terrain Hit", weapon, profile)
            {
                TargetCount = count,
                TargetIndex = index
            };
            WeaponAttackRegistry.RegisterHitAttack(hitData, info);

            InvokeModifyDamage(attack, hitData, 1.0f);

            DamagePipeline.ProcessTerrainHit(attacker, hitData, "Terrain (Ground)");
        }

        private static readonly Action<Attack, HitData, float> InvokeModifyDamage =
            AccessTools.MethodDelegate<Action<Attack, HitData, float>>(
                AccessTools.Method(typeof(Attack), "ModifyDamage", new[] { typeof(HitData), typeof(float) }));

        /// <summary>
        /// Scales damage types by the attack step's multipliers as base damage at attack origin.
        /// </summary>
        public static void ApplyAttackStepBaseDamage(ref HitData.DamageTypes damage, AttackStepModifiers stepMods)
        {
            damage.m_blunt *= stepMods.GetMultiplier(DamageType.Blunt);
            damage.m_slash *= stepMods.GetMultiplier(DamageType.Slash);
            damage.m_pierce *= stepMods.GetMultiplier(DamageType.Pierce);
            damage.m_chop *= stepMods.GetMultiplier(DamageType.Chop);
            damage.m_pickaxe *= stepMods.GetMultiplier(DamageType.Pickaxe);
            damage.m_fire *= stepMods.GetMultiplier(DamageType.Fire);
            damage.m_frost *= stepMods.GetMultiplier(DamageType.Frost);
            damage.m_lightning *= stepMods.GetMultiplier(DamageType.Lightning);
            damage.m_poison *= stepMods.GetMultiplier(DamageType.Poison);
            damage.m_spirit *= stepMods.GetMultiplier(DamageType.Spirit);
            damage.m_damage *= stepMods.GetMultiplier(DamageType.TrueDamage);

            if (stepMods.OverrideDamageType.HasValue)
            {
                var targetType = stepMods.OverrideDamageType.Value;
                float physicalSum = damage.m_blunt + damage.m_slash + damage.m_pierce;
                damage.m_blunt = 0f;
                damage.m_slash = 0f;
                damage.m_pierce = 0f;
                switch (targetType)
                {
                    case DamageType.Blunt: damage.m_blunt = physicalSum; break;
                    case DamageType.Slash: damage.m_slash = physicalSum; break;
                    case DamageType.Pierce: damage.m_pierce = physicalSum; break;
                    case DamageType.Fire: damage.m_fire += physicalSum; break;
                    case DamageType.Frost: damage.m_frost += physicalSum; break;
                    case DamageType.Lightning: damage.m_lightning += physicalSum; break;
                    case DamageType.Poison: damage.m_poison += physicalSum; break;
                    case DamageType.Spirit: damage.m_spirit += physicalSum; break;
                    case DamageType.TrueDamage: damage.m_damage += physicalSum; break;
                    case DamageType.Chop: damage.m_chop += physicalSum; break;
                    case DamageType.Pickaxe: damage.m_pickaxe += physicalSum; break;
                }
            }
        }

        private static CodeInstruction CreateMatchingLdloc(CodeInstruction sourceInst)
        {
            if (sourceInst.opcode == OpCodes.Stloc_S || sourceInst.opcode == OpCodes.Ldloc_S)
                return new CodeInstruction(OpCodes.Ldloc_S, sourceInst.operand);
            if (sourceInst.opcode == OpCodes.Stloc_0 || sourceInst.opcode == OpCodes.Ldloc_0)
                return new CodeInstruction(OpCodes.Ldloc_0);
            if (sourceInst.opcode == OpCodes.Stloc_1 || sourceInst.opcode == OpCodes.Ldloc_1)
                return new CodeInstruction(OpCodes.Ldloc_1);
            if (sourceInst.opcode == OpCodes.Stloc_2 || sourceInst.opcode == OpCodes.Ldloc_2)
                return new CodeInstruction(OpCodes.Ldloc_2);
            if (sourceInst.opcode == OpCodes.Stloc_3 || sourceInst.opcode == OpCodes.Ldloc_3)
                return new CodeInstruction(OpCodes.Ldloc_3);

            return new CodeInstruction(OpCodes.Ldloc, sourceInst.operand);
        }

        /// <summary>
        /// Transpiles Attack.DoMeleeAttack:
        /// 1. Replaces hardcoded ldc.r4 2 (2.0x combo finisher) with GetMeleeComboMultiplier().
        /// 2. Replaces ldfld Attack::m_lowerDamagePerHit with ShouldVanillaLowerDamagePerHit(attack) to bypass vanilla division.
        /// 3. Injects AssignHitTargetCount after HitData.m_hitCollider store to pass target count across RPC.
        /// 4. Injects LogTerrainHit when hitting terrain/ground to log absorbed cleave step telemetry.
        /// </summary>
        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileDoMeleeAttack(IEnumerable<CodeInstruction> instructions)
        {
            var modifyMethod = AccessTools.Method(typeof(HitData.DamageTypes), nameof(HitData.DamageTypes.Modify), new[] { typeof(float) });
            var comboHelper = AccessTools.Method(typeof(AttackModifierPatch), nameof(GetMeleeComboMultiplier));
            var lowerDmgField = AccessTools.Field(typeof(Attack), nameof(Attack.m_lowerDamagePerHit));
            var shouldLowerMethod = AccessTools.Method(typeof(AttackModifierPatch), nameof(ShouldVanillaLowerDamagePerHit));
            var getCountMethod = AccessTools.PropertyGetter(typeof(List<Attack.HitPoint>), nameof(List<Attack.HitPoint>.Count));
            var getDestructibleMethod = AccessTools.Method(typeof(GameObject), nameof(GameObject.GetComponent), Type.EmptyTypes, new[] { typeof(IDestructible) });
            var getHeightmapMethod = AccessTools.Method(typeof(GameObject), nameof(GameObject.GetComponent), Type.EmptyTypes, new[] { typeof(Heightmap) });
            var hitColliderField = AccessTools.Field(typeof(HitData), nameof(HitData.m_hitCollider));
            var hitPointGoField = AccessTools.Field(typeof(Attack.HitPoint), nameof(Attack.HitPoint.go));

            var assignTargetCountMethod = AccessTools.Method(
                typeof(AttackModifierPatch),
                nameof(AssignHitTargetCount),
                new[] { typeof(Attack), typeof(HitData), typeof(List<Attack.HitPoint>), typeof(IDestructible), typeof(Attack.HitPoint) });

            var logTerrainHitMethod = AccessTools.Method(
                typeof(AttackModifierPatch),
                nameof(LogTerrainHit),
                new[] { typeof(Attack), typeof(GameObject), typeof(Attack.HitPoint), typeof(List<Attack.HitPoint>) });

            var list = new List<CodeInstruction>(instructions);
            CodeInstruction? hitPointsInst = null;
            CodeInstruction? destructibleInst = null;
            CodeInstruction? hitDataStoreInst = null;
            CodeInstruction? hitPointStoreInst = null;
            CodeInstruction? targetGoStoreInst = null;
            int hitColliderStoreIndex = -1;
            int hitTerrainDoneStoreIndex = -1;

            for (int i = 0; i < list.Count; i++)
            {
                // Capture destructible local (V_34) stored after GetComponent<IDestructible>()
                if (list[i].Calls(getDestructibleMethod) && i + 1 < list.Count && list[i + 1].IsStloc())
                {
                    destructibleInst = list[i + 1];
                }

                // Capture hitPoints local (V_9) loaded right before List<HitPoint>::get_Count()
                if (i + 1 < list.Count && list[i + 1].Calls(getCountMethod) && list[i].IsLdloc())
                {
                    hitPointsInst = list[i];
                }

                // Capture current HitPoint local store (V_30) right after get_Current of HitPoint enumerator
                if (list[i].operand is MethodInfo mi && mi.Name == "get_Current" && mi.ReturnType == typeof(Attack.HitPoint) && i + 1 < list.Count && list[i + 1].IsStloc())
                {
                    hitPointStoreInst = list[i + 1];
                }

                // Capture target GameObject local store (V_31) right after ldfld Attack.HitPoint::go
                if (list[i].LoadsField(hitPointGoField) && i + 1 < list.Count && list[i + 1].IsStloc())
                {
                    targetGoStoreInst = list[i + 1];
                }

                // Neutralize vanilla m_lowerDamagePerHit check
                if (list[i].LoadsField(lowerDmgField))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, shouldLowerMethod);
                }

                // Capture HitData local store (V_39) right after newobj HitData::.ctor()
                if (list[i].opcode == OpCodes.Newobj && list[i].operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(HitData)
                    && i + 1 < list.Count && list[i + 1].IsStloc())
                {
                    hitDataStoreInst = list[i + 1];
                }

                // Capture where hitData.m_hitCollider is assigned
                if (list[i].StoresField(hitColliderField))
                {
                    hitColliderStoreIndex = i;
                }

                // Capture where hitTerrainDone (V_14) is set to true after GetComponent<Heightmap>()
                if (list[i].Calls(getHeightmapMethod))
                {
                    for (int j = i; j < i + 25 && j < list.Count; j++)
                    {
                        if (list[j].opcode == OpCodes.Ldc_I4_1 && j + 1 < list.Count && list[j + 1].IsStloc())
                        {
                            hitTerrainDoneStoreIndex = j + 1;
                            break;
                        }
                    }
                }

                // Neutralize 2.0x combo finisher
                if (list[i].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)list[i].operand, 2.0f) &&
                    i + 1 < list.Count && list[i + 1].Calls(modifyMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, comboHelper);
                }
            }

            // 1. Inject LogTerrainHit(attack, targetGo, hitPoint, hitPoints) after hitTerrainDone store
            // (Inject this first so earlier hitColliderStoreIndex does not shift)
            if (hitTerrainDoneStoreIndex >= 0 && targetGoStoreInst != null && hitPointStoreInst != null && hitPointsInst != null)
            {
                var injectTerrain = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    CreateMatchingLdloc(targetGoStoreInst),
                    CreateMatchingLdloc(hitPointStoreInst),
                    CreateMatchingLdloc(hitPointsInst),
                    new CodeInstruction(OpCodes.Call, logTerrainHitMethod)
                };
                list.InsertRange(hitTerrainDoneStoreIndex + 1, injectTerrain);
            }

            // 2. Inject AssignHitTargetCount(attack, hitData, hitPoints, destructible, hitPoint) after hitData.m_hitCollider store
            if (hitColliderStoreIndex >= 0 && hitDataStoreInst != null && hitPointsInst != null && destructibleInst != null && hitPointStoreInst != null)
            {
                var injectCleave = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    CreateMatchingLdloc(hitDataStoreInst),
                    CreateMatchingLdloc(hitPointsInst),
                    CreateMatchingLdloc(destructibleInst),
                    CreateMatchingLdloc(hitPointStoreInst),
                    new CodeInstruction(OpCodes.Call, assignTargetCountMethod)
                };
                list.InsertRange(hitColliderStoreIndex + 1, injectCleave);
            }

            return list;
        }

        /// <summary>
        /// Passes WeaponAttackInfo from HitData to spawned Projectile instances.
        /// </summary>
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        [HarmonyPostfix]
        public static void PostfixProjectileSetup(Projectile __instance, HitData hitData)
        {
            if (hitData != null && WeaponAttackRegistry.TryGetHitAttack(hitData, out var info))
            {
                _projectileAttacks.Remove(__instance);
                _projectileAttacks.Add(__instance, info);
            }
        }

        /// <summary>
        /// Propagates WeaponAttackInfo when Projectile executes OnHit.
        /// </summary>
        [HarmonyPatch(typeof(Projectile), "OnHit")]
        [HarmonyPrefix]
        public static void PrefixProjectileOnHit(Projectile __instance)
        {
            if (_projectileAttacks.TryGetValue(__instance, out var info))
            {
                _currentProjectileAttack = info;
            }
        }

        [HarmonyPatch(typeof(Projectile), "OnHit")]
        [HarmonyPostfix]
        public static void PostfixProjectileOnHit()
        {
            _currentProjectileAttack = null;
        }

        /// <summary>
        /// Captures Projectile attack info on newly generated HitData when Character.Damage is called.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        [HarmonyPrefix]
        public static void PrefixCharacterDamage(HitData hit)
        {
            if (hit != null && _currentProjectileAttack != null && !WeaponAttackRegistry.TryGetHitAttack(hit, out _))
            {
                WeaponAttackRegistry.RegisterHitAttack(hit, _currentProjectileAttack);
            }
        }

        /// <summary>
        /// Scales attack animation playback speed using CharacterStatType.AttackSpeed and
        /// weapon-specific attack speed stats (e.g. AttackSpeedSwords, AttackSpeedClubs).
        /// ZSyncAnimation automatically synchronizes this speed across the network via ZDOVars.s_animationSpeed.
        /// </summary>
        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        [HarmonyPostfix]
        public static void PostfixAttackStart(Attack __instance, bool __result, Humanoid character, ZSyncAnimation zanim)
        {
            if (!ModConfig.ModEnabled.Value || !__result || character == null || zanim == null) return;

            float speed = Stats.CharacterStatManager.GetStat(character, Stats.CharacterStatType.AttackSpeed);

            var weapon = __instance.GetWeapon() ?? character.GetCurrentWeapon();
            if (weapon?.m_shared != null)
            {
                var wepSpeedStat = Stats.CharacterStatManager.GetWeaponAttackSpeedStat(weapon.m_shared.m_skillType);
                if (wepSpeedStat.HasValue)
                {
                    float weaponMultiplier = Stats.CharacterStatManager.GetStat(character, wepSpeedStat.Value);
                    speed *= weaponMultiplier;
                }
            }

            speed = Mathf.Max(0.1f, speed);
            if (Mathf.Abs(speed - 1f) > 0.001f)
            {
                zanim.SetSpeed(speed);
            }
        }

        /// <summary>
        /// Resets animation playback speed when the attack finishes or is aborted.
        /// </summary>
        [HarmonyPatch(typeof(Attack), nameof(Attack.Stop))]
        [HarmonyPostfix]
        public static void PostfixAttackStop(Attack __instance)
        {
            if (!ModConfig.ModEnabled.Value || __instance == null) return;

            var character = HumanoidAttackHelper.GetCharacter(__instance);
            if (character != null)
            {
                var zanim = character.GetZAnim();
                if (zanim != null)
                {
                    zanim.SetSpeed(1f);
                }
            }
        }
    }
}
