namespace VikingLib.UI.Tooltip
{
    /// <summary>
    /// Represents a modular section within an item's tooltip (e.g. Damage, Stagger, Status Effects).
    /// </summary>
    public interface ITooltipSection
    {
        string SectionName { get; }
        int Priority { get; }
        bool ShouldRender(ItemTooltipContext context);
        string Render(ItemTooltipContext context);
    }
}
