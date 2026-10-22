using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Generates and caches procedural status effect icons for canonical status effects and HUD presentations.
    /// Ensures that runtime status effects possess non-null <see cref="StatusEffect.m_icon"/> sprites,
    /// enabling Valheim's native player HUD status effect bar to display them.
    /// </summary>
    public static class StatusEffectIconFactory
    {
        private static readonly ConcurrentDictionary<string, Sprite> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        private static Sprite? CreateSpriteFromTexture(Texture2D tex)
        {
            try
            {
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Retrieves or generates a procedural sprite icon corresponding to the specified canonical status effect name.
        /// </summary>
        public static Sprite? GetIconForDefinition(string effectName)
        {
            if (string.IsNullOrEmpty(effectName)) return null;

            if (_iconCache.TryGetValue(effectName, out var cached) && cached != null)
            {
                return cached;
            }

            Sprite? generated = null;
            if (string.Equals(effectName, CanonicalStatusEffects.BleedId, StringComparison.OrdinalIgnoreCase))
            {
                generated = GetBleedIcon();
            }
            else if (string.Equals(effectName, CanonicalStatusEffects.VulnerableId, StringComparison.OrdinalIgnoreCase))
            {
                generated = GetVulnerableIcon();
            }
            else if (string.Equals(effectName, CanonicalStatusEffects.CrippleId, StringComparison.OrdinalIgnoreCase))
            {
                generated = GetCrippleIcon();
            }
            else if (string.Equals(effectName, CanonicalStatusEffects.EmpoweredStrikeId, StringComparison.OrdinalIgnoreCase))
            {
                generated = GetEmpoweredStrikeIcon();
            }
            else if (string.Equals(effectName, CanonicalStatusEffects.BerserkId, StringComparison.OrdinalIgnoreCase))
            {
                generated = GetBerserkIcon();
            }
            else if (string.Equals(effectName, CanonicalStatusEffects.FortifiedId, StringComparison.OrdinalIgnoreCase))
            {
                generated = GetFortifiedIcon();
            }
            else if (string.Equals(effectName, CanonicalStatusEffects.WardId, StringComparison.OrdinalIgnoreCase))
            {
                generated = GetWardIcon();
            }

            if (generated != null)
            {
                _iconCache[effectName] = generated;
            }

            return generated;
        }

        /// <summary>
        /// Generates a procedural arterial blood droplet icon in vibrant crimson red with specular highlight.
        /// </summary>
        public static Sprite? GetBleedIcon()
        {
            if (_iconCache.TryGetValue(CanonicalStatusEffects.BleedId, out var existing) && existing != null)
            {
                return existing;
            }

            try
            {
                int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float cx = 15.5f;
                float cy = 11.5f;
                float radius = 7.5f;
                float tipY = 27.5f;

                Color fillColor = new Color(0.94f, 0.27f, 0.27f, 1f);     // Crimson #EF4444
                Color borderColor = new Color(0.50f, 0.11f, 0.11f, 1f);   // Dark crimson #7F1D1D
                Color highlightColor = new Color(1f, 0.88f, 0.88f, 0.95f); // Specular white/pink

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist;
                        if (y <= cy)
                        {
                            float dx = x - cx;
                            float dy = y - cy;
                            dist = Mathf.Sqrt(dx * dx + dy * dy);
                        }
                        else if (y <= tipY)
                        {
                            float t = (y - cy) / (tipY - cy);
                            float halfWidth = radius * (1.0f - t * 0.92f) * Mathf.Sqrt(Mathf.Max(0.01f, 1.0f - t * 0.5f));
                            float dx = Mathf.Abs(x - cx);
                            dist = halfWidth > 0.001f ? (dx / halfWidth) * radius : 100f;
                        }
                        else
                        {
                            dist = 100f;
                        }

                        if (dist <= radius)
                        {
                            // Inner shape
                            if (dist > radius - 1.25f)
                            {
                                tex.SetPixel(x, y, borderColor);
                            }
                            else if (x >= 13 && x <= 14 && y >= 14 && y <= 16)
                            {
                                // Specular highlight
                                tex.SetPixel(x, y, highlightColor);
                            }
                            else
                            {
                                tex.SetPixel(x, y, fillColor);
                            }
                        }
                        else if (dist <= radius + 1.0f)
                        {
                            // Antialiasing edge
                            float alpha = 1.0f - (dist - radius);
                            tex.SetPixel(x, y, new Color(borderColor.r, borderColor.g, borderColor.b, Mathf.Clamp01(alpha)));
                        }
                        else
                        {
                            tex.SetPixel(x, y, Color.clear);
                        }
                    }
                }

                tex.Apply();
                var sprite = CreateSpriteFromTexture(tex);
                if (sprite != null) _iconCache[CanonicalStatusEffects.BleedId] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generates a procedural cracked shield icon in violet for SE_Vulnerable.
        /// </summary>
        public static Sprite? GetVulnerableIcon()
        {
            if (_iconCache.TryGetValue(CanonicalStatusEffects.VulnerableId, out var existing) && existing != null)
            {
                return existing;
            }

            try
            {
                int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color fill = new Color(0.66f, 0.33f, 0.97f, 1f);   // Violet #A855F7
                Color border = new Color(0.35f, 0.11f, 0.53f, 1f); // Dark violet #581C87
                Color crack = new Color(0.95f, 0.90f, 1.00f, 1f);  // Bright fracture

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if (y < 4 || y > 27 || x < 6 || x > 25)
                        {
                            tex.SetPixel(x, y, Color.clear);
                            continue;
                        }

                        float halfWidth = 10f * (1.0f - Mathf.Pow((27f - y) / 23f, 1.8f) * 0.7f);
                        float dx = Mathf.Abs(x - 15.5f);

                        if (dx <= halfWidth)
                        {
                            // Check crack path
                            float crackX = 11f + (y - 4) * (10f / 23f);
                            if (Mathf.Abs(x - crackX) < 1.0f && y % 3 != 0)
                            {
                                tex.SetPixel(x, y, crack);
                            }
                            else if (dx > halfWidth - 1.3f || y <= 5 || y >= 26)
                            {
                                tex.SetPixel(x, y, border);
                            }
                            else
                            {
                                tex.SetPixel(x, y, fill);
                            }
                        }
                        else
                        {
                            tex.SetPixel(x, y, Color.clear);
                        }
                    }
                }

                tex.Apply();
                var sprite = CreateSpriteFromTexture(tex);
                if (sprite != null) _iconCache[CanonicalStatusEffects.VulnerableId] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generates a procedural downward slow chevron icon in amber for SE_Cripple.
        /// </summary>
        public static Sprite? GetCrippleIcon()
        {
            if (_iconCache.TryGetValue(CanonicalStatusEffects.CrippleId, out var existing) && existing != null)
            {
                return existing;
            }

            try
            {
                int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color amber = new Color(0.96f, 0.62f, 0.04f, 1f); // Amber #F59E0B
                Color dark = new Color(0.47f, 0.21f, 0.06f, 1f);  // Dark amber #78350F

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Abs(x - 15.5f);
                        // Upper chevron
                        float chev1Y = 22f - dx * 0.7f;
                        bool inChev1 = y >= chev1Y - 4f && y <= chev1Y;

                        // Lower chevron
                        float chev2Y = 12f - dx * 0.7f;
                        bool inChev2 = y >= chev2Y - 4f && y <= chev2Y;

                        if ((inChev1 || inChev2) && dx <= 11f)
                        {
                            bool isBorder = dx > 9.5f || (inChev1 && (y == Mathf.RoundToInt(chev1Y) || y == Mathf.RoundToInt(chev1Y - 4f)))
                                                     || (inChev2 && (y == Mathf.RoundToInt(chev2Y) || y == Mathf.RoundToInt(chev2Y - 4f)));
                            tex.SetPixel(x, y, isBorder ? dark : amber);
                        }
                        else
                        {
                            tex.SetPixel(x, y, Color.clear);
                        }
                    }
                }

                tex.Apply();
                var sprite = CreateSpriteFromTexture(tex);
                if (sprite != null) _iconCache[CanonicalStatusEffects.CrippleId] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generates a procedural aegis barrier rune icon in luminous sky blue for SE_Ward.
        /// </summary>
        public static Sprite? GetWardIcon()
        {
            if (_iconCache.TryGetValue(CanonicalStatusEffects.WardId, out var existing) && existing != null)
            {
                return existing;
            }

            try
            {
                int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color blue = new Color(0.22f, 0.74f, 0.97f, 1f);  // Sky blue #38BDF8
                Color dark = new Color(0.01f, 0.33f, 0.54f, 1f);  // Deep ocean #0284C7
                Color glow = new Color(0.73f, 0.90f, 0.99f, 1f);  // Luminous white/blue

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Abs(x - 15.5f);
                        float dy = Mathf.Abs(y - 15.5f);
                        float diamondDist = dx + dy;

                        if (diamondDist <= 12f && diamondDist >= 9f)
                        {
                            tex.SetPixel(x, y, blue);
                        }
                        else if (diamondDist < 9f && diamondDist >= 7f)
                        {
                            tex.SetPixel(x, y, glow);
                        }
                        else if (diamondDist < 7f && diamondDist >= 4f)
                        {
                            tex.SetPixel(x, y, dark);
                        }
                        else if (diamondDist < 4f)
                        {
                            tex.SetPixel(x, y, glow);
                        }
                        else
                        {
                            tex.SetPixel(x, y, Color.clear);
                        }
                    }
                }

                tex.Apply();
                var sprite = CreateSpriteFromTexture(tex);
                if (sprite != null) _iconCache[CanonicalStatusEffects.WardId] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generates a procedural lightning strike icon in gold for SE_EmpoweredStrike.
        /// </summary>
        public static Sprite? GetEmpoweredStrikeIcon()
        {
            if (_iconCache.TryGetValue(CanonicalStatusEffects.EmpoweredStrikeId, out var existing) && existing != null)
            {
                return existing;
            }

            try
            {
                int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color gold = new Color(0.98f, 0.75f, 0.14f, 1f); // Gold #FBBF24
                Color bright = new Color(1.00f, 0.94f, 0.54f, 1f); // Bright yellow
                Color dark = new Color(0.47f, 0.21f, 0.06f, 1f);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // Lightning zig-zag
                        bool isBolt = (y >= 16 && y <= 27 && x >= 15 && x <= 22 - (y - 16) * 0.4f) ||
                                     (y >= 5 && y <= 17 && x >= 10 + (y - 5) * 0.4f && x <= 18);

                        if (isBolt)
                        {
                            tex.SetPixel(x, y, (x == 15 || y == 16) ? bright : gold);
                        }
                        else
                        {
                            tex.SetPixel(x, y, Color.clear);
                        }
                    }
                }

                tex.Apply();
                var sprite = CreateSpriteFromTexture(tex);
                if (sprite != null) _iconCache[CanonicalStatusEffects.EmpoweredStrikeId] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generates a procedural blazing rage flame icon in scarlet for SE_Berserk.
        /// </summary>
        public static Sprite? GetBerserkIcon()
        {
            if (_iconCache.TryGetValue(CanonicalStatusEffects.BerserkId, out var existing) && existing != null)
            {
                return existing;
            }

            try
            {
                int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color scarlet = new Color(0.86f, 0.15f, 0.15f, 1f);
                Color orange = new Color(0.98f, 0.45f, 0.09f, 1f);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Abs(x - 15.5f);
                        if (y >= 5 && y <= 26 && dx <= 11f * (1.0f - (y - 5f) / 22f))
                        {
                            tex.SetPixel(x, y, dx < 4f ? orange : scarlet);
                        }
                        else
                        {
                            tex.SetPixel(x, y, Color.clear);
                        }
                    }
                }

                tex.Apply();
                var sprite = CreateSpriteFromTexture(tex);
                if (sprite != null) _iconCache[CanonicalStatusEffects.BerserkId] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generates a procedural heavy bulwark shield icon in slate steel for SE_Fortified.
        /// </summary>
        public static Sprite? GetFortifiedIcon()
        {
            if (_iconCache.TryGetValue(CanonicalStatusEffects.FortifiedId, out var existing) && existing != null)
            {
                return existing;
            }

            try
            {
                int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color slate = new Color(0.58f, 0.64f, 0.72f, 1f); // Slate #94A3B8
                Color steel = new Color(0.30f, 0.39f, 0.49f, 1f);
                Color rim = new Color(0.89f, 0.91f, 0.94f, 1f);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Abs(x - 15.5f);
                        if (y >= 5 && y <= 27 && dx <= 10f)
                        {
                            if (dx > 8.5f || y <= 6 || y >= 26)
                            {
                                tex.SetPixel(x, y, rim);
                            }
                            else if (dx < 1.5f || y == 16)
                            {
                                tex.SetPixel(x, y, rim); // Cross brace
                            }
                            else
                            {
                                tex.SetPixel(x, y, slate);
                            }
                        }
                        else
                        {
                            tex.SetPixel(x, y, Color.clear);
                        }
                    }
                }

                tex.Apply();
                var sprite = CreateSpriteFromTexture(tex);
                if (sprite != null) _iconCache[CanonicalStatusEffects.FortifiedId] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }
    }
}
