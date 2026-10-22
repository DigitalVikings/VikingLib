using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VikingLib.Combat;
using VikingLib.Configuration;
using VikingLib.StatusEffects;

namespace VikingLib.UI
{
    /// <summary>
    /// Formatted display payload for an active status effect on an enemy HUD chip.
    /// </summary>
    public struct StatusEffectDisplayData
    {
        public string EffectName;
        public string DisplayText;
        public string FallbackSymbol;
        public Sprite? Icon;
        public Color AccentColor;
        public Color BackgroundColor;
    }

    /// <summary>
    /// Renders a dedicated horizontal row of active status effect chips directly underneath
    /// enemy and boss health &amp; poise bars in EnemyHud.
    /// Displays exact remaining bleed damage in dark red, vulnerability percentages, slows, and debuff timers.
    /// </summary>
    [HarmonyPatch]
    public static class EnemyStatusEffectsHud
    {
        private sealed class StatusEffectChip
        {
            public GameObject Root { get; }
            public RectTransform Rect { get; }
            public Image BackgroundImage { get; }
            public Image IconImage { get; }
            public TextMeshProUGUI ValueText { get; }

            public StatusEffectChip(GameObject root, RectTransform rect, Image bg, Image icon, TextMeshProUGUI text)
            {
                Root = root;
                Rect = rect;
                BackgroundImage = bg;
                IconImage = icon;
                ValueText = text;
            }

            public void Setup(StatusEffectDisplayData data)
            {
                BackgroundImage.color = data.BackgroundColor;

                if (data.Icon != null)
                {
                    IconImage.gameObject.SetActive(true);
                    IconImage.sprite = data.Icon;
                    IconImage.color = Color.white;
                    ValueText.text = data.DisplayText;
                    ValueText.color = data.AccentColor;
                }
                else
                {
                    IconImage.gameObject.SetActive(false);
                    if (string.IsNullOrEmpty(data.DisplayText))
                    {
                        ValueText.text = data.FallbackSymbol;
                    }
                    else
                    {
                        ValueText.text = $"{data.FallbackSymbol} {data.DisplayText}";
                    }
                    ValueText.color = data.AccentColor;
                }
            }
        }

        private sealed class StatusEffectsHudContainer
        {
            public GameObject Root { get; }
            public RectTransform Rect { get; }
            public List<StatusEffectChip> Chips { get; } = new();
            public TMP_FontAsset? Font { get; set; }

            public StatusEffectsHudContainer(GameObject root, RectTransform rect, TMP_FontAsset? font)
            {
                Root = root;
                Rect = rect;
                Font = font;
            }
        }

        private static readonly Dictionary<Character, StatusEffectsHudContainer> _containers = new();
        private static FieldInfo? _mHudsField;
        private static FieldInfo? _mHealthFastField;
        private static FieldInfo? _mHealthTextField;
        private static FieldInfo? _mNameField;
        private static Sprite? _chipBgSprite;

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
                    _mHealthTextField = AccessTools.Field(hudDataType, "m_healthText");
                    _mNameField = AccessTools.Field(hudDataType, "m_name");
                }
            }
            return _mHudsField != null && _mHealthFastField != null;
        }

        private static Sprite GetChipBackgroundSprite()
        {
            if (_chipBgSprite != null) return _chipBgSprite;

            try
            {
                // Create a 16x16 9-sliced rounded rectangle sprite with 3px rounded corners
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        float dx = Mathf.Min(x, 15 - x);
                        float dy = Mathf.Min(y, 15 - y);
                        float dist = 0f;
                        if (dx < 3 && dy < 3)
                        {
                            dist = Vector2.Distance(new Vector2(dx, dy), new Vector2(3, 3));
                        }

                        if (dist > 3f)
                        {
                            tex.SetPixel(x, y, Color.clear);
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(3.5f - dist);
                            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                        }
                    }
                }
                tex.Apply();
                _chipBgSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, 16, 16),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(4, 4, 4, 4));
            }
            catch
            {
                // Fallback for unit testing environments where Texture2D isn't backed by Unity engine
                _chipBgSprite = null;
            }

            return _chipBgSprite!;
        }

        /// <summary>
        /// Formats status effect display data for HUD chip presentation.
        /// Extracts remaining bleed damage points, vulnerable percentages, and canonical styling.
        /// </summary>
        public static bool TryGetDisplayData(StatusEffect se, bool showText, out StatusEffectDisplayData data)
        {
            data = default;
            if (se is null || se.m_hidden || se.IsDone()) return false;

            // 1. VikingStatusEffect Suite
            if (se is VikingStatusEffect vse)
            {
                string defName = vse.Definition.Name;

                // Bleed: show remaining bleed damage in crimson red
                if (string.Equals(defName, CanonicalStatusEffects.BleedId, StringComparison.OrdinalIgnoreCase))
                {
                    data = new StatusEffectDisplayData
                    {
                        EffectName = "Bleed",
                        DisplayText = showText ? vse.TotalSliceValue.ToString("F1") : string.Empty,
                        FallbackSymbol = "🩸",
                        Icon = vse.m_icon,
                        AccentColor = new Color(0.94f, 0.27f, 0.27f), // Crimson #EF4444
                        BackgroundColor = new Color(0.18f, 0.05f, 0.05f, 0.88f) // Dark crimson
                    };
                    return true;
                }

                // Vulnerable: show +X% physical taken
                if (string.Equals(defName, CanonicalStatusEffects.VulnerableId, StringComparison.OrdinalIgnoreCase))
                {
                    data = new StatusEffectDisplayData
                    {
                        EffectName = "Vulnerable",
                        DisplayText = showText ? vse.GetIconText() : string.Empty,
                        FallbackSymbol = "⚔",
                        Icon = vse.m_icon,
                        AccentColor = new Color(0.75f, 0.52f, 0.99f), // Violet #C084FC
                        BackgroundColor = new Color(0.12f, 0.04f, 0.18f, 0.88f)
                    };
                    return true;
                }

                // Crippled: show -X% speed
                if (string.Equals(defName, CanonicalStatusEffects.CrippleId, StringComparison.OrdinalIgnoreCase))
                {
                    data = new StatusEffectDisplayData
                    {
                        EffectName = "Crippled",
                        DisplayText = showText ? vse.GetIconText() : string.Empty,
                        FallbackSymbol = "🦵",
                        Icon = vse.m_icon,
                        AccentColor = new Color(0.96f, 0.62f, 0.04f), // Amber #F59E0B
                        BackgroundColor = new Color(0.18f, 0.11f, 0.03f, 0.88f)
                    };
                    return true;
                }

                // Ward Barrier: show remaining shield points
                if (string.Equals(defName, CanonicalStatusEffects.WardId, StringComparison.OrdinalIgnoreCase))
                {
                    data = new StatusEffectDisplayData
                    {
                        EffectName = "Ward",
                        DisplayText = showText ? vse.GetIconText() : string.Empty,
                        FallbackSymbol = "🛡",
                        Icon = vse.m_icon,
                        AccentColor = new Color(0.22f, 0.74f, 0.97f), // Sky blue #38BDF8
                        BackgroundColor = new Color(0.03f, 0.18f, 0.29f, 0.88f)
                    };
                    return true;
                }

                // Empowered Strike
                if (string.Equals(defName, CanonicalStatusEffects.EmpoweredStrikeId, StringComparison.OrdinalIgnoreCase))
                {
                    data = new StatusEffectDisplayData
                    {
                        EffectName = "Empowered",
                        DisplayText = showText ? vse.GetIconText() : string.Empty,
                        FallbackSymbol = "⚡",
                        Icon = vse.m_icon,
                        AccentColor = new Color(0.98f, 0.75f, 0.14f), // Gold #FBBF24
                        BackgroundColor = new Color(0.15f, 0.12f, 0.03f, 0.88f)
                    };
                    return true;
                }

                // Generic VikingStatusEffect
                string iconText = vse.GetIconText();
                if (string.IsNullOrEmpty(iconText) && vse.m_ttl > 0f)
                {
                    iconText = $"{Mathf.CeilToInt(vse.GetRemaningTime())}s";
                }

                data = new StatusEffectDisplayData
                {
                    EffectName = vse.Definition.DisplayName,
                    DisplayText = showText ? iconText : string.Empty,
                    FallbackSymbol = defName.Length >= 3 ? defName.Substring(0, 3).ToUpper() : "FX",
                    Icon = vse.m_icon,
                    AccentColor = new Color(0.89f, 0.91f, 0.94f),
                    BackgroundColor = new Color(0.09f, 0.09f, 0.11f, 0.88f)
                };
                return true;
            }

            // 2. Native Valheim Status Effects
            string rawName = !string.IsNullOrEmpty(se.m_name) ? se.m_name : (se.name ?? string.Empty);
            string seName = rawName.ToLowerInvariant();

            if (seName.IndexOf("burn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                data = new StatusEffectDisplayData
                {
                    EffectName = "Burning",
                    DisplayText = showText && se.m_ttl > 0f ? $"{Mathf.CeilToInt(se.GetRemaningTime())}s" : string.Empty,
                    FallbackSymbol = "🔥",
                    Icon = se.m_icon,
                    AccentColor = new Color(0.98f, 0.45f, 0.09f), // Orange #F97316
                    BackgroundColor = new Color(0.18f, 0.08f, 0.03f, 0.88f)
                };
                return true;
            }

            if (seName.IndexOf("poison", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                data = new StatusEffectDisplayData
                {
                    EffectName = "Poison",
                    DisplayText = showText && se.m_ttl > 0f ? $"{Mathf.CeilToInt(se.GetRemaningTime())}s" : string.Empty,
                    FallbackSymbol = "☠",
                    Icon = se.m_icon,
                    AccentColor = new Color(0.13f, 0.77f, 0.37f), // Green #22C55E
                    BackgroundColor = new Color(0.02f, 0.13f, 0.05f, 0.88f)
                };
                return true;
            }

            if (seName.IndexOf("frost", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                data = new StatusEffectDisplayData
                {
                    EffectName = "Frost",
                    DisplayText = showText && se.m_ttl > 0f ? $"{Mathf.CeilToInt(se.GetRemaningTime())}s" : string.Empty,
                    FallbackSymbol = "❄",
                    Icon = se.m_icon,
                    AccentColor = new Color(0.22f, 0.74f, 0.97f), // Cyan #38BDF8
                    BackgroundColor = new Color(0.02f, 0.10f, 0.15f, 0.88f)
                };
                return true;
            }

            // Generic Native StatusEffect
            data = new StatusEffectDisplayData
            {
                EffectName = !string.IsNullOrEmpty(se.m_name) ? se.m_name : rawName,
                DisplayText = showText && se.m_ttl > 0f ? $"{Mathf.CeilToInt(se.GetRemaningTime())}s" : string.Empty,
                FallbackSymbol = rawName.Length >= 3 ? rawName.Substring(0, 3).ToUpper() : "FX",
                Icon = se.m_icon,
                AccentColor = new Color(0.89f, 0.91f, 0.94f),
                BackgroundColor = new Color(0.09f, 0.09f, 0.11f, 0.88f)
            };
            return true;
        }

        [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
        [HarmonyPostfix]
        public static void ShowHudPostfix(EnemyHud __instance, Character c, bool isMount)
        {
            if (!ModConfig.ModEnabled.Value || c == null || __instance == null) return;
            if (ModConfig.EnemyStatusEffectsMode.Value == StaggerBarDisplayMode.Off) return;
            if (ModConfig.EnemyStatusEffectsMode.Value == StaggerBarDisplayMode.BossesOnly && !c.IsBoss()) return;

            if (!EnsureReflected()) return;
            if (_containers.TryGetValue(c, out var existing) && existing?.Root != null) return;

            try
            {
                var dict = (IDictionary?)_mHudsField!.GetValue(__instance);
                if (dict == null || !dict.Contains(c)) return;

                object? hudData = dict[c];
                if (hudData == null) return;

                var healthFast = (GuiBar?)_mHealthFastField!.GetValue(hudData);
                if (healthFast == null) return;

                var healthFastRect = healthFast.GetComponent<RectTransform>();
                if (healthFastRect == null) return;

                // Resolve native TextMeshPro font asset from health text or name text
                TMP_FontAsset? font = null;
                if (_mHealthTextField != null)
                {
                    var ht = (TextMeshProUGUI?)_mHealthTextField.GetValue(hudData);
                    if (ht != null) font = ht.font;
                }
                if (font == null && _mNameField != null)
                {
                    var nt = (TextMeshProUGUI?)_mNameField.GetValue(hudData);
                    if (nt != null) font = nt.font;
                }

                // Create container under healthFast's parent transform (Health)
                var rootObj = new GameObject("vikinglib_status_effects_root");
                rootObj.transform.SetParent(healthFast.transform.parent, false);

                var rootRect = rootObj.AddComponent<RectTransform>();
                rootRect.anchorMin = new Vector2(0.5f, 0f);
                rootRect.anchorMax = new Vector2(0.5f, 0f);
                rootRect.pivot = new Vector2(0.5f, 1f);
                rootRect.sizeDelta = new Vector2(healthFastRect.sizeDelta.x, 18f);

                // Position cleanly below the health bar and stagger bar
                float staggerBarOffset = 13f;
                rootRect.anchoredPosition = new Vector2(healthFastRect.anchoredPosition.x, healthFastRect.anchoredPosition.y - staggerBarOffset);

                var layout = rootObj.AddComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.spacing = 3f;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                var container = new StatusEffectsHudContainer(rootObj, rootRect, font);
                rootObj.SetActive(false);
                _containers[c] = container;
            }
            catch (Exception ex)
            {
                VikingLibPlugin.Log?.LogWarning($"[VikingLib EnemyStatusEffectsHud] Failed to attach status effect container for '{c.name}': {ex.Message}");
            }
        }

        private static StatusEffectChip CreateChip(StatusEffectsHudContainer container)
        {
            var chipObj = new GameObject("vikinglib_status_chip");
            chipObj.transform.SetParent(container.Root.transform, false);

            var chipRect = chipObj.AddComponent<RectTransform>();
            chipRect.pivot = new Vector2(0.5f, 0.5f);

            var bgImg = chipObj.AddComponent<Image>();
            var bgSprite = GetChipBackgroundSprite();
            if (bgSprite != null)
            {
                bgImg.sprite = bgSprite;
                bgImg.type = Image.Type.Sliced;
            }

            var fitter = chipObj.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = chipObj.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 1, 1);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            // Child 1: Icon
            var iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(chipObj.transform, false);
            var iconRect = iconObj.AddComponent<RectTransform>();
            var iconImg = iconObj.AddComponent<Image>();
            var iconLayout = iconObj.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = 13f;
            iconLayout.preferredHeight = 13f;
            iconLayout.minWidth = 13f;
            iconLayout.minHeight = 13f;

            // Child 2: Text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(chipObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            var textComp = textObj.AddComponent<TextMeshProUGUI>();
            if (container.Font != null) textComp.font = container.Font;
            textComp.fontSize = 10f;
            textComp.fontStyle = FontStyles.Bold;
            textComp.alignment = TextAlignmentOptions.Center;

            var textFitter = textObj.AddComponent<ContentSizeFitter>();
            textFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return new StatusEffectChip(chipObj, chipRect, bgImg, iconImg, textComp);
        }

        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        [HarmonyPostfix]
        public static void UpdateHudsPostfix(EnemyHud __instance)
        {
            if (!ModConfig.ModEnabled.Value || __instance == null || _containers.Count == 0) return;

            var mode = ModConfig.EnemyStatusEffectsMode.Value;
            bool showText = ModConfig.EnemyStatusEffectsShowText.Value;
            var deadCharacters = new List<Character>();

            foreach (var kvp in _containers)
            {
                var character = kvp.Key;
                var container = kvp.Value;

                if (character == null)
                {
                    continue;
                }

                if (container == null || container.Root == null)
                {
                    deadCharacters.Add(character);
                    continue;
                }

                if (mode == StaggerBarDisplayMode.Off || (mode == StaggerBarDisplayMode.BossesOnly && !character.IsBoss()))
                {
                    if (container.Root.activeSelf) container.Root.SetActive(false);
                    continue;
                }

                var seman = character.GetSEMan();
                var activeEffects = seman?.GetStatusEffects();

                if (activeEffects == null || activeEffects.Count == 0)
                {
                    if (container.Root.activeSelf) container.Root.SetActive(false);
                    continue;
                }

                int displayIndex = 0;
                for (int i = 0; i < activeEffects.Count; i++)
                {
                    var se = activeEffects[i];
                    if (TryGetDisplayData(se, showText, out var data))
                    {
                        if (displayIndex >= container.Chips.Count)
                        {
                            var newChip = CreateChip(container);
                            container.Chips.Add(newChip);
                        }

                        var chip = container.Chips[displayIndex];
                        chip.Setup(data);
                        if (!chip.Root.activeSelf) chip.Root.SetActive(true);
                        displayIndex++;

                        if (displayIndex >= 6) break; // Limit to 6 chips per enemy
                    }
                }

                // Hide any pooled chips not currently used
                for (int j = displayIndex; j < container.Chips.Count; j++)
                {
                    if (container.Chips[j].Root.activeSelf) container.Chips[j].Root.SetActive(false);
                }

                if (displayIndex > 0)
                {
                    if (!container.Root.activeSelf) container.Root.SetActive(true);
                }
                else
                {
                    if (container.Root.activeSelf) container.Root.SetActive(false);
                }
            }

            for (int i = 0; i < deadCharacters.Count; i++)
            {
                _containers.Remove(deadCharacters[i]);
            }
        }

        [HarmonyPatch(typeof(EnemyHud), "RemoveCharacterHud")]
        [HarmonyPostfix]
        public static void RemoveCharacterHudPostfix(Character character)
        {
            if (character != null && _containers.TryGetValue(character, out var container))
            {
                if (container?.Root != null)
                {
                    UnityEngine.Object.Destroy(container.Root);
                }
                _containers.Remove(character);
            }
        }
    }
}
