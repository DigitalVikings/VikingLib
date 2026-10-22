using HarmonyLib;
using VikingLib.Combat;

namespace VikingLib.Patches
{
    /// <summary>
    /// Prevents vanilla's monolithic ApplyResistance and ApplyArmor from double-mitigating hits
    /// when VikingLib's DamagePipeline has already evaluated granular mitigations.
    /// </summary>
    [HarmonyPatch]
    public static class NativeMitigationBypassPatch
    {
        [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
        [HarmonyPrefix]
        public static bool PrefixApplyResistance(ref HitData.DamageModifier significantModifier)
        {
            if (DamagePipeline.IsActive)
            {
                // Assign the significant modifier computed by VikingLib for native damage text color
                significantModifier = DamagePipeline.CurrentSignificantModifier;
                return false; // Skip vanilla monolithic resistance loop
            }
            return true;
        }

        [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyArmor))]
        [HarmonyPrefix]
        public static bool PrefixApplyArmor(float ac)
        {
            if (DamagePipeline.IsActive)
            {
                // Skip vanilla physical-only piecewise armor, as VikingLib has applied granular armor
                return false;
            }
            return true;
        }
    }
}
