using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat.Attacks;
using VikingLib.Configuration;
using VikingLib.Stats;

namespace VikingLib.Patches
{
    /// <summary>
    /// Scales Hammer stamina costs (piece placement and repair) with <see cref="CharacterStatType.HomeToolsEfficiency"/>.
    /// Hoe and Cultivator costs flow through <c>Attack.GetAttackStamina</c> and are handled by
    /// <see cref="CharacterStatManager.GetEffectiveWeaponAttackCost(float, Character?, ItemDrop.ItemData?, bool, CharacterStatType)"/>.
    /// Only the <c>Character.UseStamina</c> call sites are swapped; the surrounding vanilla logic is untouched.
    /// </summary>
    public static class HomeToolStaminaPatch
    {
        private static readonly MethodInfo UseStaminaMethod = AccessTools.Method(typeof(Character), nameof(Character.UseStamina));
        private static readonly MethodInfo HelperMethod = AccessTools.Method(typeof(HomeToolStaminaPatch), nameof(UseToolStamina));

        public static void UseToolStamina(Character character, float amount)
        {
            if (character != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                amount = CharacterStatManager.GetEffectiveActionCost(amount, character, CharacterStatType.ToolStaminaEfficiency);
            }

            character!.UseStamina(amount);
        }

        private static IEnumerable<CodeInstruction> SwapUseStamina(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) &&
                    instruction.operand is MethodInfo mi && mi == UseStaminaMethod)
                {
                    yield return new CodeInstruction(OpCodes.Call, HelperMethod);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static class PlacementPatch
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => SwapUseStamina(instructions);
        }

        [HarmonyPatch(typeof(Player), "Repair")]
        public static class RepairPatch
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => SwapUseStamina(instructions);
        }
    }

    /// <summary>
    /// Scales food burn rate with <see cref="CharacterStatType.FoodDurationMultiplier"/>.
    /// Food ticks are driven by <c>Player.UpdateFood(dt)</c>, so dividing dt by the multiplier makes food last proportionally longer.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateFood")]
    public static class FoodDurationPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Player __instance, ref float dt, bool forceUpdate)
        {
            if (forceUpdate || dt <= 0f || !ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            if (__instance != Player.m_localPlayer) return;

            float duration = CharacterStatManager.GetStat(__instance, CharacterStatType.FoodDurationMultiplier);
            if (Mathf.Abs(duration - 1f) > 0.001f)
            {
                dt /= Mathf.Max(0.01f, duration);
            }
        }
    }

    /// <summary>
    /// Scales melee reach with <see cref="CharacterStatType.MeleeReach"/>.
    /// Humanoid.StartAttack hands <c>Attack.Start</c> a fresh clone of the weapon's attack, so mutating
    /// <c>m_attackRange</c> on that clone never touches the shared weapon prefab.
    /// Projectile and area attacks are excluded because <c>m_attackRange</c> also positions projectile spawns.
    /// </summary>
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    public static class MeleeReachPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Attack __instance, Humanoid character)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value || character == null) return;
            if (__instance.m_attackType != Attack.AttackType.Horizontal && __instance.m_attackType != Attack.AttackType.Vertical) return;

            float reach = CharacterStatManager.GetStat(character, CharacterStatType.MeleeReach);
            if (Mathf.Abs(reach - 1f) > 0.001f)
            {
                __instance.m_attackRange *= Mathf.Max(0.1f, reach);
            }
        }
    }

    /// <summary>
    /// Scales fall damage with <see cref="CharacterStatType.ResistanceFall"/>.
    /// Vanilla builds a <see cref="HitData.HitType.Fall"/> hit in Character.UpdateGroundContact and sends it through
    /// Character.Damage; the fall amount lives in the untyped <c>m_damage.m_damage</c> field, which the typed pipeline does not track,
    /// so it is scaled here at the source on the owning client.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    public static class FallResistancePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Character __instance, HitData hit)
        {
            if (hit == null || hit.m_hitType != HitData.HitType.Fall) return;
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;

            float taken = CharacterStatManager.GetStat(__instance, CharacterStatType.ResistanceFall);
            if (Mathf.Abs(taken - 1f) > 0.001f)
            {
                hit.m_damage.m_damage *= Mathf.Max(0f, taken);
            }
        }
    }

    /// <summary>
    /// Snapshot of equipped item durabilities taken before a vanilla method that wears equipment down.
    /// Only created when <see cref="CharacterStatType.DurabilityLossMultiplier"/> is not 1.0x, so the common path allocates nothing.
    /// </summary>
    public sealed class DurabilitySnapshot
    {
        private readonly ItemDrop.ItemData[] _items;
        private readonly float[] _before;
        private readonly float _multiplier;

        private DurabilitySnapshot(ItemDrop.ItemData[] items, float[] before, float multiplier)
        {
            _items = items;
            _before = before;
            _multiplier = multiplier;
        }

        public static DurabilitySnapshot? Capture(Humanoid? humanoid)
        {
            if (humanoid == null || !ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return null;
            if (!humanoid.IsOwner()) return null;

            float eff = CharacterStatManager.GetStat(humanoid, CharacterStatType.DurabilityEfficiency);
            if (eff <= 0.001f) return null;

            float multiplier = 1f / Mathf.Max(0.01f, 1f + eff);

            var equipped = humanoid.GetInventory()?.GetEquippedItems();
            if (equipped == null || equipped.Count == 0) return null;

            var items = new List<ItemDrop.ItemData>(equipped.Count);
            foreach (var item in equipped)
            {
                if (item?.m_shared != null && item.m_shared.m_useDurability) items.Add(item);
            }
            if (items.Count == 0) return null;

            var before = new float[items.Count];
            for (int i = 0; i < items.Count; i++) before[i] = items[i].m_durability;
            return new DurabilitySnapshot(items.ToArray(), before, Mathf.Max(0f, multiplier));
        }

        /// <summary>
        /// Rescales each item's durability loss since the snapshot. Only losses are rescaled; gains (repairs) are left untouched.
        /// </summary>
        public void Apply()
        {
            for (int i = 0; i < _items.Length; i++)
            {
                float now = _items[i].m_durability;
                if (now >= _before[i]) continue;

                float loss = _before[i] - now;
                _items[i].m_durability = Mathf.Max(0f, _before[i] - loss * _multiplier);
            }
        }
    }

    /// <summary>
    /// Durability loss scaling for weapon strikes and tool swings (all four vanilla Attack durability sites).
    /// </summary>
    [HarmonyPatch]
    public static class AttackDurabilityPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Attack), "DoMeleeAttack");
            yield return AccessTools.DeclaredMethod(typeof(Attack), "DoAreaAttack");
            yield return AccessTools.DeclaredMethod(typeof(Attack), "ProjectileAttackTriggered");
            yield return AccessTools.DeclaredMethod(typeof(Attack), "DoNonAttack");
        }

        [HarmonyPrefix]
        public static void Prefix(Attack __instance, out DurabilitySnapshot? __state) =>
            __state = DurabilitySnapshot.Capture(HumanoidAttackHelper.GetCharacter(__instance));

        [HarmonyPostfix]
        public static void Postfix(DurabilitySnapshot? __state) => __state?.Apply();
    }

    /// <summary>
    /// Durability loss scaling for shield blocks, per-tick drains, hammer use, and armor wear.
    /// Each target is patched on its declaring type (Humanoid or Player).
    /// </summary>
    [HarmonyPatch]
    public static class EquipmentDurabilityPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Humanoid), "BlockAttack");
            yield return AccessTools.DeclaredMethod(typeof(Humanoid), "DrainEquipedItemDurability");
            yield return AccessTools.DeclaredMethod(typeof(Player), "UpdatePlacement");
            yield return AccessTools.DeclaredMethod(typeof(Player), "DamageArmorDurability");
        }

        [HarmonyPrefix]
        public static void Prefix(Humanoid __instance, out DurabilitySnapshot? __state) =>
            __state = DurabilitySnapshot.Capture(__instance);

        [HarmonyPostfix]
        public static void Postfix(DurabilitySnapshot? __state) => __state?.Apply();
    }
}
