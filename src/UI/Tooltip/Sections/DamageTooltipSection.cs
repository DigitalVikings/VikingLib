namespace VikingLib.UI.Tooltip.Sections
{
    /// <summary>
    /// Modular tooltip section for displaying calculated weapon damage ranges.
    /// </summary>
    public class DamageTooltipSection : ITooltipSection
    {
        public string SectionName => "Damage";
        public int Priority => 100;

        public bool ShouldRender(ItemTooltipContext context)
        {
            if (context?.Item?.m_shared == null) return false;
            HitData.DamageTypes dmg = context.Item.GetDamage(context.QualityLevel, context.WorldLevel);
            return dmg.GetTotalDamage() > 0f;
        }

        public string Render(ItemTooltipContext context)
        {
            HitData.DamageTypes dmg = context.Item.GetDamage(context.QualityLevel, context.WorldLevel);
            return TooltipEngine.FormatDamageTooltip(context, dmg, context.Item.m_shared.m_skillType);
        }
    }
}
