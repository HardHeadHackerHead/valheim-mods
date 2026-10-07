using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// Quick stack: two buttons under your inventory that move your items into nearby chests, plus a lock (hover an item and press
    /// the lock key) to keep things like food out of it. The buttons only appear when a usable chest is in range.
    /// </summary>
    public partial class Plugin
    {
        private const string KeepTag = "DHack.QuickStack.Keep";

        private ConfigEntry<bool> _stackEnabled, _protectHotbar, _showStackButtons, _showChestLabels, _playSounds;
        private ConfigEntry<float> _stackRadius;
        private ConfigEntry<KeyCode> _lockKey, _stackKey, _assignKey;

        private readonly List<Container> _stackChests = new List<Container>();
        private float _nextChestScan;
        private Action _pending; // clicks run from Update, not mid-draw, so the layout never changes under IMGUI

        private static readonly System.Reflection.MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");

        private void BindQuickStackConfig()
        {
            _stackEnabled = Config.Bind("QuickStack", "Enabled", true, "Turn the quick-stack buttons and item locking on or off.");
            _stackRadius = Config.Bind("QuickStack", "Radius", 20f, "How far (in metres) from you a chest can be and still be used.");
            _lockKey = Config.Bind("QuickStack", "LockKey", KeyCode.L,
                "Inventory open: hover an item and press this to lock it (it will never be moved by the buttons) or unlock it.");
            _stackKey = Config.Bind("QuickStack", "StackKey", KeyCode.None,
                "Optional key that does 'Stack to chests' while your inventory is open (useful with a controller). None = off.");
            _protectHotbar = Config.Bind("QuickStack", "ProtectHotbar", true, "Never move items in your top inventory row (the hotbar).");
            _showStackButtons = Config.Bind("QuickStack", "ShowButtons", true, "Show the buttons under your inventory when a chest is in range.");
            _playSounds = Config.Bind("QuickStack", "PlaySounds", true,
                "Play the chests' own sounds when you stack (a thunk from each chest that takes items) and when you undo.");
            _showChestLabels = Config.Bind("QuickStack", "ShowChestLabels", true,
                "Show what a chest is assigned to receive: under its Assign button when it's open, and in the hover text when you look at it.");
            _assignKey = Config.Bind("QuickStack", "AssignKey", KeyCode.K,
                "Look at a chest (or have one open) and press this to choose what it should receive when you stack: whole categories or individual items.");
        }

        // ---- input and chest scan (called from Update) --------------------------------------------

        private void UpdateQuickStack(Player player)
        {
            if (_pending != null) { Action a = _pending; _pending = null; a(); }
            if (!_stackEnabled.Value) return;

            // The assign key works with the inventory open (on an open chest) or closed (on the chest you're looking at).
            if (!TypingOrMenuOpen() && !RulesWindowOpen && Input.GetKeyDown(_assignKey.Value)) HandleAssignKey(player);

            if (!InventoryGui.IsVisible())
            {
                _stackChests.Clear();
                _undo = null; // close the inventory and the chance to undo is gone
                return;
            }

            if (Time.time >= _nextChestScan)
            {
                _nextChestScan = Time.time + 0.5f;
                ScanChests(player);
            }

            if (TypingOrMenuOpen()) return;

            if (Input.GetKeyDown(_lockKey.Value)) ToggleKeep(player);
            if (_stackKey.Value != KeyCode.None && Input.GetKeyDown(_stackKey.Value)) StackToChests(player);
        }

        private static bool IsKept(ItemDrop.ItemData item) => item.m_customData != null && item.m_customData.ContainsKey(KeepTag);

        private void ToggleKeep(Player player)
        {
            ItemDrop.ItemData item = HoveredItem(player);
            if (item == null) return;

            if (IsKept(item))
            {
                item.m_customData.Remove(KeepTag);
                Tell(player, $"Unlocked: {NameOf(item)}");
            }
            else
            {
                item.m_customData[KeepTag] = "1";
                Tell(player, $"Locked: {NameOf(item)} (won't be stacked into chests)");
            }
        }

        /// <summary>Can this player use this chest (not in use by someone else, not warded off, not someone's private chest)?</summary>
        private static bool Usable(Container c)
        {
            if (c == null || c.GetInventory() == null) return false;
            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) return false;
            return (bool)CheckAccess.Invoke(c, new object[] { playerId });
        }

        /// <summary>Chests near you that you're allowed to use, nearest first, and what each one has been assigned.</summary>
        private void ScanChests(Player player)
        {
            _stackChests.Clear();
            _chestRules.Clear();
            Vector3 here = player.transform.position;
            float max = _stackRadius.Value * _stackRadius.Value;

            foreach (Container c in ContainerRegistry.Alive())
            {
                if (c == null || ContainerRegistry.InUse(c)) continue; // (a chest you have open is handled by the game's own button)
                if (c.GetInventory() == null || (c.transform.position - here).sqrMagnitude > max) continue;
                if (!Usable(c)) continue;
                _stackChests.Add(c);
            }
            _stackChests.Sort((a, b) =>
                (a.transform.position - here).sqrMagnitude.CompareTo((b.transform.position - here).sqrMagnitude));

            foreach (Container c in _stackChests) _chestRules[c] = ChestRules.Read(c);
        }

        private readonly Dictionary<Container, ChestRules> _chestRules = new Dictionary<Container, ChestRules>();

        // ---- the stacking itself ------------------------------------------------------------------

        private bool IsProtected(Player player, ItemDrop.ItemData item)
        {
            if (player.IsItemEquiped(item)) return true;                       // what you're wearing or holding
            if (IsKept(item)) return true;                                     // locked by you
            if (IsQuick(item)) return true;                                    // part of your quick set
            if (item.m_shared.m_questItem) return true;
            if (AppDomain.CurrentDomain.GetData("DHack.GearSlots.IsGear") is Func<ItemDrop.ItemData, bool> inGearSlot && inGearSlot(item)) return true; // GearSlots mod: worn gear and food slots stay put
            if (_protectHotbar.Value && item.m_gridPos.y == 0) return true;    // top row
            return false;
        }

        /// <summary>
        /// Move your unlocked items into nearby chests. For each item, in order of preference: a chest assigned that exact item,
        /// then a chest assigned its category, then a chest that already holds that item. Nearest chest first within each step.
        /// </summary>
        private void StackToChests(Player player)
        {
            // Leave out any chest someone opened since the last scan, and bring the rest up to date before looking inside them.
            List<Container> chests = _stackChests.Where(c => c != null && !ContainerRegistry.InUse(c)).ToList();
            if (chests.Count == 0) { Tell(player, "No chest in range."); return; }
            foreach (Container c in chests) ContainerRegistry.Reload(c);

            Inventory inventory = player.GetInventory();
            int moved = 0;
            var used = new HashSet<Container>();
            var record = new List<Moved>(); // for Undo

            // A safety check: count every kind of item we are about to move (yours plus the chests') now and again afterwards, so that
            // if anything ever goes missing it is noticed, reported, and written to the log.
            int Total(string itemName) => inventory.CountItems(itemName) + chests.Sum(c => c.GetInventory().CountItems(itemName));
            var totalsBefore = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (!IsProtected(player, item) && !totalsBefore.ContainsKey(item.m_shared.m_name)) totalsBefore[item.m_shared.m_name] = Total(item.m_shared.m_name);

            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                if (IsProtected(player, item)) continue;

                int before = item.m_stack;
                string name = item.m_shared.m_name;
                string category = Categories.Of(item.m_shared);

                // The three steps, strongest first. A chest you assigned wins even if it's empty.
                var steps = new Func<Container, bool>[]
                {
                    c => _chestRules.TryGetValue(c, out ChestRules r) && r.WantsItem(name),
                    c => _chestRules.TryGetValue(c, out ChestRules r) && r.WantsCategory(category),
                    c => c.GetInventory().ContainsItemByName(name),
                };

                foreach (Func<Container, bool> wants in steps)
                {
                    foreach (Container c in chests)
                    {
                        if (!inventory.ContainsItem(item)) break;
                        if (!wants(c)) continue;

                        // Remember exactly what we're about to move, for Undo (the stack may merge into the chest's and lose its identity).
                        Vector2i slot = item.m_gridPos;
                        ItemDrop.ItemData snapshot = item.Clone();
                        int n = Deposit(c, inventory, item);
                        if (n <= 0) continue;

                        snapshot.m_stack = n;
                        record.Add(new Moved { Chest = c, Item = snapshot, Slot = slot });
                        used.Add(c);
                    }
                    if (!inventory.ContainsItem(item)) break;
                }

                moved += before - (inventory.ContainsItem(item) ? item.m_stack : 0);
            }

            // Undo exists only if this stack really moved something; a new stack replaces the previous Undo.
            _undo = record.Count > 0 ? record : null;

            // Where did it all go? (also in the log), and did anything go missing?
            Vector3 here = player.transform.position;
            var where = new List<string>();
            foreach (var group in record.GroupBy(r => r.Item.m_shared.m_name))
            {
                string display = Localization.instance.Localize(group.Key);
                string chestsText = string.Join(", ", group.Select(r => $"{r.Item.m_stack} in a chest {Vector3.Distance(here, r.Chest.transform.position):0} m away").ToArray());
                where.Add($"{display}: {chestsText}");
            }
            if (where.Count > 0) Logger.LogInfo("Stacked: " + string.Join("; ", where.ToArray()));

            int missing = 0;
            foreach (var kv in totalsBefore)
            {
                int now = Total(kv.Key);
                if (now >= kv.Value) continue;
                missing += kv.Value - now;
                Logger.LogError($"STACK LOST ITEMS: {Localization.instance.Localize(kv.Key)} had {kv.Value} (inventory + nearby chests) before and {now} after");
            }

            string summary = moved > 0
                ? $"Stacked {moved} item(s) into {used.Count} chest(s)" + (record.Count > 0 ? $" ({record.Select(r => Vector3.Distance(here, r.Chest.transform.position)).Min():0} m away)" : "")
                : "Nothing to stack: no nearby chest is assigned, or holds, any of your unlocked items.";
            if (missing > 0) summary += $"   WARNING: {missing} item(s) went missing, see the log";
            Tell(player, summary);

            if (moved > 0) PlayChestSounds(used, opening: false);
        }

        /// <summary>
        /// The game's own chest sounds: each chest that took something plays its "close" thunk from where it stands (a few at most, a
        /// moment apart so they don't smear together). Undo plays the "open" sound instead.
        /// </summary>
        private void PlayChestSounds(IEnumerable<Container> chests, bool opening)
        {
            if (!_playSounds.Value) return;
            Vector3 here = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            List<Container> nearestFirst = chests.Where(c => c != null)
                .OrderBy(c => (c.transform.position - here).sqrMagnitude).Take(4).ToList();
            if (nearestFirst.Count > 0) StartCoroutine(PlayChestSoundsRoutine(nearestFirst, opening));
        }

        private IEnumerator PlayChestSoundsRoutine(List<Container> chests, bool opening)
        {
            foreach (Container c in chests)
            {
                if (c == null) continue;
                try
                {
                    EffectList effects = opening ? c.m_openEffects : c.m_closeEffects;
                    effects?.Create(c.transform.position, c.transform.rotation);
                }
                catch (Exception) { /* a missing sound must never break stacking */ }
                yield return new WaitForSeconds(0.12f);
            }
        }

        /// <summary>Move (as much as fits of) one stack into a chest. Returns how many items actually moved.</summary>
        private static int Deposit(Container chest, Inventory from, ItemDrop.ItemData item)
        {
            Inventory into = chest.GetInventory();
            int before = item.m_stack;

            ContainerRegistry.TakeOwnership(chest); // only the owner can save a chest's contents (matters in multiplayer)

            if (into.AddItem(item))
            {
                from.RemoveItem(item); // the whole stack fitted
                return before;
            }
            return before - item.m_stack; // part of it fitted: what's left stays in your inventory
        }

        /// <summary>
        /// For other mods (AICompanion, through AppDomain "DHack.QoL.StackInventory"): put the items of an inventory that <paramref name="keep"/>
        /// does not protect into the chests within <paramref name="radius"/> of <paramref name="here"/>, by the same rules as Stack to chests
        /// (a chest assigned that item, then its category, then a chest already holding it; nearest first). Only chests: never a cart's,
        /// a ship's, a tombstone or a companion's bag, never one someone has open or a ward keeps you out of. The same lost-item check as
        /// your own stacking. Returns how many items moved.
        /// </summary>
        internal static int StackInventory(Inventory inventory, Vector3 here, float radius, Func<ItemDrop.ItemData, bool> keep)
        {
            if (inventory == null || Game.instance == null) return 0;
            float max = radius * radius;
            List<Container> chests = ContainerRegistry.Alive()
                .Where(c => c != null && c.GetInventory() != null && c.GetInventory() != inventory && !ContainerRegistry.InUse(c)
                            && (c.transform.position - here).sqrMagnitude <= max && c.GetComponentInParent<Piece>() != null
                            && c.GetComponentInParent<Vagon>() == null && c.GetComponentInParent<Ship>() == null && c.GetComponent<TombStone>() == null && Usable(c))
                .OrderBy(c => (c.transform.position - here).sqrMagnitude).ToList();
            if (chests.Count == 0) return 0;
            foreach (Container c in chests) ContainerRegistry.Reload(c);
            var rules = chests.ToDictionary(c => c, c => ChestRules.Read(c));

            int Total(string itemName) => inventory.CountItems(itemName) + chests.Sum(c => c.GetInventory().CountItems(itemName));
            var totalsBefore = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if ((keep == null || !keep(item)) && !totalsBefore.ContainsKey(item.m_shared.m_name)) totalsBefore[item.m_shared.m_name] = Total(item.m_shared.m_name);

            int moved = 0;
            var where = new List<string>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                if ((keep != null && keep(item)) || item.m_shared.m_questItem) continue;
                int before = item.m_stack;
                string name = item.m_shared.m_name, category = Categories.Of(item.m_shared);
                var steps = new Func<Container, bool>[]
                {
                    c => rules[c].WantsItem(name),
                    c => rules[c].WantsCategory(category),
                    c => c.GetInventory().ContainsItemByName(name),
                };
                foreach (Func<Container, bool> wants in steps)
                {
                    foreach (Container c in chests)
                    {
                        if (!inventory.ContainsItem(item)) break;
                        if (!wants(c)) continue;
                        int n = Deposit(c, inventory, item);
                        if (n > 0) where.Add($"{n} {Localization.instance.Localize(name)} in a chest {Vector3.Distance(here, c.transform.position):0} m away");
                    }
                    if (!inventory.ContainsItem(item)) break;
                }
                moved += before - (inventory.ContainsItem(item) ? item.m_stack : 0);
            }
            if (where.Count > 0) Instance?.Logger.LogInfo("Stacked for a companion: " + string.Join("; ", where.ToArray()));
            foreach (var kv in totalsBefore)
            {
                int now = Total(kv.Key);
                if (now < kv.Value) Instance?.Logger.LogError($"STACK LOST ITEMS (companion): {Localization.instance.Localize(kv.Key)} had {kv.Value} before and {now} after");
            }
            return moved;
        }

        // ---- undo ---------------------------------------------------------------------------------

        /// <summary>One stack (or part of one) that was moved into a chest.</summary>
        private class Moved { public Container Chest; public ItemDrop.ItemData Item; public Vector2i Slot; }

        /// <summary>What the last "Stack to chests" moved, or null if there's nothing to undo (never stacked, undone already, or the inventory was closed).</summary>
        private List<Moved> _undo;

        /// <summary>Take the items from the last stack back out of those chests and into your inventory, in their old slots where possible.</summary>
        private void UndoStack(Player player)
        {
            List<Moved> undo = _undo;
            _undo = null; // one undo per stack
            if (undo == null) return;

            Inventory inventory = player.GetInventory();
            int back = 0, couldNot = 0;
            var touched = new HashSet<Container>();

            foreach (Moved m in undo)
            {
                Inventory chest = m.Chest != null ? m.Chest.GetInventory() : null;
                if (chest == null) { couldNot += m.Item.m_stack; continue; } // the chest is gone
                if (ContainerRegistry.InUse(m.Chest)) { couldNot += m.Item.m_stack; continue; } // someone has it open: leave it alone
                ContainerRegistry.Reload(m.Chest); // count what's really in it now

                string name = m.Item.m_shared.m_name;
                int n = Math.Min(m.Item.m_stack, chest.CountItems(name, m.Item.m_quality)); // someone may have taken some since
                if (n <= 0) { couldNot += m.Item.m_stack; continue; }

                m.Item.m_stack = n;
                if (!inventory.CanAddItem(m.Item, n)) { couldNot += n; continue; } // no room: leave it in the chest rather than lose it

                ContainerRegistry.TakeOwnership(m.Chest);
                chest.RemoveItem(name, n, m.Item.m_quality);

                bool slotFree = inventory.GetItemAt(m.Slot.x, m.Slot.y) == null;
                if (slotFree ? inventory.AddItem(m.Item, m.Slot) : inventory.AddItem(m.Item)) back += n;
                touched.Add(m.Chest);
            }

            if (back > 0) PlayChestSounds(touched, opening: true);

            Tell(player, couldNot == 0
                ? $"Put back {back} item(s)"
                : $"Put back {back} item(s); {couldNot} couldn't be returned (taken from the chest, chest in use, or no room)");
        }

        // ---- buttons and badges -----------------------------------------------------------------

        private GUIStyle _stackLabel;

        private void OnGUI()
        {
            DrawQuickSetBadges();
            DrawStackButtons(); // the Sort button works on its own; Stack to chests needs the feature on
            if (!_stackEnabled.Value) return;
            DrawLockBadges();
            DrawAssignButton();
            DrawRulesWindow();
        }

        private bool ShowStackUi(out Player player, out InventoryGui gui)
        {
            player = Player.m_localPlayer;
            gui = InventoryGui.instance;
            return player != null && gui != null && InventoryGui.IsVisible() && gui.m_player != null;
        }

        /// <summary>A small red "KEEP" tab, shown only on a locked item while the mouse is over it.</summary>
        private void DrawLockBadges()
        {
            if (Event.current.type != EventType.Repaint || !ShowStackUi(out Player player, out InventoryGui gui) || gui.m_playerGrid == null) return;

            ItemDrop.ItemData item = HoveredItem(player);
            if (item == null || !IsKept(item)) return;

            var elements = ElementsField?.GetValue(gui.m_playerGrid) as IList;
            if (elements == null) return;
            int index = item.m_gridPos.y * player.GetInventory().GetWidth() + item.m_gridPos.x;
            if (index < 0 || index >= elements.Count) return;
            var element = elements[index] as InventoryElement;
            if (element == null) return;
            EnsureStackStyle();

            RectTransform rect = element.GetElementRectTransform();
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Camera cam = rect.GetComponentInParent<Canvas>()?.worldCamera;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);

            var badge = new Rect(bottomLeft.x + 2f, Screen.height - bottomLeft.y - 15f, 34f, 13f); // bottom-left of the slot
            Rounded(badge, new Color(0.65f, 0.15f, 0.15f, 0.95f), 3f);
            GUI.Label(badge, "KEEP", _stackLabel);
        }

        /// <summary>The two buttons under your inventory panel. They exist only while a chest is in range.</summary>
        private void DrawStackButtons()
        {
            if (!_showStackButtons.Value || !ShowStackUi(out Player player, out InventoryGui gui)) return;
            bool stack = _stackEnabled.Value && _stackChests.Count > 0 && OpenChest() == null; // with a chest open the game's own stack/take buttons do the job
            bool sort = _sortEnabled.Value;
            if (!stack && !sort) return;
            // How far down the buttons (and the hint under Stack to chests) reach, for other mods' panels below the inventory.
            AppDomain.CurrentDomain.SetData("DHack.QoL.UnderInventoryHeight", stack ? 54f : 36f);
            EnsureStackStyle();

            var corners = new Vector3[4];
            gui.m_player.GetWorldCorners(corners);
            Canvas canvas = gui.m_player.GetComponentInParent<Canvas>();
            Camera cam = canvas != null ? canvas.worldCamera : null;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            float scale = canvas != null ? Mathf.Max(0.6f, canvas.scaleFactor) : 1f;

            float h = 30f * scale;
            float x = bottomLeft.x + 8f * scale;
            float y = Screen.height - bottomLeft.y + 6f * scale; // IMGUI's y runs top-down

            // One row: Sort, Stack to chests, Undo. Other mods (GearSlots) read how much room this takes.
            if (sort)
            {
                var sortRect = new Rect(x, y, 70f * scale, h);
                if (StackButton(sortRect, "Sort")) { Player p = player; _pending = () => SortInventory(p); }
                x = sortRect.xMax + 6f * scale;
            }

            float hintX = x;
            if (stack)
            {
                var stackRect = new Rect(x, y, 170f * scale, h);
                if (StackButton(stackRect, $"Stack to chests ({_stackChests.Count})")) { Player p = player; _pending = () => StackToChests(p); }
                x = stackRect.xMax + 6f * scale;

                // Undo appears only right after a stack that really moved something, and goes when you close the inventory.
                if (_undo != null)
                {
                    var undo = new Rect(x, y, 80f * scale, h);
                    if (StackButton(undo, "Undo")) { Player p = player; _pending = () => UndoStack(p); }
                }

                if (Event.current.type == EventType.Repaint)
                {
                    _stackLabel.fontSize = Mathf.RoundToInt(10f * scale);
                    GUI.color = new Color(1f, 1f, 1f, 0.65f);
                    GUI.Label(new Rect(hintX, y + h + 3f * scale, 190f * scale, 16f * scale), $"Hover an item + {_lockKey.Value} to lock it", _stackLabel);
                    GUI.color = Color.white;
                    _stackLabel.fontSize = 10;
                }
            }
        }

        private bool StackButton(Rect r, string text)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                Rounded(new Rect(r.x + 1f, r.y + 2f, r.width, r.height), new Color(0f, 0f, 0f, 0.35f), 6f);
                Rounded(r, hover ? new Color(0.36f, 0.29f, 0.18f, 0.97f) : new Color(0.2f, 0.17f, 0.13f, 0.95f), 6f);
                Outline(r, new Color(0.62f, 0.47f, 0.22f, 0.95f), 6f);
                int size = _stackLabel.fontSize;
                _stackLabel.fontSize = Mathf.RoundToInt(r.height * 0.42f);
                GUI.Label(r, text, _stackLabel);
                _stackLabel.fontSize = size;
            }
            return GUI.Button(r, GUIContent.none, GUIStyle.none); // invisible: just catches the click
        }

        private void EnsureStackStyle()
        {
            if (_stackLabel != null) return;
            _stackLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            };
            _stackLabel.normal.textColor = new Color(0.95f, 0.9f, 0.8f);
        }

        private static void Rounded(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(radius, radius, radius, radius));

        private static void Outline(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.one, new Vector4(radius, radius, radius, radius));
    }
}
