using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;
using UnityEngine.UI;

namespace PartyHud
{
    /// <summary>
    /// The panel, using embedded carved artwork and the game's fonts and icons: it hides with the HUD and scales with the player's
    /// interface scale. Rows are built again only when who is shown (or a setting) changes; the bars, texts, pictures and arrows in them are
    /// updated in place, the bars and arrows every frame.
    /// </summary>
    public partial class Plugin
    {
        private const string PanelName = "PartyHudPanel";
        private float PanelWidth => _compact.Value ? _compactWidth.Value : _panelWidth.Value;

        private static readonly Color HealthColor = new Color(0.95f, 0.13f, 0.10f);
        private static readonly Color StaminaColor = new Color(1f, 0.77f, 0.12f);
        private static readonly Color EitrColor = new Color(0.66f, 0.46f, 0.96f);
        private static readonly Color CompanionColor = new Color(0.36f, 0.62f, 0.56f);
        private static readonly Color Grey = new Color(0.3f, 0.3f, 0.3f);
        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        /// <summary>Per-player animation state: the smoothed bars, the "recent damage" trail behind health, and the fade-in.</summary>
        private class Anim { public float Health, Trail, Stamina, Eitr, Alpha; public float LastTime; }
        private readonly Dictionary<long, Anim> _anim = new Dictionary<long, Anim>();

        /// <summary>A small icon with a thin line under it for the time it has left (a buff, a food).</summary>
        private class Slot { public Image Back, Icon; public GameUi.Meter Time; }

        /// <summary>One row on screen, bound to the member at the same index in _members.</summary>
        private class Row
        {
            public bool Companion, Compact;
            public RectTransform Rect;
            public CanvasGroup Group;
            public float Height;
            public GameUi.Edge Edge, Ring;
            public Image PictureBack;
            public RawImage Picture;
            public TMP_Text Initial, Name, Distance, Status;
            public RectTransform Arrow;
            public Graphic ArrowImage;   // the minimap's arrow, or a "^" if a game update moved it
            public GameUi.Meter Health, Stamina, Eitr;
            public readonly List<Slot> Foods = new List<Slot>(), Effects = new List<Slot>();
        }

        private RectTransform _panel;
        private TMP_Text _header;
        private readonly List<Row> _rows = new List<Row>();
        private string _built = "";
        private bool _membersChanged;      // BuildMembers ran: check the rows and refresh their texts and pictures
        private float _nextShipCheck;
        private float _retryAt;
        private bool _loggedFailure;

        /// <summary>From LateUpdate (after the camera moved, so the arrows point right this frame).</summary>
        private void UpdatePanel()
        {
            if (Time.unscaledTime < _retryAt) return;
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                // a failure here would repeat every frame: say it once, hide the panel, and try again in a few seconds
                if (!_loggedFailure) Logger.LogError("The party panel failed and is hidden for now: " + e);
                _loggedFailure = true;
                _retryAt = Time.unscaledTime + 5f;
                DestroyPanel();
            }
        }

        private void Tick()
        {
            bool show = _enabled.Value && _visible && Player.m_localPlayer != null && _members.Count > 0 && GameUi.HudRoot != null &&
                        !Menu.IsVisible() && !(_hideInMenus.Value && ScreenCoversPanel());
            if (!show)
            {
                if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
                return;
            }

            if (_panel == null || _membersChanged || _rows.Count != _members.Count)
            {
                if (!GameUi.Ready()) return;
                string signature = Signature();
                if (_panel == null || signature != _built || _rows.Count != _members.Count) Build(signature);
                _membersChanged = false;
                Refresh();
            }
            if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);

            if (Time.unscaledTime >= _nextShipCheck) { _nextShipCheck = Time.unscaledTime + 0.25f; Place(); }
            for (int i = 0; i < _rows.Count; i++) Animate(_rows[i], _members[i]);
        }

        /// <summary>Everything that decides which objects the panel is made of: when it changes, the panel is built again.</summary>
        private string Signature()
        {
            var sb = new StringBuilder();
            sb.Append(_compact.Value).Append(_showDistance.Value).Append(_showArrow.Value).Append(_showEffects.Value).Append(_showFood.Value)
              .Append(_onLeft.Value).Append(',').Append(_scale.Value).Append(',').Append(_opacity.Value).Append(',').Append(_offsetX.Value)
              .Append(',').Append(_offsetY.Value)
              .Append(',').Append(_panelWidth.Value).Append(',').Append(_compactWidth.Value).Append(',').Append(_rowSpacing.Value)
              .Append(',').Append(_textScale.Value).Append(',').Append(_hudOpacity.Value).Append(',').Append(_headerX.Value)
              .Append(',').Append(_headerY.Value).Append(',').Append(_headerFontSize.Value).Append(',').Append(_headerWidth.Value).Append(',').Append(_anchorBottom.Value)
              .Append(',').Append(_fitToScreen.Value).Append(',').Append(_keepOnScreen.Value);
            foreach (Member m in _members)
            {
                sb.Append('|').Append(m.Id).Append(m.Companion ? 'c' : 'p').Append(m.Self ? 's' : '-').Append(m.HasData ? 'd' : '-');
                if (m.Companion) sb.Append(CompanionEffects(m)).Append(CompanionFood(m) ? 'f' : '-').Append(m.MaxSt > 0f ? 'm' : '-');
                else sb.Append(PlayerEffects(m)).Append(PlayerFood(m) ? 'f' : '-').Append(ShowsEitr(m) ? 'e' : '-');
            }
            return sb.ToString();
        }

        private bool ShowsEitr(Member m) => m.HasData && m.MaxEitr > 1f;
        private bool PlayerFood(Member m) => _showFood.Value && m.HasData && m.FoodKnown;
        private int PlayerEffects(Member m) => _showEffects.Value ? Mathf.Min(m.Effects.Count, 12) : 0;
        private bool CompanionFood(Member m) => _showFood.Value && m.FoodKnown && !_compact.Value;
        private int CompanionEffects(Member m) => _showEffects.Value ? Mathf.Min(m.Effects.Count, 6) : 0;

        private void DestroyPanel()
        {
            if (_panel != null) Destroy(_panel.gameObject);
            _panel = null; _header = null;
            _rows.Clear();
            _built = "";
        }

        /// <summary>Remove the panel, and any a previous copy of the mod left on the HUD (a hot reload: its types differ from ours).</summary>
        private void DestroyDrawResources()
        {
            DestroyPanel();
            Transform hud = GameUi.HudRoot;
            if (hud != null) foreach (Transform old in hud.Cast<Transform>().Where(t => t.name == PanelName).ToList()) Destroy(old.gameObject);
            GameUi.Destroy();
            HudSkin.Destroy();
            _anim.Clear();
        }

        // ---- building ---------------------------------------------------------------------------------------------------------------

        private void Build(string signature)
        {
            DestroyPanel();
            Transform hud = GameUi.HudRoot;
            foreach (Transform old in hud.Cast<Transform>().Where(t => t.name == PanelName).ToList()) Destroy(old.gameObject);

            var anchor = new Vector2(_onLeft.Value ? 0f : 1f, _anchorBottom.Value ? 0f : 1f);
            _panel = GameUi.Rect(hud, PanelName, anchor, anchor, Vector2.zero, new Vector2(PanelWidth + 16f, 0f));
            _panel.localScale = Vector3.one * _scale.Value;
            // its own canvas: the bars change every frame, and without one Unity rebuilds the whole HUD's meshes each time
            _panel.gameObject.AddComponent<Canvas>();
            CanvasGroup group = _panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = _hudOpacity.Value;
            group.blocksRaycasts = false; group.interactable = false;   // (nothing to click: never take the mouse from the game)

            // The raised shoulder and the surrounding border are one continuous piece of artwork.
            RectTransform surround = GameUi.Stretch(_panel, "PartySurround");
            HudSkin.Surround(surround, _opacity.Value);
            // Centre the label within the flat shoulder, above the lower top edge.
            // Its short line box prevents vertical centring from dropping the words into the body.
            float headerSize = _headerFontSize.Value;
            _header = GameUi.Label(_panel, "", new Vector2(_headerX.Value, -_headerY.Value), new Vector2(_headerWidth.Value, 18f * headerSize / 15f), headerSize, GameUi.Warm,
                                   null, TextAlignmentOptions.Center);
            _header.enableAutoSizing = true;
            _header.fontSizeMin = headerSize * (2f / 3f);
            _header.fontSizeMax = headerSize;
            _header.textWrappingMode = TextWrappingModes.NoWrap;
            float y = 33f;
            foreach (Member m in _members)
            {
                Row row = m.Companion ? BuildCompanion(m, y) : BuildPlayer(m, y);
                _rows.Add(row);
                y += row.Height + _rowSpacing.Value;
            }
            _panel.sizeDelta = new Vector2(PanelWidth + 16f, y + 8f);
            _built = signature;

            // forget the animation of people no longer shown (they fade in again when they come back)
            foreach (long gone in _anim.Keys.Where(id => _members.All(m => m.Id != id)).ToList()) _anim.Remove(gone);
            _nextShipCheck = 0f;
            Place();
        }

        /// <summary>A row's carved frame, dark backing controlled by Opacity, and a live warning edge.</summary>
        private Row NewRow(string name, float y, float indent, float height, bool companion)
        {
            var row = new Row { Companion = companion, Compact = _compact.Value, Height = height };
            row.Rect = GameUi.At(_panel, name, 8f, y, PanelWidth, height);
            row.Group = row.Rect.gameObject.AddComponent<CanvasGroup>();
            row.Group.alpha = 0f;
            GameUi.Plain(GameUi.At(row.Rect, "DarkWoodBacking", indent + 1f, 1f, PanelWidth - indent - 2f, height - 2f),
                         new Color(0.015f, 0.013f, 0.011f, _opacity.Value));
            RectTransform box = GameUi.At(row.Rect, "CarvedPanel", indent, 0f, PanelWidth - indent, height);
            HudSkin.Panel(box, companion, _opacity.Value);
            row.Edge = GameUi.Frame(box, 0.8f, -2f, Clear);
            return row;
        }

        /// <summary>The picture: a coloured square with the initial, and the picture over it once there is one; a ring round it for health.</summary>
        private void BuildPicture(Row row, Member m, float x, float y, float size, float initialSize, bool ring)
        {
            RectTransform r = GameUi.At(row.Rect, "Portrait", x, y, size, size);
            row.PictureBack = GameUi.Plain(r, Grey);
            row.Initial = GameUi.Label(r, string.IsNullOrEmpty(m.Name) ? "?" : m.Name.Substring(0, 1).ToUpperInvariant(), Vector2.zero, new Vector2(size, size),
                                       initialSize * _textScale.Value, Color.white, null, TextAlignmentOptions.Center);
            row.Initial.textWrappingMode = TextWrappingModes.NoWrap;
            row.Picture = GameUi.Picture(r, Vector2.zero, new Vector2(size, size));
            HudSkin.Bevel(r, row.Companion ? CompanionColor : HudSkin.Bronze);
            if (ring) row.Ring = GameUi.Frame(r, 0.7f, -1f, Clear);
        }

        private TMP_Text NameLabel(Row row, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align)
        {
            size *= _textScale.Value;
            // tall enough for a line of this size: with Ellipsis, TMP hides a whole line that is even a little taller than its box
            // (Averia's lines are about 1.3 times the font size), so a name in an 18-high box at 16 pt never showed
            float tall = Mathf.Max(h, size * 1.8f);   // (1.8: Chinese, Japanese and Korean come from a fallback font with taller lines)
            TextAlignmentOptions top = align == TextAlignmentOptions.Right ? TextAlignmentOptions.TopRight
                                     : align == TextAlignmentOptions.Center ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
            TMP_Text t = GameUi.Label(row.Rect, "", new Vector2(x, -y + (tall - h) * 0.5f - 1f), new Vector2(w, tall), size, color, null, top);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>The arrow pointing the way someone is, turned around its own centre.</summary>
        private void BuildArrow(Row row, float x, float y, float w, float h, Color color)
        {
            Sprite sprite = GameUi.ArrowSprite();
            row.Arrow = GameUi.Rect(row.Rect, "Arrow", new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(x + w / 2f, -y - h / 2f), new Vector2(w, h));
            if (sprite != null)
            {
                Image image = row.Arrow.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                row.ArrowImage = image;
            }
            else
            {
                TMP_Text caret = GameUi.Label(row.Arrow, "^", Vector2.zero, new Vector2(w, h), h * 1.2f, color, null, TextAlignmentOptions.Center);
                caret.textWrappingMode = TextWrappingModes.NoWrap;
                row.ArrowImage = caret;
            }
            row.ArrowImage.color = color;
            row.ArrowImage.raycastTarget = false;
            row.ArrowImage.enabled = false;
        }

        private GameUi.Meter BuildMeter(Transform parent, Vector2 pos, Vector2 size, Color fill, bool trail = false, float fontSize = 0f) =>
            GameUi.LiveBar(parent, pos, size, fill, trail, fontSize * _textScale.Value);

        private Slot BuildSlot(Row row, float x, float y, float size, bool back, float lineHeight)
        {
            var slot = new Slot();
            RectTransform cell = GameUi.At(row.Rect, "Slot", x, y, size, size);
            if (back)
            {
                slot.Back = GameUi.Plain(cell, new Color(0f, 0f, 0f, 0.55f));
                HudSkin.Bevel(cell, HudSkin.Bronze);
            }
            RectTransform icon = back ? GameUi.Stretch(cell, "Icon", -0.5f, -0.5f, -0.5f, -0.5f) : GameUi.Stretch(cell, "Icon");
            slot.Icon = icon.gameObject.AddComponent<Image>();
            slot.Icon.preserveAspect = true;
            slot.Icon.raycastTarget = false;
            slot.Icon.enabled = false;
            slot.Time = BuildMeter(row.Rect, new Vector2(x, -(y + size + 1f)), new Vector2(size, lineHeight), GameUi.Good);
            slot.Time.SetActive(false);
            return slot;
        }

        /// <summary>A player's row: picture, name, distance and arrow, health, stamina and Eitr, food under the picture, buffs beside it.</summary>
        private Row BuildPlayer(Member m, float y)
        {
            bool compact = _compact.Value;
            bool showEitr = ShowsEitr(m), showFood = PlayerFood(m);
            int shownEffects = PlayerEffects(m);
            // the extra band along the bottom holds the buff icons (beside the bars) and the three food slots (under the picture)
            float band = (shownEffects > 0 || showFood) ? (compact ? (shownEffects > 0 ? 15f : 12f) : 24f) : 0f;
            float effectsRow = shownEffects > 0 ? (compact ? 15f : band) : 0f;
            float height = (compact ? 38f + (showEitr ? 5f : 0f) : 76f + (showEitr ? 12f : 0f)) + band;
            Row row = NewRow("Player", y, 0f, height, false);

            float pic = compact ? 28f : 60f, px = compact ? 5f : 10f;
            BuildPicture(row, m, px, px, pic, compact ? 15f : 26f, true);

            float bx = compact ? 40f : 82f, bw = PanelWidth - bx - 12f;
            float ty = compact ? 2f : 6f, th = compact ? 15f : 24f;
            row.Name = NameLabel(row, bx, ty, (m.Self || !_showDistance.Value ? bw : bw - 66f), th, compact ? 13f : 21f, m.Self ? GameUi.Gold : GameUi.Text, TextAlignmentOptions.Left);
            if (_showDistance.Value && !m.Self)
            {
                row.Distance = NameLabel(row, bx, ty, bw, th, compact ? 11f : 14f, GameUi.Dim, TextAlignmentOptions.Right);
                if (_showArrow.Value) BuildArrow(row, bx + bw - 54f, compact ? 3f : 8f, 14f, compact ? 13f : 16f, new Color(0.95f, 0.85f, 0.55f));
            }

            row.Health = compact ? BuildMeter(row.Rect, new Vector2(bx, -18f), new Vector2(bw, 12f), HealthColor, true, 10f)
                                 : BuildMeter(row.Rect, new Vector2(bx, -36f), new Vector2(bw, 18f), HealthColor, true, 13f);
            row.Stamina = compact ? BuildMeter(row.Rect, new Vector2(bx, -32f), new Vector2(bw, 5f), StaminaColor)
                                  : BuildMeter(row.Rect, new Vector2(bx, -60f), new Vector2(bw, 10f), StaminaColor);
            if (showEitr)
                row.Eitr = compact ? BuildMeter(row.Rect, new Vector2(bx, -39f), new Vector2(bw, 4f), EitrColor)
                                   : BuildMeter(row.Rect, new Vector2(bx, -75f), new Vector2(bw, 8f), EitrColor);

            // three food slots under the picture: what they are eating, with a thin line for how long each has left
            if (showFood)
            {
                float slot = compact ? 9f : 17f, gap = compact ? 1.5f : 4f, sy = px + pic + (compact ? 3f : 8f);
                for (int i = 0; i < 3; i++) row.Foods.Add(BuildSlot(row, px + i * (slot + gap), sy, slot, true, 2f));
            }

            // their buffs and debuffs (food, rested, wet, poison...): a small icon each, with a thin line under it for the time left
            if (shownEffects > 0)
            {
                float size = compact ? 12f : 19f, ey = height - effectsRow + (compact ? 0f : 1f);
                for (int i = 0; i < shownEffects; i++)
                {
                    float ex = (compact && !showFood ? 7f : bx) + i * (size + 3f);   // (with food slots under the picture, the buffs go beside the bars)
                    if (ex + size > PanelWidth - 4f) break;                          // no room for more
                    row.Effects.Add(BuildSlot(row, ex, ey, size, false, 2f));
                }
            }
            return row;
        }

        /// <summary>
        /// A companion's row (AICompanion mod): green trim below its owner, indented in compact mode. Its picture (or initial),
        /// name, how far away it is and which way, health and stamina, food, buffs, and what it is doing (or that it has fallen).
        /// </summary>
        private Row BuildCompanion(Member m, float y)
        {
            bool compact = _compact.Value, food = CompanionFood(m);
            float indent = compact ? 14f : 0f, height = compact ? 38f : 100f;
            Row row = NewRow("Companion", y, indent, height, true);
            float px = compact ? indent + 5f : 10f, pic = compact ? 24f : 60f;
            BuildPicture(row, m, px, compact ? 6f : 10f, pic, compact ? 12f : 26f, true);
            if (food)
                for (int i = 0; i < 3; i++) row.Foods.Add(BuildSlot(row, px + i * 21f, 78f, 17f, true, 1.5f));

            float bx = compact ? px + pic + 7f : 82f, bw = PanelWidth - bx - 12f;
            float ty = compact ? 2f : 6f, th = compact ? 14f : 24f;
            row.Name = NameLabel(row, bx, ty, _showDistance.Value ? bw - 66f : bw, th,
                                 compact ? 12f : 21f, GameUi.Text, TextAlignmentOptions.Left);
            if (_showDistance.Value)
            {
                row.Distance = NameLabel(row, bx, ty, bw, th, compact ? 11f : 14f, GameUi.Text, TextAlignmentOptions.Right);
                if (_showArrow.Value) BuildArrow(row, bx + bw - 56f, compact ? 3f : 8f, 14f, th - 4f, new Color(0.75f, 0.95f, 0.85f));
            }
            row.Health = BuildMeter(row.Rect, new Vector2(bx, compact ? -19f : -36f),
                                       new Vector2(bw, compact ? 8f : 18f), HealthColor, true, compact ? 0f : 13f);
            if (m.MaxSt > 0f)
                row.Stamina = BuildMeter(row.Rect, new Vector2(bx, compact ? -31f : -60f),
                                            new Vector2(bw, compact ? 4f : 10f), StaminaColor);
            int n = CompanionEffects(m);
            float size = compact ? 10f : 18f;
            // Compact buffs stay off the name/distance line and do not obscure the bars.
            if (compact && n > 0)
            {
                row.Height += 16f;
                row.Rect.sizeDelta = new Vector2(PanelWidth, row.Height);
                row.Rect.Find("CarvedPanel").GetComponent<RectTransform>().sizeDelta = new Vector2(PanelWidth - indent, row.Height);
                row.Rect.Find("DarkWoodBacking").GetComponent<RectTransform>().sizeDelta = new Vector2(PanelWidth - indent - 2f, row.Height - 2f);
            }
            for (int i = 0; i < n; i++)
                row.Effects.Add(BuildSlot(row, PanelWidth - 12f - (i + 1) * (size + 3f), compact ? 39f : 78f, size, false, 1f));
            if (!compact)
            {
                row.Status = NameLabel(row, bx, 77f, bw - n * (size + 3f) - 4f, 16f, 13f, GameUi.Dim, TextAlignmentOptions.Left);
                row.Status.fontStyle = FontStyles.Italic;
            }
            return row;
        }

        // ---- updating in place ------------------------------------------------------------------------------------------------------

        /// <summary>After the members were read again (5 times a second): texts, pictures, food and buff icons.</summary>
        private void Refresh()
        {
            GameUi.SetText(_header, T("partyhud_header", _members.Count(m => !m.Companion)));
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                Member m = _members[i];
                bool dead = IsDead(m);
                GameUi.SetText(row.Name, m.Name ?? "?");
                GameUi.SetText(row.Distance, _showDistance.Value && m.Distance >= 0f ? T("partyhud_distance", m.Distance.ToString("0")) : "");

                if (row.Companion)
                {
                    if (row.Name != null) row.Name.color = dead ? GameUi.Dim : GameUi.Text;
                    row.Health.SetText(dead ? T("partyhud_fallen") : $"{Mathf.CeilToInt(m.Hp)} / {Mathf.CeilToInt(m.MaxHp)}");
                    GameUi.SetText(row.Status, string.IsNullOrEmpty(m.Status) ? "" : dead ? T("partyhud_fallen_status") : m.Status);
                    // its face, when the AICompanion mod has a picture of it (it takes one of companions near you); otherwise its initial
                    Texture face = null;
                    if (AppDomain.CurrentDomain.GetData("DHack.CompanionPortrait") is Func<long, Texture> faces) { try { face = faces(m.Id); } catch (Exception) { } }
                    row.PictureBack.color = dead ? new Color(0.25f, 0.25f, 0.25f) : CompanionColor * 0.8f;
                    GameUi.Show(row.Picture, face, dead ? new Color(0.45f, 0.45f, 0.45f, 1f) : Color.white);
                }
                else
                {
                    row.Health.SetText(!m.HasData ? T("partyhud_no_data") : dead ? T("partyhud_dead") : $"{Mathf.CeilToInt(m.Hp)} / {Mathf.CeilToInt(m.MaxHp)}");
                    if (!m.HasData) row.Health.Text.color = GameUi.Dim; else row.Health.Text.color = Color.white;
                    Texture2D picture = _showPortraits.Value ? SteamAvatars.Get(m.SteamId) : null;
                    // no picture (yet, or not on Steam): a coloured square with their initial, the colour picked from their name
                    int hash = (m.Name ?? "?").Aggregate(17, (h, c) => h * 31 + c);
                    row.PictureBack.color = Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.45f, dead ? 0.3f : 0.55f);
                    GameUi.Show(row.Picture, picture, dead ? new Color(0.45f, 0.45f, 0.45f, 1f) : Color.white);
                }
                row.Initial.enabled = row.Picture.texture == null;

                for (int f = 0; f < row.Foods.Count; f++)
                {
                    Slot slot = row.Foods[f];
                    FoodSlot food = f < m.Foods.Count ? m.Foods[f] : null;
                    bool has = food != null && food.Icon != null;
                    slot.Icon.enabled = has;
                    if (has && slot.Icon.sprite != food.Icon) slot.Icon.sprite = food.Icon;
                    slot.Back.color = has ? new Color(0f, 0f, 0f, 0.55f) : new Color(0.45f, 0.4f, 0.3f, 0.25f);   // (empty: hungry)
                    slot.Time.SetActive(has);
                    if (has)
                    {
                        slot.Time.Set(food.Fraction);
                        slot.Time.SetColor(food.Fraction < 0.2f ? new Color(1f, 0.45f, 0.35f, 0.95f) : new Color(0.55f, 0.9f, 0.45f, 0.95f));
                    }
                }

                for (int e = 0; e < row.Effects.Count; e++)
                {
                    Slot slot = row.Effects[e];
                    Effect fx = e < m.Effects.Count && !(row.Companion && dead) ? m.Effects[e] : null;
                    bool has = fx != null && fx.Icon != null;
                    slot.Icon.enabled = has;
                    if (has && slot.Icon.sprite != fx.Icon) slot.Icon.sprite = fx.Icon;
                    bool timed = has && !row.Companion && fx.Fraction < 0.999f;
                    slot.Time.SetActive(timed);
                    if (timed) { slot.Time.Set(fx.Fraction); slot.Time.SetColor(new Color(0.95f, 0.8f, 0.4f, 0.95f)); }
                }
            }
        }

        private static bool IsDead(Member m) => m.Companion ? m.MaxHp <= 0f || m.Hp <= 0.5f : m.HasData && m.MaxHp > 0f && m.Hp <= 0.5f;

        /// <summary>Every frame: the smoothed bars, the fade-in, the pulse when health is low, the arrows.</summary>
        private void Animate(Row row, Member m)
        {
            bool dead = IsDead(m);
            float hpFrac = m.HasData && m.MaxHp > 0f ? Mathf.Clamp01(m.Hp / m.MaxHp) : 0f;
            float stFrac = m.HasData && m.MaxSt > 0f ? Mathf.Clamp01(m.St / m.MaxSt) : 0f;
            float eitrFrac = m.HasData && m.MaxEitr > 0f ? Mathf.Clamp01(m.Eitr / m.MaxEitr) : 0f;
            Anim a = Animate(m.Id, hpFrac, stFrac, eitrFrac);

            // fade the row in when someone appears, and dim it when we have no numbers for them
            float alpha = a.Alpha * (m.HasData ? 1f : 0.6f);
            if (Mathf.Abs(row.Group.alpha - alpha) > 0.004f) row.Group.alpha = alpha;

            bool low = m.HasData && !dead && hpFrac < 0.25f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            Color edge = row.Companion ? (dead ? new Color(0.4f, 0.4f, 0.4f, 0.8f) : new Color(CompanionColor.r, CompanionColor.g, CompanionColor.b, 0.85f))
                                       : m.Self ? new Color(GameUi.Gold.r, GameUi.Gold.g, GameUi.Gold.b, 0.85f) : Clear;
            if (low) edge = Color.Lerp(edge.a > 0f ? edge : new Color(1f, 0.25f, 0.2f, 0f), new Color(1f, 0.25f, 0.2f, 1f), 0.5f + 0.5f * pulse);
            row.Edge.SetColor(edge);

            // the picture's ring says how healthy they are (green, then amber, then red), so a glance is enough
            row.Ring?.SetColor(!m.HasData ? Clear : dead ? new Color(0.4f, 0.4f, 0.4f, 0.9f)
                : Color.Lerp(new Color(0.9f, 0.2f, 0.18f, 0.95f), new Color(0.35f, 0.85f, 0.4f, 0.95f), Mathf.Clamp01(a.Health * 1.15f)));

            Color hp = dead ? (row.Companion ? Grey : new Color(0.35f, 0.35f, 0.35f)) : HealthColor;
            if (low) hp = Color.Lerp(HealthColor, new Color(1f, 0.55f, 0.45f), pulse);
            row.Health.SetColor(hp);
            row.Health.Set(m.HasData ? a.Health : 0f, m.HasData ? a.Trail : 0f);
            if (row.Stamina != null) { row.Stamina.SetColor(dead ? Grey : StaminaColor); row.Stamina.Set(m.HasData ? a.Stamina : 0f); }
            row.Eitr?.Set(a.Eitr);

            if (row.ArrowImage != null)
            {
                float? bearing = m.Self || m.Distance < 0f ? null : BearingTo(m);
                if (row.ArrowImage.enabled != bearing.HasValue) row.ArrowImage.enabled = bearing.HasValue;
                if (bearing.HasValue)
                {
                    Quaternion turn = Quaternion.Euler(0f, 0f, -bearing.Value);
                    if (Quaternion.Angle(row.Arrow.localRotation, turn) > 0.5f) row.Arrow.localRotation = turn;
                }
            }
        }

        /// <summary>Smooth the bars toward their real value; the trail lingers behind a health drop, then catches up.</summary>
        private Anim Animate(long id, float hp, float stamina, float eitr)
        {
            float now = Time.unscaledTime;
            if (!_anim.TryGetValue(id, out Anim a))
                _anim[id] = a = new Anim { Health = hp, Trail = hp, Stamina = stamina, Eitr = eitr, Alpha = 0f, LastTime = now };

            float dt = Mathf.Clamp(now - a.LastTime, 0f, 0.1f);
            a.LastTime = now;
            if (dt <= 0f) return a; // already updated this frame

            float k = 1f - Mathf.Exp(-14f * dt); // fast ease: bars move smoothly but never lag noticeably
            a.Health = Mathf.Lerp(a.Health, hp, k);
            a.Stamina = Mathf.Lerp(a.Stamina, stamina, k);
            a.Eitr = Mathf.Lerp(a.Eitr, eitr, k);
            a.Alpha = Mathf.MoveTowards(a.Alpha, 1f, dt * 4f); // ~0.25s fade-in

            if (a.Trail < a.Health) a.Trail = a.Health;                          // healed: the trail jumps with the bar
            else a.Trail = Mathf.MoveTowards(a.Trail, a.Health, dt * 0.35f);       // damaged: it drains slowly
            return a;
        }

        // ---- where it goes -----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Is a screen open that would sit under the panel (inventory, crafting, build menu, trader, big map)? The panel is on top of the
        /// game's HUD, so we simply get out of the way.
        /// </summary>
        private static bool ScreenCoversPanel() =>
            InventoryGui.IsVisible() || StoreGui.IsVisible() || Minimap.IsOpen() || Hud.IsPieceSelectionVisible() || TextInput.IsVisible();

        /// <summary>The panel's place on the HUD: from the chosen edge, below the ship's display while you steer.</summary>
        private void Place()
        {
            if (_panel == null || !(_panel.parent is RectTransform parent)) return;
            bool left = _onLeft.Value, bottom = _anchorBottom.Value;
            float offsetY = _offsetY.Value;
            LayoutGeometry.Placement Resolve() => LayoutGeometry.Resolve(parent.rect.width, parent.rect.height,
                _panel.sizeDelta.x, _panel.sizeDelta.y, _scale.Value, _offsetX.Value, offsetY, left, bottom,
                _fitToScreen.Value, _keepOnScreen.Value);
            LayoutGeometry.Placement placement = Resolve();

            if (_avoidShipHud.Value && ShipHudBounds(out Rect ship))
            {
                Canvas canvas = parent.GetComponentInParent<Canvas>();
                Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, ship.min, cam, out Vector2 lo) &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, ship.max, cam, out Vector2 hi))
                {
                    Rect area = parent.rect;
                    float width = _panel.sizeDelta.x * placement.Scale, height = _panel.sizeDelta.y * placement.Scale;
                    float x0 = left ? area.xMin + placement.X : area.xMax + placement.X - width;
                    float y0 = bottom ? area.yMin + placement.Y : area.yMax + placement.Y - height;
                    if (hi.x > x0 && lo.x < x0 + width && hi.y > y0 && lo.y < y0 + height)
                    {
                        float gap = bottom ? hi.y - area.yMin : area.yMax - lo.y;
                        offsetY = Mathf.Max(offsetY, (gap + 10f * placement.Scale) / _scale.Value);
                        placement = Resolve();
                    }
                }
            }
            _panel.localScale = Vector3.one * placement.Scale;
            var pos = new Vector2(placement.X, placement.Y);
            if ((_panel.anchoredPosition - pos).sqrMagnitude > 0.25f) _panel.anchoredPosition = pos;
        }

        /// <summary>
        /// While you steer a ship the game shows its own display (wind direction, sail and rudder). Find where it is on the screen, in screen
        /// pixels (origin bottom left). False when you are not steering.
        /// </summary>
        private static bool ShipHudBounds(out Rect bounds)
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
            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        /// <summary>The way someone is, as an angle from where the camera looks (0 = ahead, 90 = to your right), or null if we do not know where they are.</summary>
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
    }
}
