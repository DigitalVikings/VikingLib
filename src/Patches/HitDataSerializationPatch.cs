using HarmonyLib;
using VikingLib.Combat.Attacks;

namespace VikingLib.Patches
{
    /// <summary>
    /// Extends Valheim's native HitData binary serialization protocol to carry rich VikingLib
    /// combat metadata (attack step keys, blade sweep target indexing, pickaxe chunk splitting)
    /// across network RPCs and local ZRoutedRpc execution passes.
    /// </summary>
    [HarmonyPatch]
    public static class HitDataSerializationPatch
    {
        /// <summary>
        /// 4-byte magic identifier "VKLB" (0x564B4C42) marking the start of VikingLib hit metadata.
        /// </summary>
        public const int VikingLibHitMagic = 0x564B4C42;

        /// <summary>
        /// Current binary protocol schema version.
        /// </summary>
        public const byte ProtocolVersion = 1;

        /// <summary>
        /// Appends VikingLib hit metadata to the ZPackage stream during serialization.
        /// </summary>
        [HarmonyPatch(typeof(HitData), nameof(HitData.Serialize))]
        [HarmonyPostfix]
        public static void PostfixSerialize(HitData __instance, ref ZPackage pkg)
        {
            if (pkg == null) return;

            if (WeaponAttackRegistry.TryGetHitAttack(__instance, out var info))
            {
                pkg.Write(VikingLibHitMagic);
                pkg.Write(ProtocolVersion);
                info.Serialize(pkg);
            }
            else
            {
                pkg.Write(0); // Zero marker indicates no extension payload attached
            }
        }

        /// <summary>
        /// Inspects the ZPackage stream after native HitData deserialization and restores
        /// the attached WeaponAttackInfo metadata onto the newly instantiated HitData.
        /// </summary>
        [HarmonyPatch(typeof(HitData), nameof(HitData.Deserialize))]
        [HarmonyPostfix]
        public static void PostfixDeserialize(HitData __instance, ref ZPackage pkg)
        {
            if (pkg == null) return;

            int curPos = pkg.GetPos();
            if (curPos + 4 <= pkg.Size())
            {
                int magic = pkg.ReadInt();
                if (magic == VikingLibHitMagic)
                {
                    byte version = pkg.ReadByte();
                    var info = WeaponAttackInfo.Deserialize(pkg, version, __instance);
                    if (info != null)
                    {
                        WeaponAttackRegistry.RegisterHitAttack(__instance, info);
                    }
                }
                else if (magic != 0)
                {
                    // Stream did not contain a VikingLib header; rewind so downstream RPC parameters are untouched
                    pkg.SetPos(curPos);
                }
            }
        }

        /// <summary>
        /// Automatically duplicates WeaponAttackInfo whenever HitData.Clone() is executed,
        /// ensuring each target clone in a melee sweep receives an isolated copy of attack metadata.
        /// </summary>
        [HarmonyPatch(typeof(HitData), nameof(HitData.Clone))]
        [HarmonyPostfix]
        public static void PostfixClone(HitData __instance, HitData __result)
        {
            if (__instance != null && __result != null && WeaponAttackRegistry.TryGetHitAttack(__instance, out var info))
            {
                WeaponAttackRegistry.RegisterHitAttack(__result, info.Clone());
            }
        }
    }
}
