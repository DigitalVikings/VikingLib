using UnityEngine;

namespace VikingLib.Combat.Roll
{
    /// <summary>
    /// Default Valheim vanilla skill scaling provider.
    /// Reproduces the vanilla center = Lerp(0.4, 1.0, skillFactor) and +/- 0.15 bounds ([0.25..0.55] -> [0.85..1.00]).
    /// </summary>
    public class VanillaSkillScalingProvider : ISkillScalingProvider
    {
        public void CalculateBounds(DamageRollContext context, out float lowerMult, out float upperMult)
        {
            bool isPlayer = context.Attacker != null ? context.Attacker.IsPlayer() : (Player.m_localPlayer != null);
            if (isPlayer)
            {
                // Player skill scaling: center is Lerp(0.4, 1.0, skillFactor)
                float skillFactor = context.Attacker != null
                    ? context.Attacker.GetSkillFactor(context.SkillType)
                    : (Player.m_localPlayer != null ? Player.m_localPlayer.GetSkillFactor(context.SkillType) : 0f);
                float center = Mathf.Lerp(0.4f, 1.0f, skillFactor);

                lowerMult = Mathf.Max(0f, center - 0.15f);
                upperMult = Mathf.Min(1.0f, center + 0.15f);
            }
            else
            {
                // Non-player creature or neutral preview bounds [0.75..1.0]
                lowerMult = SkillSystem.VanillaSkillIntegration.CreatureDamageRollLowerBound;
                upperMult = SkillSystem.VanillaSkillIntegration.CreatureDamageRollUpperBound;
            }
        }
    }
}
