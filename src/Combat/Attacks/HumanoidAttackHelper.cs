using HarmonyLib;

namespace VikingLib.Combat.Attacks
{
    /// <summary>
    /// High-performance compiled accessors for private and protected runtime attack fields
    /// on Humanoid and Attack instances.
    /// </summary>
    public static class HumanoidAttackHelper
    {
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttackRef =
            AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");

        private static readonly AccessTools.FieldRef<Humanoid, bool> IsSecondaryRef =
            AccessTools.FieldRefAccess<Humanoid, bool>("m_currentAttackIsSecondary");

        private static readonly AccessTools.FieldRef<Attack, Humanoid> AttackCharacterRef =
            AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");

        private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> AttackWeaponRef =
            AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_weapon");

        private static readonly AccessTools.FieldRef<Attack, int> AttackChainLevelRef =
            AccessTools.FieldRefAccess<Attack, int>("m_currentAttackCainLevel");

        public static Attack? GetCurrentAttack(Humanoid? humanoid) =>
            humanoid != null ? CurrentAttackRef(humanoid) : null;

        public static bool IsCurrentAttackSecondary(Humanoid? humanoid) =>
            humanoid != null && IsSecondaryRef(humanoid);

        public static Humanoid? GetCharacter(Attack? attack) =>
            attack != null ? AttackCharacterRef(attack) : null;

        public static ItemDrop.ItemData? GetWeapon(Attack? attack) =>
            attack != null ? AttackWeaponRef(attack) : null;

        public static int GetCurrentAttackChainLevel(Attack? attack) =>
            attack != null ? AttackChainLevelRef(attack) : 0;
    }
}
