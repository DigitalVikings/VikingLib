using HarmonyLib;
using VikingLib.Combat;
using VikingLib.Configuration;

namespace VikingLib.Patches
{
    /// <summary>
    /// Intercepts piece and structure damage via WearNTear.RPC_Damage to route hits through VikingLib's DamagePipeline.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    public static class WearNTearCombatPatch
    {
        [HarmonyPrefix]
        public static void Prefix(WearNTear __instance, HitData hit, out DamageContext? __state)
        {
            __state = null;

            if (!ModConfig.ModEnabled.Value || hit == null || __instance == null) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            __state = DamagePipeline.ProcessPieceHit(__instance, hit);

            if (__state.IsCancelled)
            {
                hit.m_damage = default;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(DamageContext? __state)
        {
            if (__state != null)
            {
                DamagePipeline.FinalizeHit(__state);
            }
        }
    }
}
