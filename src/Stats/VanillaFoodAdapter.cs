using System.Collections.Generic;
using UnityEngine;

namespace VikingLib.Stats
{
    /// <summary>
    /// Synchronizes active player foods into declarative Tier 1 Flat tokens on MaxHealth,
    /// MaxStamina, MaxEitr, and HealthRegen, applying Food Effectiveness multipliers.
    /// </summary>
    public static class VanillaFoodAdapter
    {
        public static readonly object FoodOwnerKey = new();

        /// <summary>
        /// Scans active foods on the player and synchronizes 3-Tier flat tokens.
        /// </summary>
        public static void SyncFood(Player? player)
        {
            if (player == null) return;

            CharacterStatManager.RemoveModifiersByOwner(player, FoodOwnerKey);

            var foods = player.GetFoods();
            if (foods == null || foods.Count == 0) return;

            // Fetch the 4 food effectiveness multipliers (Tier 2 additive %, default 1.0x)
            float hpEff = CharacterStatManager.GetStat(player, CharacterStatType.FoodHealthEffectiveness);
            float regenEff = CharacterStatManager.GetStat(player, CharacterStatType.FoodHealthRegenEffectiveness);
            float stamEff = CharacterStatManager.GetStat(player, CharacterStatType.FoodStaminaEffectiveness);
            float eitrEff = CharacterStatManager.GetStat(player, CharacterStatType.FoodEitrEffectiveness);

            for (int i = 0; i < foods.Count; i++)
            {
                var food = foods[i];
                if (food == null) continue;

                string foodName = food.m_item?.m_shared?.m_name ?? "Food";
                if (foodName.StartsWith("$"))
                {
                    foodName = Localization.instance != null ? Localization.instance.Localize(foodName) : foodName;
                }

                // 1. Health flat bonus
                float flatHp = food.m_health * hpEff;
                if (flatHp > 0.01f)
                {
                    CharacterStatManager.AddModifier(player,
                        StatModifier.Flat(CharacterStats.MaxHealth, flatHp, foodName, null, FoodOwnerKey));
                }

                // 2. Stamina flat bonus
                float flatStam = food.m_stamina * stamEff;
                if (flatStam > 0.01f)
                {
                    CharacterStatManager.AddModifier(player,
                        StatModifier.Flat(CharacterStats.MaxStamina, flatStam, foodName, null, FoodOwnerKey));
                }

                // 3. Eitr flat bonus
                float flatEitr = food.m_eitr * eitrEff;
                if (flatEitr > 0.01f)
                {
                    CharacterStatManager.AddModifier(player,
                        StatModifier.Flat(CharacterStats.MaxEitr, flatEitr, foodName, null, FoodOwnerKey));
                }

                // 4. Continuous health regeneration flat bonus (converted from vanilla 10s pulse rate to HP/sec)
                if (food.m_item?.m_shared != null && food.m_item.m_shared.m_foodRegen > 0.001f)
                {
                    float flatRegen = (food.m_item.m_shared.m_foodRegen / 10.0f) * regenEff;
                    CharacterStatManager.AddModifier(player,
                        StatModifier.Flat(CharacterStats.HealthRegen, flatRegen, foodName, null, FoodOwnerKey));
                }
            }
        }
    }
}
