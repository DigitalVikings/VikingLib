using System.Collections.Generic;
using HarmonyLib;

namespace VikingLib.Stats
{
    /// <summary>
    /// High-performance cached FieldRef accessors for private and protected runtime fields
    /// on Character, Humanoid, SEMan, and TextsDialog.
    /// </summary>
    public static class CharacterStatsHelper
    {
        // --- Character Accessors ---
        private static readonly AccessTools.FieldRef<Character, ZNetView?> CharacterNViewRef =
            AccessTools.FieldRefAccess<Character, ZNetView?>("m_nview");

        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation?> CharacterZAnimRef =
            AccessTools.FieldRefAccess<Character, ZSyncAnimation?>("m_zanim");

        public static ZNetView? GetNView(Character? character) =>
            character != null ? CharacterNViewRef(character) : null;

        public static ZSyncAnimation? GetZAnim(Character? character) =>
            character != null ? CharacterZAnimRef(character) : null;

        // --- Humanoid Equipment Slot Accessors ---
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> RightItemRef =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_rightItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> LeftItemRef =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_leftItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> ChestItemRef =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_chestItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> LegItemRef =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_legItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HelmetItemRef =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_helmetItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> ShoulderItemRef =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_shoulderItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> UtilityItemRef =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_utilityItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> TrinketItemRef =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_trinketItem");

        public static ItemDrop.ItemData? GetRightItem(Humanoid? humanoid) =>
            humanoid != null ? RightItemRef(humanoid) : null;

        public static ItemDrop.ItemData? GetLeftItem(Humanoid? humanoid) =>
            humanoid != null ? LeftItemRef(humanoid) : null;

        public static ItemDrop.ItemData? GetChestItem(Humanoid? humanoid) =>
            humanoid != null ? ChestItemRef(humanoid) : null;

        public static ItemDrop.ItemData? GetLegItem(Humanoid? humanoid) =>
            humanoid != null ? LegItemRef(humanoid) : null;

        public static ItemDrop.ItemData? GetHelmetItem(Humanoid? humanoid) =>
            humanoid != null ? HelmetItemRef(humanoid) : null;

        public static ItemDrop.ItemData? GetShoulderItem(Humanoid? humanoid) =>
            humanoid != null ? ShoulderItemRef(humanoid) : null;

        public static ItemDrop.ItemData? GetUtilityItem(Humanoid? humanoid) =>
            humanoid != null ? UtilityItemRef(humanoid) : null;

        public static ItemDrop.ItemData? GetTrinketItem(Humanoid? humanoid) =>
            humanoid != null ? TrinketItemRef(humanoid) : null;

        public static List<ItemDrop.ItemData?> GetEquippedItems(Humanoid? humanoid)
        {
            var list = new List<ItemDrop.ItemData?>(8);
            if (humanoid == null) return list;

            list.Add(RightItemRef(humanoid));
            list.Add(LeftItemRef(humanoid));
            list.Add(ChestItemRef(humanoid));
            list.Add(LegItemRef(humanoid));
            list.Add(HelmetItemRef(humanoid));
            list.Add(ShoulderItemRef(humanoid));
            list.Add(UtilityItemRef(humanoid));
            list.Add(TrinketItemRef(humanoid));
            return list;
        }

        public static ItemDrop.ItemData? GetCurrentBlocker(Humanoid? humanoid)
        {
            if (humanoid == null) return null;
            return GetLeftItem(humanoid) ?? humanoid.GetCurrentWeapon();
        }

        private static readonly AccessTools.FieldRef<Humanoid, float> HumanoidBlockTimerRef =
            AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");

        public static float GetBlockTimer(Humanoid? humanoid) =>
            humanoid != null ? HumanoidBlockTimerRef(humanoid) : -1f;

        public delegate bool BlockAttackDelegate(Humanoid humanoid, HitData hit, Character? attacker);

        public static readonly BlockAttackDelegate BlockAttackInvoker =
            AccessTools.MethodDelegate<BlockAttackDelegate>(AccessTools.Method(typeof(Humanoid), "BlockAttack", new[] { typeof(HitData), typeof(Character) }));

        // --- BaseAI Accessors ---
        private static readonly AccessTools.FieldRef<BaseAI, Character?> BaseAICharacterRef =
            AccessTools.FieldRefAccess<BaseAI, Character?>("m_character");

        public static Character? GetCharacter(BaseAI? baseAI) =>
            baseAI != null ? BaseAICharacterRef(baseAI) : null;

        // --- SEMan Accessors ---
        private static readonly AccessTools.FieldRef<SEMan, Character> SEManCharacterRef =
            AccessTools.FieldRefAccess<SEMan, Character>("m_character");

        public static Character? GetCharacter(SEMan? seMan) =>
            seMan != null ? SEManCharacterRef(seMan) : null;

        // --- TextsDialog Accessors ---
        private static readonly AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>> TextsListRef =
            AccessTools.FieldRefAccess<TextsDialog, List<TextsDialog.TextInfo>>("m_texts");

        private static readonly AccessTools.FieldRef<TextsDialog, int> SelectionIndexRef =
            AccessTools.FieldRefAccess<TextsDialog, int>("m_selectionIndex");

        public static List<TextsDialog.TextInfo>? GetTexts(TextsDialog? dialog) =>
            dialog != null ? TextsListRef(dialog) : null;

        public static int GetSelectionIndex(TextsDialog? dialog) =>
            dialog != null ? SelectionIndexRef(dialog) : -1;
    }
}
