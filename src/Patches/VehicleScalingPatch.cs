using HarmonyLib;
using UnityEngine;
using VikingLib.Configuration;
using VikingLib.Stats;

namespace VikingLib.Patches
{
    /// <summary>
    /// Scales ship propulsion with the controlling player's <see cref="CharacterStatType.ShipSailSpeed"/> and <see cref="CharacterStatType.ShipRudderSpeed"/>.
    /// <para>
    /// Ship physics run on the ship's owning client, which Valheim assigns to the player at the helm, so the helmsman's
    /// locally registered stats are the ones evaluated. Remote helmsmen (whose modifiers don't exist on this client) evaluate to the 1.0x baseline.
    /// </para>
    /// The ship's tuning fields are swapped for the duration of one physics step and restored in the postfix,
    /// leaving the shared prefab values untouched.
    /// </summary>
    public static class ShipSpeedPatch
    {
        private static readonly AccessTools.FieldRef<Ship, float> SailForceFactorRef = AccessTools.FieldRefAccess<Ship, float>(nameof(Ship.m_sailForceFactor));
        private static readonly AccessTools.FieldRef<Ship, float> BackwardForceRef = AccessTools.FieldRefAccess<Ship, float>(nameof(Ship.m_backwardForce));
        private static readonly AccessTools.FieldRef<Ship, float> RudderSpeedRef = AccessTools.FieldRefAccess<Ship, float>(nameof(Ship.m_rudderSpeed));

        internal static Player? GetHelmsman(Ship ship)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return null;
            if (ship == null || ship.m_shipControlls == null) return null;

            long userId = ship.m_shipControlls.GetUser();
            if (userId == 0L) return null;

            return Player.GetPlayer(userId);
        }

        internal static float GetSailSpeed(Ship ship)
        {
            var helmsman = GetHelmsman(ship);
            if (helmsman == null) return 1f;

            float speed = CharacterStatManager.GetStat(helmsman, CharacterStatType.ShipSailSpeed);
            return Mathf.Abs(speed - 1f) > 0.001f ? Mathf.Max(0.01f, speed) : 1f;
        }

        internal static float GetRudderSpeed(Ship ship)
        {
            var helmsman = GetHelmsman(ship);
            if (helmsman == null) return 1f;

            float speed = CharacterStatManager.GetStat(helmsman, CharacterStatType.ShipRudderSpeed);
            return Mathf.Abs(speed - 1f) > 0.001f ? Mathf.Max(0.01f, speed) : 1f;
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
        public static class FixedUpdatePatch
        {
            [HarmonyPrefix]
            public static void Prefix(Ship __instance, out (float sail, float backward) __state)
            {
                __state = (SailForceFactorRef(__instance), BackwardForceRef(__instance));

                float sailSpeed = GetSailSpeed(__instance);
                float rudderSpeed = GetRudderSpeed(__instance);

                if (Mathf.Abs(sailSpeed - 1f) > 0.001f)
                {
                    SailForceFactorRef(__instance) = __state.sail * sailSpeed;
                }

                if (Mathf.Abs(rudderSpeed - 1f) > 0.001f)
                {
                    BackwardForceRef(__instance) = __state.backward * rudderSpeed;
                }
            }

            [HarmonyPostfix]
            public static void Postfix(Ship __instance, (float sail, float backward) __state)
            {
                SailForceFactorRef(__instance) = __state.sail;
                BackwardForceRef(__instance) = __state.backward;
            }
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.ApplyControlls))]
        public static class ApplyControllsPatch
        {
            [HarmonyPrefix]
            public static void Prefix(Ship __instance, out float __state)
            {
                __state = RudderSpeedRef(__instance);

                float speed = GetRudderSpeed(__instance);
                if (Mathf.Abs(speed - 1f) > 0.001f)
                {
                    RudderSpeedRef(__instance) = __state * speed;
                }
            }

            [HarmonyPostfix]
            public static void Postfix(Ship __instance, float __state) => RudderSpeedRef(__instance) = __state;
        }
    }

    /// <summary>
    /// Scales how much cargo weight contributes to cart inertia with the puller's <see cref="CharacterStatType.CartWeightEfficiency"/>.
    /// Higher efficiency reduces effective cargo mass: Mass = Base / (1 + Eff).
    /// The instance's <c>m_itemWeightMassFactor</c> is swapped only for the duration of
    /// <c>Vagon.UpdateMass</c>, which runs on the cart owner (the puller, who requests ownership when attaching).
    /// </summary>
    [HarmonyPatch(typeof(Vagon), "UpdateMass")]
    public static class CartWeightPatch
    {
        private static readonly AccessTools.FieldRef<Vagon, GameObject> AttachedObjectRef = AccessTools.FieldRefAccess<Vagon, GameObject>("m_attachedObject");

        [HarmonyPrefix]
        public static void Prefix(Vagon __instance, out float __state)
        {
            __state = __instance.m_itemWeightMassFactor;

            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;

            var attached = AttachedObjectRef(__instance);
            if (attached == null) return;

            var puller = attached.GetComponent<Character>();
            if (puller == null) return;

            float eff = CharacterStatManager.GetStat(puller, CharacterStatType.CartWeightEfficiency);
            if (eff > 0.001f)
            {
                __instance.m_itemWeightMassFactor = __state / Mathf.Max(0.01f, 1f + eff);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Vagon __instance, float __state) => __instance.m_itemWeightMassFactor = __state;
    }
}
