using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace BuildOrders
{
    /// <summary>
    /// The on-screen help, part of the game's HUD and built from its own interface (GameUi.cs): a materials panel (what all the orders need,
    /// against what you have), banners while planning, placing a blueprint, levelling or drawing a bridge, and a card on the order you're
    /// aiming at (on foot: what it costs, E to build it, hold E for everything nearby). Each piece is built again only when what it shows
    /// changes; under the HUD it hides with it (the hide-HUD key, the loading screen).
    /// </summary>
    public partial class Plugin
    {
        private class Need { public string Item, Name; public int Amount; public ItemDrop.ItemData Data; }

        private List<Need> _needs = new List<Need>();
        private float _nextNeeds;

        /// <summary>Total up the materials every order still needs.</summary>
        private void RefreshNeeds()
        {
            if (Time.unscaledTime < _nextNeeds) return;
            _nextNeeds = Time.unscaledTime + 0.5f;

            var totals = new Dictionary<string, Need>();
            foreach (Order o in _orders.Values)
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(o.Prefab) : null;
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece == null) continue;

                foreach (Piece.Requirement req in piece.m_resources)
                {
                    if (req.m_resItem == null || req.m_amount <= 0) continue;
                    string item = req.m_resItem.m_itemData.m_shared.m_name;
                    if (!totals.TryGetValue(item, out Need need))
                        totals[item] = need = new Need { Item = item, Name = Localization.instance.Localize(item), Data = req.m_resItem.m_itemData };
                    need.Amount += req.m_amount;
                }
            }
            _needs = totals.Values.OrderByDescending(n => n.Amount).ToList();
        }

        // ---- the pieces on the HUD ----------------------------------------------------------------------------------------------------

        private const string HudPrefix = "BuildOrders.";
        private class HudPiece { public RectTransform Root; public string Shown; }
        private readonly Dictionary<string, HudPiece> _hudPieces = new Dictionary<string, HudPiece>();
        private float _hudAt;
        private RectTransform _holdFill;   // the hold-E bar's fill, moved every frame (the rest of the prompt only when it changes)
        private float _holdWidth;

        /// <summary>A line on a banner or card: its text, size and colour.</summary>
        private struct Line
        {
            public string Text; public float Size; public Color Color;
            public Line(string text, float size, Color color) { Text = text; Size = size; Color = color; }
        }

        /// <summary>Every frame (from Update): which pieces should be on the HUD, each built again only when what it shows changed.</summary>
        private void TickHud()
        {
            if (_holdFill != null) _holdFill.offsetMax = new Vector2(2f + (_holdWidth - 4f) * Mathf.Clamp01(HoldProgress), -2f);
            if (Time.unscaledTime < _hudAt) return;
            _hudAt = Time.unscaledTime + 0.1f;

            var wanted = new HashSet<string>();
            Player player = Player.m_localPlayer;
            if (_enabled.Value && player != null && GameUi.HudRoot != null && !Menu.IsVisible() && GameUi.Ready())
            {
                // banners, top middle, one under the other
                float top = 70f;
                if (!PlansWindowOpen)
                {
                    if (_placing != null) top = ShowBanner(wanted, "placing", top, PlacementBannerLines());
                    else if (ActiveLevelJob != null) top = ShowBanner(wanted, "level", top, LevelBannerLines());
                    if (_bridgeStart.HasValue && !BridgeOptionsOpen) top = ShowBanner(wanted, "bridge", top, BridgeBannerLines());
                }

                bool placing = player.InPlaceMode();
                if (!PlansWindowOpen && !BridgeOptionsOpen && !InventoryGui.IsVisible() && (placing || _alwaysShowPanel.Value || Aimed != null))
                {
                    if (PlanKeyHeld && placing)
                        top = ShowBanner(wanted, "plan", top, new[] { new Line(T("buildorders_banner_plan") + (_planToggle.Value ? "   " + T("buildorders_banner_plan_leave", _planKey.Value) : ""), 18f, GameUi.Warm) });
                    if (_orders.Count > 0 && (placing || _alwaysShowPanel.Value)) { RefreshNeeds(); ShowNeeds(wanted, player); }
                    if (Aimed != null)
                    {
                        if (placing) ShowAimCard(wanted);
                        else ShowBuildPrompt(wanted);
                    }
                    // Not while a radial menu covers the middle of the screen (the game's, or another mod's that hides the build panel).
                    if (placing && FetchAvailable && BuildPanelShowing) ShowFetchHint(wanted);
                }
            }
            foreach (string name in _hudPieces.Keys.Where(k => !wanted.Contains(k)).ToList()) HideHud(name);
        }

        private void ShowHud(HashSet<string> wanted, string name, string signature, Func<Transform, RectTransform> build)
        {
            wanted.Add(name);
            if (_hudPieces.TryGetValue(name, out HudPiece piece) && piece.Root != null && piece.Shown == signature) return;
            HideHud(name);
            Transform hud = GameUi.HudRoot;
            // a hot reload leaves the old copy's pieces behind (its types differ from ours): find them by name
            foreach (Transform old in hud.Cast<Transform>().Where(t => t.name == HudPrefix + name).ToList()) Destroy(old.gameObject);
            RectTransform r = build(hud);
            r.name = HudPrefix + name;
            _hudPieces[name] = new HudPiece { Root = r, Shown = signature };
        }

        private void HideHud(string name)
        {
            if (_hudPieces.TryGetValue(name, out HudPiece piece) && piece.Root != null) Destroy(piece.Root.gameObject);
            _hudPieces.Remove(name);
            if (name == "prompt") _holdFill = null;
        }

        private void HideAllHud()
        {
            foreach (string name in _hudPieces.Keys.ToList()) HideHud(name);
            Transform hud = GameUi.HudRoot;
            if (hud != null) foreach (Transform old in hud.Cast<Transform>().Where(t => t.name.StartsWith(HudPrefix)).ToList()) Destroy(old.gameObject);
        }

        /// <summary>A sunken box with lines of text in the middle, as wide as its longest line (pos: its top middle, from the anchor).</summary>
        private static RectTransform TextCard(Transform parent, Vector2 anchor, Vector2 pos, IList<Line> lines)
        {
            RectTransform box = GameUi.Box(parent, Vector2.zero, new Vector2(100f, 100f));
            box.anchorMin = box.anchorMax = anchor;
            box.pivot = new Vector2(0.5f, 1f);
            box.anchoredPosition = pos;
            float y = 10f, widest = 200f;
            var made = new List<TMP_Text>();
            foreach (Line line in lines)
            {
                TMP_Text t = GameUi.Line(box, line.Text, new Vector2(0f, -y), 1100f, line.Size, line.Color, null, TextAlignmentOptions.Top, new Vector2(0.5f, 1f));
                widest = Mathf.Max(widest, GameUi.Width(t, line.Text));
                made.Add(t);
                y += line.Size * 1.35f + 2f;
            }
            float w = Mathf.Min(1100f, widest + 48f);
            foreach (TMP_Text t in made) ((RectTransform)t.transform).sizeDelta = new Vector2(w - 24f, ((RectTransform)t.transform).sizeDelta.y);
            box.sizeDelta = new Vector2(w, y + 8f);
            return box;
        }

        /// <summary>A banner at the top of the screen; returns where the next one goes.</summary>
        private float ShowBanner(HashSet<string> wanted, string name, float top, IList<Line> lines)
        {
            string signature = top + "|" + string.Join("|", lines.Select(l => l.Text).ToArray());
            ShowHud(wanted, name, signature, hud => TextCard(hud, new Vector2(0.5f, 1f), new Vector2(0f, -top), lines));
            return top + 10f + lines.Sum(l => l.Size * 1.35f + 2f) + 8f + 6f;
        }

        /// <summary>The game's way of showing a key: [<color=yellow><b>E</b></color>].</summary>
        private static string Key(string key) => $"[<color=yellow><b>{key}</b></color>]";

        // ---- what all the orders need ----

        private void ShowNeeds(HashSet<string> wanted, Player player)
        {
            Inventory inv = player.GetInventory();
            int shown = Mathf.Min(_needs.Count, 10);
            List<Need> list = _needs.Take(shown).ToList();
            int[] have = list.Select(n => inv.CountItems(n.Item)).ToArray();   // includes nearby chests when BuildFromChests is installed
            string signature = $"{_panelX.Value},{_panelY.Value}|{_orders.Count}|{_needs.Count}|{PlanHint}|" +
                               string.Join("|", list.Select((n, i) => n.Item + "," + n.Amount + "," + have[i]).ToArray());
            ShowHud(wanted, "needs", signature, hud =>
            {
                const float w = 280f, pad = 12f, row = 26f;
                float h = pad + 30f + shown * row + (_needs.Count > shown ? 22f : 0f) + 22f + pad;
                RectTransform box = GameUi.Box(hud, new Vector2(_panelX.Value, -_panelY.Value), new Vector2(w, h));
                GameUi.Line(box, T("buildorders_needs_title"), new Vector2(pad, -pad + 2f), 160f, 20f, GameUi.Warm, GameUi.Norse);
                GameUi.Line(box, Pieces(_orders.Count), new Vector2(-pad, -pad + 2f), 110f, 15f, GameUi.Dim, null, TextAlignmentOptions.TopRight, new Vector2(1f, 1f));
                float y = pad + 30f;
                for (int i = 0; i < list.Count; i++)
                {
                    Need n = list[i];
                    if (n.Data != null) GameUi.Icon(box, new Vector2(pad, -y), new Vector2(22f, 22f), n.Data.GetIcon());
                    GameUi.Line(box, n.Name, new Vector2(pad + 28f, -y), w - pad * 2f - 28f - 84f, 16f, GameUi.Text);
                    GameUi.Line(box, $"{have[i]} / {n.Amount}", new Vector2(-pad, -y), 84f, 16f, have[i] >= n.Amount ? GameUi.Good : GameUi.Warn, null, TextAlignmentOptions.TopRight, new Vector2(1f, 1f));
                    y += row;
                }
                if (_needs.Count > shown) { GameUi.Line(box, T("buildorders_needs_more", _needs.Count - shown), new Vector2(pad, -y), w - pad * 2f, 15f, GameUi.Dim); y += 22f; }
                GameUi.Line(box, PlanHint, new Vector2(pad, -y - 2f), w - pad * 2f, 15f, GameUi.Dim);
                return box;
            });
        }

        // ---- aiming at an order with the hammer out ----

        private void ShowAimCard(HashSet<string> wanted)
        {
            Order o = Aimed;
            var lines = new[]
            {
                new Line($"{PieceName(o.Prefab)}   <color=#bdb7a9>{PlannedBy(o.By)}</color>", 19f, GameUi.Warm),
                new Line($"{Key(_selectKey.Value.ToString())} {T("buildorders_aim_select")}    {Key(_removeKey.Value.ToString())} {T("buildorders_aim_remove")}    {T("buildorders_aim_shift")}", 16f, GameUi.Text),
            };
            ShowHud(wanted, "aim", o.Id + "|" + lines[0].Text + "|" + lines[1].Text, hud => TextCard(hud, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), lines));
        }

        // ---- on foot, aiming at a ghost: what it costs, and E to build it ----

        private string HoldText => HoldCount >= HoldAll ? T("buildorders_hold_all", HoldAll, _buildAllRadius.Value.ToString("0"))
                                 : HoldCount > 0 ? T("buildorders_hold_some", HoldCount, HoldAll)
                                 : T("buildorders_hold_none");

        /// <summary>"planned by Bob" under a ghost's name (the order's "By": a blueprint's or a player's name).</summary>
        private static string PlannedBy(string by) => (by ?? "").StartsWith(BlueprintPrefix) ? T("buildorders_aim_planned_by_blueprint", ShownTitle(by.Substring(BlueprintPrefix.Length)))
                                                                                     : T("buildorders_aim_planned_by", Who(by));

        /// <summary>"1 piece", "5 pieces".</summary>
        internal static string Pieces(int n) => T(n == 1 ? "buildorders_piece_one" : "buildorders_pieces", n);

        /// <summary>
        /// The piece's picture and name, the inventory's own cells for its materials (what you have of what it needs, orange when short, the
        /// game's tooltip), the whole plan's materials, how well it would stand, E to build it, and the hold-E bar.
        /// </summary>
        private void ShowBuildPrompt(HashSet<string> wanted)
        {
            Order o = Aimed;
            int pn = PlanLeft > 1 ? PlanLines.Count : 0;   // the whole plan's materials (not worth showing for a lone ghost)
            Stab stab = StabilityOf(o.Id);
            bool holding = HoldProgress > 0f;
            var sig = new System.Text.StringBuilder();
            sig.Append(o.Id).Append('|').Append(o.By).Append('|').Append(HintKnown).Append(HintAffordable).Append(holding).Append('|').Append(HoldText).Append('|');
            foreach (HintLine l in HintLines) sig.Append(l.Name).Append(',').Append(l.Have).Append(',').Append(l.Need).Append(';');
            if (pn > 0) { sig.Append('|').Append(PlanTitle).Append(PlanLeft); foreach (HintLine l in PlanLines) sig.Append(l.Name).Append(',').Append(l.Have).Append(',').Append(l.Need).Append(';'); }
            if (stab != null) sig.Append('|').Append(stab.Percent).Append(stab.Collapses).Append(stab.Support >= stab.Max);
            ShowHud(wanted, "prompt", sig.ToString(), hud => BuildPrompt(hud, o, pn, stab, holding));
        }

        private RectTransform BuildPrompt(Transform hud, Order o, int pn, Stab stab, bool holding)
        {
            const float pad = 16f;
            float cs = GameUi.CellSpace;
            int n = HintLines.Count;
            float holdText = HoldText.Sum(c => c >= 0x2E80 ? 14f : 7.2f);   // about how wide the hold-E line is at 14 pt (wide CJK letters count double)
            float w = Mathf.Max(440f, n * cs + pad * 2f, pn * 96f + pad * 2f, Mathf.Min(1000f, holdText + pad * 2f + 16f));
            RectTransform box = GameUi.Box(hud, Vector2.zero, new Vector2(w, 100f));
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 1f);
            box.anchoredPosition = new Vector2(0f, -80f);

            // the piece
            GameUi.Icon(box, new Vector2(pad, -pad + 2f), new Vector2(46f, 46f), HintPieceIcon);
            GameUi.Line(box, PieceName(o.Prefab), new Vector2(pad + 56f, -pad), w - pad * 2f - 56f, 22f, GameUi.Warm);
            GameUi.Line(box, PlannedBy(o.By), new Vector2(pad + 56f, -pad - 28f), w - pad * 2f - 56f, 16f, GameUi.Dim);
            float y = pad + 58f;

            // its materials, in the inventory's own cells
            if (n > 0)
            {
                float x0 = (w - n * cs) / 2f + (cs - 64f) / 2f;
                for (int i = 0; i < n; i++)
                {
                    HintLine line = HintLines[i];
                    bool enough = line.Have >= line.Need;
                    if (line.Item != null)
                    {
                        InventoryElement cell = GameUi.Cell(box, new Vector2(x0 + i * cs, -y), line.Item, $"{line.Have}/{line.Need}", false, null);
                        if (!enough) cell.m_amount.color = GameUi.Warn;
                    }
                    GameUi.Line(box, line.Name, new Vector2(x0 + i * cs - (cs - 64f) / 2f, -y - 66f), cs, 13f, GameUi.Dim, null, TextAlignmentOptions.Top);
                }
                y += 64f + 26f;
            }

            // the whole plan: what it still needs altogether
            if (pn > 0)
            {
                GameUi.Line(box, T("buildorders_whole_plan", PlanTitle, PlanLeft), new Vector2(pad, -y), w - pad * 2f, 16f, GameUi.Text, null, TextAlignmentOptions.Top);
                y += 26f;
                RectTransform row = GameUi.Rect(box, "Plan", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -y), new Vector2(w, 28f));
                float x = 0f;
                foreach (HintLine line in PlanLines)
                    x += line.Item != null ? GameUi.Chip(row, new Vector2(x, 0f), line.Item, $"{line.Have}/{line.Need}", 26f, 15f, line.Have >= line.Need ? GameUi.Good : GameUi.Warn) : 0f;
                row.anchoredPosition = new Vector2((w - (x - 12f)) / 2f, -y);
                y += 36f;
            }

            // how well it would be supported (an estimate; the game does the real thing once it is built)
            if (stab != null)
            {
                string text = stab.Support >= stab.Max ? T("buildorders_stab_solid")
                            : stab.Collapses ? T("buildorders_stab_falls")
                            : T("buildorders_stab_support", stab.Percent);
                GameUi.Line(box, text, new Vector2(pad, -y), w - pad * 2f, 16f, stab.Collapses ? GameUi.Warn : StabilityColor(stab), null, TextAlignmentOptions.Top);
                y += 26f;
            }

            // what E does
            string what = HintKnown && HintAffordable ? Localization.instance.Localize(Key("$KEY_Use")) + " " + T("buildorders_build_it")
                        : HintKnown ? T("buildorders_missing") : T("buildorders_not_unlocked_this");
            GameUi.Line(box, what, new Vector2(pad, -y), w - pad * 2f, 20f, HintKnown && HintAffordable ? GameUi.Text : GameUi.Warn, null, TextAlignmentOptions.Top);
            y += 32f;

            // hold E for everything nearby (its fill follows the key every frame: see TickHud)
            _holdWidth = w - pad * 2f;
            RectTransform groove = GameUi.Bar(box, new Vector2(pad, -y), new Vector2(_holdWidth, 22f), 0f, GameUi.Gold,
                                              holding ? T("buildorders_keep_holding") : HoldText, 14f);
            _holdFill = groove.Find("Fill") as RectTransform;
            y += 22f + pad;

            box.sizeDelta = new Vector2(w, y);
            return box;
        }

        // ---- with the hammer out: fetch a piece's materials from the chests ----

        /// <summary>
        /// "[Y] Fetch what one Wood wall needs from the chests" (or "another Wood wall's worth"), with a cell for each material a press takes
        /// (what you have in it, and how many a press brings under it).
        /// </summary>
        private static bool BuildPanelShowing => !Hud.InRadial() && (Hud.instance == null || Hud.instance.m_buildHud == null || Hud.instance.m_buildHud.activeSelf);

        private void ShowFetchHint(HashSet<string> wanted)
        {
            List<FetchLine> lines = FetchLines.Where(l => l.Short).ToList();
            if (lines.Count == 0 || !FetchTopUp) return; // (holding enough already: no panel in the way; the key still brings another piece's worth)
            string head = $"{Key(_fetchKey.Value.ToString())} " + T(FetchTopUp ? "buildorders_fetch_topup" : "buildorders_fetch_another", FetchPieceName);
            string signature = head + "|" + string.Join("|", lines.Select(l => l.Item + "," + l.InInventory + "," + l.Take).ToArray());
            ShowHud(wanted, "fetch", signature, hud =>
            {
                const float pad = 14f;
                float cs = GameUi.CellSpace;
                RectTransform box = GameUi.Box(hud, Vector2.zero, new Vector2(100f, 100f));
                box.anchorMin = box.anchorMax = new Vector2(0.5f, 0f);
                box.pivot = new Vector2(0.5f, 0f);
                box.anchoredPosition = new Vector2(0f, 250f);
                TMP_Text title = GameUi.Line(box, head, new Vector2(0f, -pad), 1000f, 18f, GameUi.Text, null, TextAlignmentOptions.Top, new Vector2(0.5f, 1f));
                float w = Mathf.Max(lines.Count * cs + pad * 2f, GameUi.Width(title, head) + 48f);
                ((RectTransform)title.transform).sizeDelta = new Vector2(w - 24f, 26f);
                float x0 = (w - lines.Count * cs) / 2f + (cs - 64f) / 2f, y = pad + 32f;
                for (int i = 0; i < lines.Count; i++)
                {
                    FetchLine line = lines[i];
                    ItemDrop.ItemData item = line.Prefab != null ? line.Prefab.GetComponent<ItemDrop>()?.m_itemData : null;
                    if (item != null)
                    {
                        InventoryElement cell = GameUi.Cell(box, new Vector2(x0 + i * cs, -y), item, line.InInventory.ToString(), false, null);
                        cell.m_amount.color = GameUi.Warn;
                    }
                    GameUi.Line(box, $"+{line.Take}", new Vector2(x0 + i * cs - (cs - 64f) / 2f, -y - 66f), cs, 15f, GameUi.Good, null, TextAlignmentOptions.Top);
                }
                box.sizeDelta = new Vector2(w, y + 64f + 26f + pad);
                return box;
            });
        }
    }
}
