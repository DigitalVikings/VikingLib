using System;

namespace VikingLib.UI.Tooltip
{
    /// <summary>
    /// Contextual state for the item currently generating a tooltip.
    /// Provides access to the hovered ItemData, player, quality level, crafting mode, and world level.
    /// </summary>
    public class ItemTooltipContext
    {
        public static ItemTooltipContext? Current { get; internal set; }

        public ItemDrop.ItemData Item { get; }
        public int QualityLevel { get; }
        public bool Crafting { get; }
        public float WorldLevel { get; }
        public Player? Player { get; }

        public ItemTooltipContext(ItemDrop.ItemData item, int qualityLevel, bool crafting, float worldLevel, Player? player)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            QualityLevel = qualityLevel;
            Crafting = crafting;
            WorldLevel = worldLevel;
            Player = player;
        }
    }
}
