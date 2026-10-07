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

        private GUIStyle _nameStyle, _nameCompactStyle, _distanceStyle, _barStyle, _initialStyle, _headerStyle, _arrowStyle;
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
            _nameCompactStyle = Make(12, TextAnchor.MiddleLeft);
            _arrowStyle = Make(15, TextAnchor.MiddleCenter);
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
            _nameStyle = _nameCompactStyle = _distanceStyle = _barStyle = _initialStyle = _headerStyle = _arrowStyle = null;
            _anim.Clear();
        }

        /// <summary>
        /// While you steer a ship the game shows its own display (wind direction, sail and rudder). Find where it is on the screen, in the
        /// same units we draw in, so the panel can get out of its way. False when you are not steering.
        /// </summary>
        private static bool ShipHudBounds(float scale, out Rect bounds)
        {
            bounds = default;
            Hud hud = Hud.instance;
            if (hud == null || hud.m_shipHudRoot == null || !hud.m_shipHudRoot.activeInHierarchy) return false;

            bool any = false;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var corners = new Vector3[4];
            foreach (RectTransform rt in hud.m_shipHudRoot.GetComponentsInChildren<RectTransform>(false))
            {
                if (hud.m_shipControlsRoot != null && rt.IsChildOf(hud.m_shipControlsRoot.transform)) continue; // that one floats near the wheel, not on the screen edge
                rt.GetWorldCorners(corners);
                Canvas canvas = rt.GetComponentInParent<Canvas>();
                Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (Vector3 c in corners)
                {
                    Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, c);
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
                }
                if (x1 - x0 > Screen.width * 0.5f || y1 - y0 > Screen.height * 0.5f) continue; // a container that spans the screen, not a symbol
                if (x1 - x0 < 4f || y1 - y0 < 4f) continue;
                any = true;
                minX = Mathf.Min(minX, x0); maxX = Mathf.Max(maxX, x1); minY = Mathf.Min(minY, y0); maxY = Mathf.Max(maxY, y1);
            }
            if (!any) return false;

            // screen pixels (origin bottom left) -> the drawing units here (origin top left, scaled)
            bounds = Rect.MinMaxRect(minX / scale, (Screen.height - maxY) / scale, maxX / scale, (Screen.height - minY) / scale);
            return true;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return; // the panel has no buttons: draw only, never handle input
            if (!_enabled.Value || !_visible || Player.m_localPlayer == null || _members.Count == 0) return;
            if (Hud.IsUserHidden() || Menu.IsVisible()) return;
            if (_hideInMenus.Value && ScreenCoversPanel()) return;
            EnsureStyles();

            // Scale with the screen so it stays the same relative size at 1440p/4K.
            float s = Mathf.Max(0.75f, Screen.height / 1080f) * Mathf.Clamp(_scale.Value, 0.5f, 2f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            float x = _onLeft.Value ? _offsetX.Value : Screen.width / s - PanelWidth - _offsetX.Value;
            float y = _offsetY.Value;

            // Steering a ship: the game shows its wind and sail display on this side, so sit below it instead of covering it.
            if (_avoidShipHud.Value && ShipHudBounds(s, out Rect ship) && ship.xMax > x - 8f && ship.xMin < x + PanelWidth + 8f)
                y = Mathf.Max(y, ship.yMax + 10f);

            // Small header so the panel reads as one thing.
            Text(new Rect(x + 2f, y, PanelWidth, 16f), $"PARTY  ·  {_members.Count(m => !m.Companion)}", _headerStyle, new Color(Gold.r, Gold.g, Gold.b, 0.75f));
            y += 18f;

            foreach (Member m in _members) y += m.Companion ? DrawCompanion(m, x, y) + 5f : DrawMember(m, x, y) + 7f;
            GUI.matrix = previousMatrix; // leave the drawing scale as we found it, for whatever draws after us
        }

        /// <summary>
        /// Is a screen open that would sit under the panel (inventory, crafting, build menu, trader, big map)?
        /// Our panel is drawn on top of the game's own UI, so we simply get out of the way.
        /// </summary>
        private static bool ScreenCoversPanel() =>
            InventoryGui.IsVisible() || StoreGui.IsVisible() || Minimap.IsOpen() || Hud.IsPieceSelectionVisible() || TextInput.IsVisible();

        /// <summary>The way a player is, as an angle from where the camera looks (0 = ahead, 90 = to your right), or null if we do not know where they are.</summary>
        private static float? BearingTo(Member m)
        {
            Player me = Player.m_localPlayer;
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : (Camera.main != null ? Camera.main.transform : null);
            if (me == null || cam == null || !m.HasPos) return null;
            Vector3 toThem = m.Pos - me.transform.position; toThem.y = 0f;
            Vector3 ahead = cam.forward; ahead.y = 0f;
            if (toThem.sqrMagnitude < 4f || ahead.sqrMagnitude < 0.0001f) return null; // standing on top of each other, or looking straight down
            return Vector3.SignedAngle(ahead, toThem, Vector3.up);
        }

        /// <summary>A small arrow, turned by <paramref name="degrees"/> around its own centre (the rest of the drawing is left as it was).</summary>
        private void Arrow(Rect r, float degrees, Color color)
        {
            // (Not GUIUtility.RotateAroundPivot: it works in screen pixels, but we draw in scaled units, so it flung the arrow around the screen.)
            Matrix4x4 saved = GUI.matrix;
            Vector3 c = new Vector3(r.center.x, r.center.y, 0f);
            GUI.matrix = saved * Matrix4x4.TRS(c, Quaternion.Euler(0f, 0f, degrees), Vector3.one) * Matrix4x4.TRS(-c, Quaternion.identity, Vector3.one);
            Text(r, "↑", _arrowStyle, color);
            GUI.matrix = saved;
        }

        /// <summary>Draw one player's row and return how tall it was. A compact row is smaller all over.</summary>
        private float DrawMember(Member m, float x, float y)
        {
            bool compact = _compact.Value;
            bool showEitr = m.HasData && m.MaxEitr > 1f;
            int shownEffects = _showEffects.Value ? Mathf.Min(m.Effects.Count, 12) : 0;
            bool showFood = _showFood.Value && m.HasData && m.FoodKnown;
            // The extra band along the bottom holds the buff icons (beside the bars) and the three food slots (under the picture).
            float band = (shownEffects > 0 || showFood) ? (compact ? (shownEffects > 0 ? 15f : 12f) : 21f) : 0f;
            float effectsRow = shownEffects > 0 ? (compact ? 15f : band) : 0f;
            float height = (compact ? 38f + (showEitr ? 5f : 0f) : 62f + (showEitr ? 12f : 0f)) + band;
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

            // The picture, ringed in a colour that says how healthy they are (green, then amber, then red) so a glance is enough.
            float pic = compact ? 28f : 48f;
            var portrait = new Rect(x + (compact ? 5f : 7f), y + (compact ? 5f : 7f), pic, pic);
            DrawPortrait(m, portrait, dead);
            if (m.HasData)
            {
                Color ring = dead ? new Color(0.4f, 0.4f, 0.4f, 0.9f)
                           : Color.Lerp(new Color(0.9f, 0.2f, 0.18f, 0.95f), new Color(0.35f, 0.85f, 0.4f, 0.95f), Mathf.Clamp01(a.Health * 1.15f));
                RoundedOutline(new Rect(portrait.x - 1.5f, portrait.y - 1.5f, portrait.width + 3f, portrait.height + 3f), ring, 6.5f, 2f);
            }

            float bx = x + (compact ? 40f : 63f), bw = PanelWidth - (compact ? 40f : 63f) - 9f;

            // Name on the left; on the right, an arrow toward them and how far away they are.
            Text(new Rect(bx, y + (compact ? 2f : 3f), bw - 62f, compact ? 15f : 18f), m.Name ?? "?", compact ? _nameCompactStyle : _nameStyle, m.Self ? Gold : Color.white);
            if (_showDistance.Value && m.Distance >= 0f && !m.Self)
            {
                Text(new Rect(bx, y + (compact ? 2f : 3f), bw, compact ? 15f : 18f), $"{m.Distance:0} m", _distanceStyle, Dim);
                float? bearing = _showArrow.Value ? BearingTo(m) : null;
                if (bearing.HasValue)
                    Arrow(new Rect(bx + bw - 54f, y + (compact ? 3f : 4f), 14f, compact ? 13f : 16f), bearing.Value, new Color(0.95f, 0.85f, 0.55f));
            }

            Rect hpRect = compact ? new Rect(bx, y + 18f, bw, 12f) : new Rect(bx, y + 24f, bw, 18f);
            Rect stRect = compact ? new Rect(bx, y + 32f, bw, 5f) : new Rect(bx, y + 45f, bw, 12f);

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
                if (showEitr) Bar(compact ? new Rect(bx, y + 39f, bw, 4f) : new Rect(bx, y + 60f, bw, 8f), a.Eitr, a.Eitr, EitrColor, null, Color.white);
            }

            // Three food slots under the picture: what they are eating, with a thin line for how long each has left.
            if (showFood)
            {
                float slot = compact ? 9f : 14f, gapX = compact ? 1.5f : 3f;
                float sy = portrait.yMax + (compact ? 3f : 4f);
                for (int i = 0; i < 3; i++)
                {
                    var cell = new Rect(portrait.x + i * (slot + gapX), sy, slot, slot);
                    Rounded(cell, new Color(0f, 0f, 0f, 0.55f), 3f);
                    if (i < m.Foods.Count)
                    {
                        FoodSlot food = m.Foods[i];
                        if (food.Icon != null && food.Icon.texture != null)
                        {
                            Texture2D tex = food.Icon.texture;
                            Rect sr = food.Icon.textureRect;
                            GUI.DrawTextureWithTexCoords(new Rect(cell.x + 0.5f, cell.y + 0.5f, slot - 1f, slot - 1f), tex, new Rect(sr.x / tex.width, sr.y / tex.height, sr.width / tex.width, sr.height / tex.height));
                        }
                        float h = Mathf.Max(1.5f, 2f);
                        Rounded(new Rect(cell.x, cell.yMax + 1f, slot, h), new Color(0f, 0f, 0f, 0.7f), 1f);
                        Color timeColor = food.Fraction < 0.2f ? new Color(1f, 0.45f, 0.35f, 0.95f) : new Color(0.55f, 0.9f, 0.45f, 0.95f);
                        Rounded(new Rect(cell.x, cell.yMax + 1f, slot * Mathf.Clamp01(food.Fraction), h), timeColor, 1f);
                    }
                    else RoundedOutline(cell, new Color(0.45f, 0.4f, 0.3f, 0.5f), 3f);
                }
            }

            // Their buffs and debuffs (food, rested, wet, poison...): a small icon each, with a thin line under it for the time left.
            if (shownEffects > 0)
            {
                float size = compact ? 12f : 17f;
                float ey = y + height - effectsRow + (compact ? 0f : 1f);
                for (int i = 0; i < shownEffects; i++)
                {
                    Effect fx = m.Effects[i];
                    float ex = (compact && !showFood ? x + 7f : bx) + i * (size + 3f); // (with food slots under the picture, the buffs go beside the bars)
                    if (ex + size > x + PanelWidth - 4f) break; // no room for more
                    var icon = new Rect(ex, ey, size, size);
                    if (fx.Icon != null && fx.Icon.texture != null)
                    {
                        Texture2D tex = fx.Icon.texture;
                        Rect sr = fx.Icon.textureRect;
                        GUI.DrawTextureWithTexCoords(icon, tex, new Rect(sr.x / tex.width, sr.y / tex.height, sr.width / tex.width, sr.height / tex.height));
                    }
                    if (fx.Fraction < 0.999f)
                    {
                        Rounded(new Rect(ex, ey + size + 1f, size, 2f), new Color(0f, 0f, 0f, 0.7f), 1f);
                        Rounded(new Rect(ex, ey + size + 1f, size * fx.Fraction, 2f), new Color(0.95f, 0.8f, 0.4f, 0.95f), 1f);
                    }
                }
            }

            GUI.color = Color.white;
            return height;
        }

        private static readonly Color CompanionColor = new Color(0.36f, 0.62f, 0.56f);

        /// <summary>
        /// A companion's row (AICompanion mod): smaller and indented under its owner, joined to it by a thin line. Its picture is its initial,
        /// then its name, how far away it is and which way, its health, and what it is doing (or that it has fallen).
        /// </summary>
        private float DrawCompanion(Member m, float x, float y)
        {
            bool compact = _compact.Value;
            float indent = compact ? 14f : 20f, w = PanelWidth - indent, height = compact ? 30f : 48f;
            float px = x + indent;
            bool dead = m.MaxHp <= 0f || m.Hp <= 0.5f;
            float hpFrac = m.MaxHp > 0f ? Mathf.Clamp01(m.Hp / m.MaxHp) : 0f;
            float stFrac = m.MaxSt > 0f ? Mathf.Clamp01(m.St / m.MaxSt) : 0f;
            Anim a = Animate(m.Id, hpFrac, stFrac, 0f);
            GUI.color = new Color(1f, 1f, 1f, a.Alpha);

            Color line = new Color(0.55f, 0.45f, 0.25f, 0.7f);
            float lx = x + indent * 0.45f;
            Rounded(new Rect(lx, y - 6f, 2f, height / 2f + 7f), line, 1f);
            Rounded(new Rect(lx, y + height / 2f - 1f, px - lx, 2f), line, 1f);

            var panel = new Rect(px, y, w, height);
            Rounded(new Rect(px + 2f, y + 3f, w, height), new Color(0f, 0f, 0f, 0.3f), PanelRadius);
            Rounded(panel, new Color(0.05f, 0.06f, 0.055f, Mathf.Clamp(_opacity.Value, 0.2f, 1f)), PanelRadius);
            Color edge = dead ? new Color(0.4f, 0.4f, 0.4f, 0.8f) : new Color(CompanionColor.r, CompanionColor.g, CompanionColor.b, 0.85f);
            bool low = !dead && hpFrac < 0.25f;
            if (low) edge = Color.Lerp(edge, new Color(1f, 0.25f, 0.2f, 1f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
            RoundedOutline(panel, edge, PanelRadius);

            float pic = compact ? 20f : 36f;
            var portrait = new Rect(px + 5f, y + (height - pic) / 2f, pic, pic);
            Rounded(portrait, dead ? new Color(0.25f, 0.25f, 0.25f) : CompanionColor * 0.8f, 4f);
            // Its face, when the AICompanion mod has a picture of it (it takes one of companions near you); otherwise its initial.
            Texture face = null;
            if (AppDomain.CurrentDomain.GetData("DHack.CompanionPortrait") is Func<long, Texture> faces) { try { face = faces(m.Id); } catch (Exception) { } }
            if (face != null)
                GUI.DrawTexture(portrait, face, ScaleMode.ScaleAndCrop, true, 0f, dead ? new Color(0.45f, 0.45f, 0.45f, 1f) : Color.white, Vector4.zero, Radii(4f));
            else
                Text(portrait, string.IsNullOrEmpty(m.Name) ? "?" : m.Name.Substring(0, 1).ToUpperInvariant(), compact ? _nameCompactStyle : _nameStyle, Color.white);
            RoundedOutline(portrait, new Color(0f, 0f, 0f, 0.85f), 4f);

            float bx = portrait.xMax + 6f, bw = px + w - 7f - bx;
            Text(new Rect(bx, y + 2f, bw - 58f, compact ? 13f : 15f), m.Name ?? "?", compact ? _nameCompactStyle : _nameStyle, dead ? Dim : new Color(0.8f, 0.95f, 0.9f));
            if (_showDistance.Value && m.Distance >= 0f)
            {
                Text(new Rect(bx, y + 2f, bw, compact ? 13f : 15f), $"{m.Distance:0} m", _distanceStyle, Dim);
                float? bearing = _showArrow.Value ? BearingTo(m) : null;
                if (bearing.HasValue) Arrow(new Rect(bx + bw - 50f, y + 2f, 14f, compact ? 13f : 15f), bearing.Value, new Color(0.75f, 0.95f, 0.85f));
            }
            Rect hp = compact ? new Rect(bx, y + 16f, bw, 7f) : new Rect(bx, y + 18f, bw, 11f);
            Bar(hp, a.Health, a.Trail, dead ? new Color(0.3f, 0.3f, 0.3f) : HealthColor, compact ? null : (dead ? "FALLEN" : $"{Mathf.CeilToInt(m.Hp)} / {Mathf.CeilToInt(m.MaxHp)}"), Color.white);
            if (m.MaxSt > 0f) Bar(compact ? new Rect(bx, y + 24f, bw, 4f) : new Rect(bx, y + 31f, bw, 5f), a.Stamina, a.Stamina, dead ? new Color(0.3f, 0.3f, 0.3f) : StaminaColor, null, Color.white);

            // Its buffs (a boss power, meads...) as small icons on the right of the status line.
            float iconsWidth = 0f;
            if (_showEffects.Value && !dead && m.Effects.Count > 0)
            {
                float size = compact ? 10f : 12f;
                int n = Mathf.Min(m.Effects.Count, 6);
                iconsWidth = n * (size + 2f);
                for (int i = 0; i < n; i++)
                {
                    Effect fx = m.Effects[i];
                    if (fx.Icon == null || fx.Icon.texture == null) continue;
                    var icon = compact ? new Rect(px + w - 6f - (i + 1) * (size + 2f), y + 2f, size, size) : new Rect(px + w - 6f - (i + 1) * (size + 2f), y + 36f, size, size);
                    Texture2D tex = fx.Icon.texture;
                    Rect sr = fx.Icon.textureRect;
                    GUI.DrawTextureWithTexCoords(icon, tex, new Rect(sr.x / tex.width, sr.y / tex.height, sr.width / tex.width, sr.height / tex.height));
                }
            }
            if (!compact && !string.IsNullOrEmpty(m.Status))
                Text(new Rect(bx, y + 35f, bw - iconsWidth, 12f), dead ? "fallen: gear in a crate (skull on the map)" : m.Status, _headerStyle, Dim);
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
