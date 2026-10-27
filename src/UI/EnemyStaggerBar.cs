using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat.Stagger;
using VikingLib.Configuration;

namespace VikingLib.UI
{
    /// <summary>
    /// Renders an amber/gold poise/stagger meter directly underneath enemy and boss health bars in EnemyHud.
    /// Pulses bright white while the enemy is undergoing stagger or artificial stun.
    /// </summary>
    [HarmonyPatch]
    public static class EnemyStaggerBar
    {
        private static readonly Dictionary<Character, GuiBar> _bars = new();
        private static FieldInfo? _mHudsField;
        private static FieldInfo? _mHealthFastField;
        private static FieldInfo? _mNameField;
        private static readonly Color AmberColor = new(0.96f, 0.62f, 0.04f); // #F59E0B

        private static bool EnsureReflected()
        {
            if (_mHudsField == null)
            {
                _mHudsField = AccessTools.Field(typeof(EnemyHud), "m_huds");
            }
            if (_mHealthFastField == null)
            {
                var hudDataType = AccessTools.Inner(typeof(EnemyHud), "HudData");
                if (hudDataType != null)
                {
                    _mHealthFastField = AccessTools.Field(hudDataType, "m_healthFast");
                }
            }
            if (_mNameField == null)
            {
                var hudDataType = AccessTools.Inner(typeof(EnemyHud), "HudData");
                if (hudDataType != null)
                {
                    _mNameField = AccessTools.Field(hudDataType, "m_name");
                }
            }
            return _mHudsField != null && _mHealthFastField != null;
        }

        [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
        [HarmonyPostfix]
        public static void ShowHudPostfix(EnemyHud __instance, Character c, bool isMount)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.StaggerOverhaulEnabled.Value || c == null || __instance == null)
                return;

            if (ModConfig.EnemyStaggerBarMode.Value == StaggerBarDisplayMode.Off)
                return;

            if (ModConfig.EnemyStaggerBarMode.Value == StaggerBarDisplayMode.BossesOnly && !c.IsBoss())
                return;

            if (!EnsureReflected()) return;

            if (_bars.TryGetValue(c, out var existingBar) && existingBar != null)
            {
                existingBar.SetMaxValue(1f);
                existingBar.m_smoothDrain = false;
                existingBar.m_smoothFill = false;
                existingBar.m_changeDelay = 0f;
                return;
            }

            try
            {
                var dict = (IDictionary?)_mHudsField!.GetValue(__instance);
                if (dict == null || !dict.Contains(c)) return;

                object? hudData = dict[c];
                if (hudData == null) return;

                var healthFast = (GuiBar?)_mHealthFastField!.GetValue(hudData);
                if (healthFast == null) return;

                // Instantiate poise meter as a sibling under healthFast's parent transform
                var staggerObj = UnityEngine.Object.Instantiate(healthFast.gameObject, healthFast.transform.parent);
                staggerObj.name = "vikinglib_stagger_bar";

                var rect = staggerObj.GetComponent<RectTransform>();
                if (rect != null)
                {
                    float height = Mathf.Max(2.5f, rect.sizeDelta.y * 0.35f);
                    rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y - height - 1.5f);
                }

                var staggerBar = staggerObj.GetComponent<GuiBar>();
                if (staggerBar != null)
                {
                    staggerBar.SetMaxValue(1f);
                    staggerBar.m_smoothDrain = false;
                    staggerBar.m_smoothFill = false;
                    staggerBar.m_changeDelay = 0f;
                    staggerBar.SetColor(AmberColor);
                    staggerBar.SetValue(0f);
                    staggerObj.SetActive(false);
                    _bars[c] = staggerBar;
                }
            }
            catch (Exception ex)
            {
                VikingLibPlugin.Log?.LogWarning($"[VikingLib EnemyStaggerBar] Failed to attach stagger bar for '{c.name}': {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        [HarmonyPostfix]
        public static void UpdateHudsPostfix(EnemyHud __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.StaggerOverhaulEnabled.Value || __instance == null)
                return;

            if (_bars.Count == 0) return;

            var mode = ModConfig.EnemyStaggerBarMode.Value;
            var deadCharacters = new List<Character>();

            foreach (var kvp in _bars)
            {
                var character = kvp.Key;
                var bar = kvp.Value;

                if (character == null)
                {
                    continue;
                }

                if (bar == null)
                {
                    deadCharacters.Add(character);
                    continue;
                }

                if (mode == StaggerBarDisplayMode.Off || (mode == StaggerBarDisplayMode.BossesOnly && !character.IsBoss()))
                {
                    if (bar.gameObject.activeSelf) bar.gameObject.SetActive(false);
                    continue;
                }

                float poisePct = StaggerManager.GetPoisePercentage(character);
                bool isStaggering = character.IsStaggering() || StaggerManager.IsStaggering(character);

                if (poisePct > 0.001f || isStaggering)
                {
                    if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);

                    if (isStaggering)
                    {
                        // Flashing feedback during active stagger / stun
                        float pulse = Mathf.PingPong(Time.time * 6f, 1f);
                        bar.SetColor(Color.Lerp(AmberColor, Color.white, pulse));

                        // Drains smoothly towards 0 while creature is staggered to show remaining duration
                        float staggerFraction = StaggerManager.GetStaggerFraction(character);
                        bar.SetValue(staggerFraction);

                        // Highlight nameplate if target is a boss
                        if (character.IsBoss() && _mHudsField != null)
                        {
                            var dict = (IDictionary?)_mHudsField.GetValue(__instance);
                            if (dict != null && dict.Contains(character))
                            {
                                object? hudData = dict[character];
                                if (hudData != null && _mNameField != null)
                                {
                                    var nameText = (TMPro.TextMeshProUGUI?)_mNameField.GetValue(hudData);
                                    if (nameText != null)
                                    {
                                        nameText.text = $"{Localization.instance.Localize(character.GetHoverName())} <color=#F59E0B>[STAGGERED]</color>";
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        bar.SetColor(AmberColor);
                        bar.SetValue(poisePct);
                    }
                }
                else
                {
                    if (bar.gameObject.activeSelf) bar.gameObject.SetActive(false);
                }
            }

            for (int i = 0; i < deadCharacters.Count; i++)
            {
                _bars.Remove(deadCharacters[i]);
            }
        }

        [HarmonyPatch(typeof(EnemyHud), "RemoveCharacterHud")]
        [HarmonyPostfix]
        public static void RemoveCharacterHudPostfix(Character character)
        {
            if (character != null)
            {
                _bars.Remove(character);
            }
        }
    }
}
