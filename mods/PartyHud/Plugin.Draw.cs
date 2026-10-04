using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PartyHud
{
    /// <summary>Drawing the panel with Unity's immediate-mode GUI: rounded rectangles and the Steam pictures.</summary>
    public partial class Plugin
    {
        private const float PanelWidth = 252f;
        private const float PanelRadius = 7f;

        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color HealthColor = new Color(0.84f, 0.22f, 0.22f);
        private static readonly Color StaminaColor = new Color(0.96f, 0.80f, 0.26f);
        private static readonly Color EitrColor = new Color(0.66f, 0.46f, 0.96f);
        private static readonly Color BarBackground = new Color(0.03f, 0.03f, 0.03f, 0.9f);
        private static readonly Color Dim = new Color(0.68f, 0.66f, 0.62f);

        private GUIStyle _nameStyle, _distanceStyle, _barStyle, _initialStyle, _headerStyle;
        private Texture2D _gradient;

        /// <summary>Per-player animation state: the smoothed bars, the "recent damage" trail behind health, and the fade-in.</summary>
        private class Anim { public float Health, Trail, Stamina, Eitr, Alpha; public float LastTime; }
        private readonly Dictionary<long, Anim> _anim = new Dictionary<long, Anim>();

        private void EnsureStyles()
        {
            if (_nameStyle != null) return;
            GUIStyle Make(int size, TextAnchor anchor)
            {
                var s = new GUIStyle(GUI.skin.label)
                {
                    fontSize = size, fontStyle = FontStyle.Bold, alignment = anchor, clipping = TextClipping.Clip, wordWrap = false,
                    padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
                };
                s.normal.textColor = Color.white; // tinted per use through GUI.color
                return s;
            }
            _nameStyle = Make(14, TextAnchor.MiddleLeft);
            _distanceStyle = Make(11, TextAnchor.MiddleRight);
            _barStyle = Make(11, TextAnchor.MiddleCenter);
            _initialStyle = Make(26, TextAnchor.MiddleCenter);
            _headerStyle = Make(11, TextAnchor.MiddleLeft);

            // A tiny top-to-bottom light-to-dark strip that gives the bars a subtle glossy look when tinted.
            const int h = 16;
            _gradient = new Texture2D(1, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            {
                float v = Mathf.Lerp(0.62f, 1f, y / (float)(h - 1)); // y=0 is the bottom row
                _gradient.SetPixel(0, y, new Color(v, v, v, 1f));
            }
            _gradient.Apply();
        }

        private void DestroyDrawResources()
        {
            if (_gradient != null) Destroy(_gradient);
            _gradient = null;
            _nameStyle = _distanceStyle = _barStyle = _initialStyle = _headerStyle = null;
            _anim.Clear();
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return; // the panel has no buttons: draw only, never handle input
            if (!_enabled.Value || !_visible || Player.m_localPlayer == null || _members.Count == 0) return;
            if (Hud.IsUserHidden() || Menu.IsVisible()) return;
            EnsureStyles();

            // Scale with the screen so it stays the same relative size at 1440p/4K.
            float s = Mathf.Max(0.75f, Screen.height / 1080f) * Mathf.Clamp(_scale.Value, 0.5f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            float x = Screen.width / s - PanelWidth - _offsetX.Value;
            float y = _offsetY.Value;

            // Small header so the panel reads as one thing.
            Text(new Rect(x + 2f, y, PanelWidth, 16f), $"PARTY  ·  {_members.Count}", _headerStyle, new Color(Gold.r, Gold.g, Gold.b, 0.75f));
            y += 18f;

            foreach (Member m in _members) y += DrawMember(m, x, y) + 7f;
        }

        /// <summary>Draw one player's row and return how tall it was.</summary>
        private float DrawMember(Member m, float x, float y)
        {
            bool showEitr = m.HasData && m.MaxEitr > 1f;
            float height = 62f + (showEitr ? 12f : 0f);
            var panel = new Rect(x, y, PanelWidth, height);

            bool dead = m.HasData && m.MaxHp > 0f && m.Hp <= 0.5f;
            float hpFrac = m.HasData && m.MaxHp > 0f ? Mathf.Clamp01(m.Hp / m.MaxHp) : 0f;
            float stFrac = m.HasData && m.MaxSt > 0f ? Mathf.Clamp01(m.St / m.MaxSt) : 0f;
            float eitrFrac = m.HasData && m.MaxEitr > 0f ? Mathf.Clamp01(m.Eitr / m.MaxEitr) : 0f;
            Anim a = Animate(m.Id, hpFrac, stFrac, eitrFrac);

            // Fade the whole row in when a player appears, and dim it when we have no numbers for them.
            float rowAlpha = a.Alpha * (m.HasData ? 1f : 0.6f);
            GUI.color = new Color(1f, 1f, 1f, rowAlpha);

            // Soft drop shadow, then the panel: dark, with a gold edge for you and a red pulsing edge when someone's in trouble.
            Rounded(new Rect(x + 2f, y + 3f, PanelWidth, height), new Color(0f, 0f, 0f, 0.35f), PanelRadius + 1f);
            Rounded(panel, new Color(0.06f, 0.05f, 0.045f, Mathf.Clamp(_opacity.Value, 0.2f, 1f)), PanelRadius);
            Color edge = m.Self ? new Color(Gold.r, Gold.g, Gold.b, 0.85f) : new Color(0.38f, 0.32f, 0.24f, 0.9f);
            bool lowHealth = m.HasData && !dead && hpFrac < 0.25f;
            if (lowHealth) edge = Color.Lerp(edge, new Color(1f, 0.25f, 0.2f, 1f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
            RoundedOutline(panel, edge, PanelRadius);

            DrawPortrait(m, new Rect(x + 7f, y + 7f, 48f, 48f), dead);

            float bx = x + 63f, bw = PanelWidth - 63f - 9f;
            Text(new Rect(bx, y + 3f, bw - 50f, 18f), m.Name ?? "?", _nameStyle, m.Self ? Gold : Color.white);
            if (_showDistance.Value && m.Distance >= 0f && !m.Self)
                Text(new Rect(bx, y + 3f, bw, 18f), $"{m.Distance:0} m", _distanceStyle, Dim);

            var hpRect = new Rect(bx, y + 24f, bw, 18f);
            var stRect = new Rect(bx, y + 45f, bw, 12f);

            if (!m.HasData)
            {
                Bar(hpRect, 0f, 0f, HealthColor, "no data", Dim);
                Bar(stRect, 0f, 0f, StaminaColor, null, Dim);
            }
            else
            {
                Color hpColor = dead ? new Color(0.35f, 0.35f, 0.35f) : HealthColor;
                if (lowHealth) hpColor = Color.Lerp(HealthColor, new Color(1f, 0.55f, 0.45f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));

                Bar(hpRect, a.Health, a.Trail, hpColor, dead ? "DEAD" : $"{Mathf.CeilToInt(m.Hp)} / {Mathf.CeilToInt(m.MaxHp)}", Color.white);
                Bar(stRect, a.Stamina, a.Stamina, dead ? new Color(0.3f, 0.3f, 0.3f) : StaminaColor, null, Color.white);
                if (showEitr) Bar(new Rect(bx, y + 60f, bw, 8f), a.Eitr, a.Eitr, EitrColor, null, Color.white);
            }

            GUI.color = Color.white;
            return height;
        }

        /// <summary>Smooth the bars toward their real value; the trail lingers behind a health drop, then catches up.</summary>
        private Anim Animate(long id, float hp, float stamina, float eitr)
        {
            float now = Time.unscaledTime;
            if (!_anim.TryGetValue(id, out Anim a))
                _anim[id] = a = new Anim { Health = hp, Trail = hp, Stamina = stamina, Eitr = eitr, Alpha = 0f, LastTime = now };

            float dt = Mathf.Clamp(now - a.LastTime, 0f, 0.1f);
            a.LastTime = now;
            if (dt <= 0f) return a; // already updated this frame (OnGUI can repaint more than once)

            float k = 1f - Mathf.Exp(-14f * dt); // fast ease: bars move smoothly but never lag noticeably
            a.Health = Mathf.Lerp(a.Health, hp, k);
            a.Stamina = Mathf.Lerp(a.Stamina, stamina, k);
            a.Eitr = Mathf.Lerp(a.Eitr, eitr, k);
            a.Alpha = Mathf.MoveTowards(a.Alpha, 1f, dt * 4f); // ~0.25s fade-in

            if (a.Trail < a.Health) a.Trail = a.Health;                          // healed: the trail jumps with the bar
            else a.Trail = Mathf.MoveTowards(a.Trail, a.Health, dt * 0.35f);       // damaged: it drains slowly
            return a;
        }

        private void DrawPortrait(Member m, Rect r, bool dead)
        {
            const float radius = 5f;
            Texture2D picture = _showPortraits.Value ? SteamAvatars.Get(m.SteamId) : null;
            if (picture != null)
            {
                GUI.DrawTexture(r, picture, ScaleMode.ScaleAndCrop, true, 0f, dead ? new Color(0.45f, 0.45f, 0.45f, 1f) : Color.white,
                                Vector4.zero, new Vector4(radius, radius, radius, radius));
            }
            else
            {
                // No picture (yet, or not on Steam): a coloured square with their initial, the colour picked from their name.
                int hash = (m.Name ?? "?").Aggregate(17, (h, c) => h * 31 + c);
                Rounded(r, Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.45f, dead ? 0.3f : 0.55f), radius);
                string initial = string.IsNullOrEmpty(m.Name) ? "?" : m.Name.Substring(0, 1).ToUpperInvariant();
                Text(r, initial, _initialStyle, Color.white);
            }
            RoundedOutline(r, new Color(0f, 0f, 0f, 0.85f), radius);
        }

        // ---- drawing helpers -------------------------------------------------------------------
        // GUI.color (set per row) multiplies every colour below, which is how a row fades or dims as a whole.

        private static Vector4 Radii(float radius) => new Vector4(radius, radius, radius, radius);

        private static void Rounded(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, Radii(radius));

        private static void RoundedOutline(Rect r, Color c, float radius, float width = 1f) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, new Vector4(width, width, width, width), Radii(radius));

        /// <summary>
        /// A glossy rounded bar: dark background, a pale "recent damage" trail, then the coloured fill,
        /// with an optional centred label.
        /// </summary>
        private void Bar(Rect r, float fill, float trail, Color color, string label, Color labelColor)
        {
            float radius = Mathf.Min(4f, r.height / 2f);
            Rounded(r, BarBackground, radius);

            float inner = r.width - 2f;
            if (trail > fill + 0.002f)
            {
                float w = inner * Mathf.Clamp01(trail);
                GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, w, r.height - 2f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                                new Color(1f, 0.92f, 0.85f, 0.55f), Vector4.zero, Radii(Mathf.Min(radius - 1f, w / 2f)));
            }
            if (fill > 0.002f)
            {
                float w = inner * Mathf.Clamp01(fill);
                GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, w, r.height - 2f), _gradient, ScaleMode.StretchToFill, true, 0f,
                                color, Vector4.zero, Radii(Mathf.Min(radius - 1f, w / 2f)));
            }

            RoundedOutline(r, new Color(0f, 0f, 0f, 0.95f), radius);
            if (!string.IsNullOrEmpty(label)) Text(r, label, _barStyle, labelColor);
        }

        /// <summary>Text with a 1px dark shadow so it reads on any background.</summary>
        private static void Text(Rect r, string text, GUIStyle style, Color color)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.9f * previous.a);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            GUI.color = new Color(color.r, color.g, color.b, color.a * previous.a);
            GUI.Label(r, text, style);
            GUI.color = previous;
        }
    }
}
