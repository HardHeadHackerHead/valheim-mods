using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>
    /// The building blocks, in a built dungeon (their fields: BlockDef in TriggerModel.cs; the pack format: PackReaderTriggers.cs). No look
    /// is built in: each is its pack's parts, with parts shown only in some states ("show"), and the game's sounds the pack names ("fx").
    /// Their state is a mechanism's state like any other (saved by MechRecords, moved by the server's clock, changed only by the entrance's
    /// owner), so triggers and the solver see them as mechanisms:
    /// - pedestal (0 empty, 1 its item, 2 another): a player's game takes the item off them and keeps it until the owner says it's on (the
    ///   "who" in TriggerState), giving it back if no word comes; taking it off, the owner drops it at the pedestal's foot (a saved item);
    /// - brazier (0 out, 1 lit): a player with a torch in hand (E), or a fire hit, asks the owner to light it; it goes out after "burn";
    /// - pushblock (a grid cell each): walking into it for a moment asks the owner to push it a cell (if the cell ahead is floor and free);
    /// - trap (0 off, 1 on): fires on its own clock while on; every game hurts its own player in its area, the owner the creatures it owns;
    /// - teleporter (0 off, 1 on): a player's own game takes them to the pair's other end;
    /// - lock (a gate): E with its key asks the owner to open it; the key is taken from the player only once the owner says it's open;
    /// - checkpoint: each player's own (Checkpoints.cs).
    /// </summary>
    internal partial class MechSet
    {
        private const float PushHold = 0.3f, StepCooldown = 2.5f, EscrowWait = 6f;

        /// <summary>What this game keeps of a block it built.</summary>
        private sealed class BlockRun
        {
            public readonly List<KeyValuePair<GameObject, List<int>>> Shown = new List<KeyValuePair<GameObject, List<int>>>();
            public int ShownState = -2;         // the state its "show" parts were last set for (-2: never)
            // its parts' fire, light, warmth and crackle, each with the states it burns in (null: a brazier's, lit only)
            public readonly List<KeyValuePair<GameObject, List<int>>> Flames = new List<KeyValuePair<GameObject, List<int>>>();
            public Transform Anchor;            // a pedestal: where its item shows
            public GameObject ItemVisual;
            public string ItemShown;
            public Transform TrapPart;          // a trap: what its own clock moves
            public Bounds Area;                 // a trap: where it hurts (the mechanism's frame); a pushblock: its box
            public long Window = long.MinValue; // a trap: the firing seen last (each game hurts once per firing)
            public float PushFor, NextPush;
            public int PushDir = -1;
            public float NextStep;              // a teleporter stepped on: not again before
        }

        private readonly Dictionary<Mech, BlockRun> _blocks = new Dictionary<Mech, BlockRun>();
        private readonly Dictionary<Character, long> _trapHit = new Dictionary<Character, long>();

        // a player's item on its way to a pedestal (this game), or a lock this game asked to open: until the owner's word
        private static Escrow _escrow;
        private static string _unlocking;
        private static float _unlockAsked;

        private sealed class Escrow
        {
            public string Site, Mech, Prefab;
            public ItemDrop.ItemData Item;
            public double Sent;
            public float Until;
        }

        private BlockRun BlockOf(Mech m)
        {
            if (!_blocks.TryGetValue(m, out BlockRun r)) _blocks[m] = r = new BlockRun();
            return r;
        }

        // ---- made (MakeOne's hooks)

        /// <summary>MakeOne, each part made: one shown only in some states is kept, to show and hide; a brazier's flames are found. Both are
        /// set for the state it's in at once, before the frame is drawn: nothing ever shows lit and then goes out.</summary>
        private void BlockParts(Mech m, PartDef part, GameObject made)
        {
            if (made == null || part == null) return;
            bool shown = part.Show != null && part.Show.Count > 0, burns = part.Lit != null || m.Def.Kind == "brazier";
            if (!shown && !burns) return;
            BlockRun r = BlockOf(m);
            int s = FirstState(m);
            r.ShownState = s;
            if (shown)
            {
                r.Shown.Add(new KeyValuePair<GameObject, List<int>>(made, part.Show));
                made.SetActive(part.Show.Contains(s));
            }
            if (burns)
                foreach (GameObject flame in FlamesOf(made))
                {
                    r.Flames.Add(new KeyValuePair<GameObject, List<int>>(flame, part.Lit));
                    flame.SetActive(Burns(part.Lit, s));
                }
        }

        /// <summary>A part's fire in a state: its "lit" states, or a brazier's (lit only).</summary>
        private static bool Burns(List<int> lit, int state) => lit != null ? lit.Contains(state) : state >= 1;

        /// <summary>The state a block shows as it's made: its saved one (a dungeon built again with its braziers lit), a checkpoint's
        /// whether it's this player's.</summary>
        private int FirstState(Mech m)
        {
            if (m.Def.Kind == "checkpoint") return Checkpoints.IsMine(Site, m) ? 1 : 0;
            ReadRecords();
            return Motion(m, MechClock.Now).To;
        }

        /// <summary>
        /// A part's flames (a brazier's, or one with "lit"): what the game's fire turns on when it burns (each Fireplace's m_enabledObject and m_enabledObjectHigh:
        /// the flame particles, the light, the warmth, the crackle), read before Builder.Strip's destroying of the Fireplace takes effect
        /// (Strip lights every fire it strips, the game's "unlit" torches included). A part with no Fireplace: its lights, particle systems
        /// and looping sounds. Never the part's own model: a brazier keeps one model, lit or out.
        /// </summary>
        private static List<GameObject> FlamesOf(GameObject part)
        {
            var found = new List<GameObject>();
            foreach (Fireplace f in part.GetComponentsInChildren<Fireplace>(true))
                foreach (GameObject on in new[] { f.m_enabledObject, f.m_enabledObjectHigh })
                    if (on != null && on != part && !found.Contains(on)) found.Add(on);
            if (found.Count > 0) return found;
            var bits = part.GetComponentsInChildren<Light>(true).Select(c => c.gameObject)
                .Concat(part.GetComponentsInChildren<ParticleSystem>(true).Where(ps => ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null).Select(c => c.gameObject))
                .Concat(part.GetComponentsInChildren<AudioSource>(true).Where(a => a.loop).Select(c => c.gameObject));
            foreach (GameObject go in bits) if (go != part && !found.Contains(go)) found.Add(go);
            return found;
        }

        /// <summary>MakeOne, at its end: what a block needs on its pivot (what E finds, what moves on its own clock).</summary>
        private void MakeBlock(Mech m, GameObject pivot)
        {
            if (!TriggerReader.IsBlock(m.Def) || m.Def.Block == null) return;
            BlockRun r = BlockOf(m);
            BlockDef b = m.Def.Block;
            switch (m.Def.Kind)
            {
                case "pedestal":
                    AddBox(m, true, AimMargin);
                    var anchor = new GameObject("quaddungeon_item");
                    anchor.transform.SetParent(m.Pivot, false);
                    anchor.transform.localPosition = b.ItemAt;
                    r.Anchor = anchor.transform;
                    pivot.AddComponent<DungeonPedestal>().Init(this, m.Key);
                    break;
                case "brazier":
                    AddBox(m, true, AimMargin);
                    pivot.AddComponent<DungeonBrazier>().Init(this, m.Key);
                    break;
                case "lock":
                    AddBox(m, true, AimMargin);
                    pivot.AddComponent<DungeonLock>().Init(this, m.Key);
                    break;
                case "teleporter":
                    AddBox(m, true, AimMargin);
                    pivot.AddComponent<DungeonTeleporter>().Init(this, m.Key);
                    break;
                case "checkpoint":
                    AddBox(m, true, AimMargin);
                    pivot.AddComponent<DungeonCheckpoint>().Init(this, m.Key);
                    break;
                case "pushblock":
                    if (m.Colliders.Count == 0) AddBox(m, false);   // (it must be something to walk into)
                    r.Area = LocalBounds(m.Pivot, m.Pivot);
                    pivot.AddComponent<DungeonPushblock>().Init(this, m.Key);
                    break;
                case "trap":
                    // its parts under one child its own clock moves (the pivot stays where the state puts it)
                    var part = new GameObject("quaddungeon_trap");
                    part.transform.SetParent(m.Pivot, false);
                    foreach (Transform child in m.Pivot.Cast<Transform>().ToList())
                        if (child != part.transform && !child.name.StartsWith("quaddungeon_aim")) child.SetParent(part.transform, true);
                    r.TrapPart = part.transform;
                    if (b.AreaSize != Vector3.zero) r.Area = new Bounds(b.AreaAt, b.AreaSize);
                    else
                    {
                        Bounds parts = LocalBounds(m.Pivot, m.Base);
                        parts.SetMinMax(parts.min - new Vector3(0.1f, 0.3f, 0.1f), new Vector3(parts.max.x + 0.1f, parts.max.y + 1.2f, parts.max.z + 0.1f));
                        r.Area = parts;
                    }
                    break;
            }
        }

        /// <summary>The box over what shows of everything under root, in frame's local space (a small box at its origin if nothing shows).</summary>
        private static Bounds LocalBounds(Transform root, Transform frame)
        {
            Bounds? local = null;
            foreach (Renderer rd in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(rd is MeshRenderer || rd is SkinnedMeshRenderer)) continue;
                foreach (Vector3 corner in Corners(rd.bounds))
                {
                    Vector3 c = frame.InverseTransformPoint(corner);
                    if (local == null) local = new Bounds(c, Vector3.zero);
                    else { Bounds l = local.Value; l.Encapsulate(c); local = l; }
                }
            }
            return local ?? new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
        }

        // ---- every game, every frame (Update's hook)

        /// <summary>Every game: parts shown for the state, pedestals' items, traps (moving, hurting), pushing, teleporters stepped on, a
        /// player's escrowed item or key settled, and where this game's player is told to the owner.</summary>
        private void BlocksUpdate()
        {
            RegisterNotices();
            TellPosition();
            if (_blocks.Count == 0 && _escrow == null && _unlocking == null) return;
            double now = MechClock.Now;
            Player p = Player.m_localPlayer;
            foreach (KeyValuePair<Mech, BlockRun> kv in _blocks)
            {
                Mech m = kv.Key;
                BlockRun r = kv.Value;
                if (m.Pivot == null) continue;
                try
                {
                    int s = m.Def.Kind == "checkpoint" ? (Checkpoints.IsMine(Site, m) ? 1 : 0) : StateOf(m);
                    if (s != r.ShownState)
                    {
                        bool first = r.ShownState == -2;
                        r.ShownState = s;
                        foreach (KeyValuePair<GameObject, List<int>> part in r.Shown) if (part.Key != null) part.Key.SetActive(part.Value.Contains(s));
                        foreach (KeyValuePair<GameObject, List<int>> flame in r.Flames) if (flame.Key != null) flame.Key.SetActive(Burns(flame.Value, s));
                        // its sound, where the change is fresh (a block built again in a state plays nothing)
                        if (!first && m.Def.Block != null && m.Def.Kind != "trap" && m.Def.Kind != "checkpoint" && now - Motion(m, now).Start < 1.5)
                            foreach (string fx in m.Def.Block.Fx) PlayAt(fx, m.WorldPos);
                    }
                    switch (m.Def.Kind)
                    {
                        case "pedestal": ShowItem(m, r); break;
                        case "trap": Trap(m, r, now, p); break;
                        case "pushblock": if (p != null) Push(m, r, p); break;
                        case "teleporter": if (p != null && m.Def.Block != null && m.Def.Block.Step) StepOn(m, r, p); break;
                    }
                }
                catch (Exception e) { WarnOnce($"Dungeon {Site.Id}: block {m.Key}: {e}"); }
            }
            Settle(now);
        }

        // ---- pedestals

        private void ShowItem(Mech m, BlockRun r)
        {
            string want = TrigValue("p|" + m.Key);
            if (want == r.ItemShown) return;
            if (r.ItemVisual != null) Object.Destroy(r.ItemVisual);
            r.ItemVisual = null;
            r.ItemShown = want;
            if (string.IsNullOrEmpty(want) || r.Anchor == null || ObjectDB.instance == null) return;
            GameObject prefab = ObjectDB.instance.GetItemPrefab(want);
            if (prefab == null) return;
            r.ItemVisual = ItemVisual(prefab, r.Anchor);
        }

        /// <summary>An item's look, local, nothing saved and nothing to pick up: its item-stand model (the "attach" child the game hangs on
        /// stands), else the item itself stripped to its model.</summary>
        private static GameObject ItemVisual(GameObject prefab, Transform at)
        {
            GameObject attach = ItemStand.GetAttachPrefab(prefab);
            bool was = ZNetView.m_forceDisableInit;
            GameObject go;
            try
            {
                ZNetView.m_forceDisableInit = true;
                go = Object.Instantiate(attach != null ? ItemStand.GetAttachGameObject(attach) : prefab, at, false);
            }
            finally { ZNetView.m_forceDisableInit = was; }
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = attach != null ? attach.transform.localRotation : Quaternion.identity;
            foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(body);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is ParticleIntensityScaler)) Object.Destroy(mb);
            return go;
        }

        /// <summary>A pedestal's item, what a player sees it called.</summary>
        public string ItemOn(Mech m) => TrigValue("p|" + m.Key);

        /// <summary>This game's player puts one of their items on a pedestal: taken from them now, kept here until the owner's word.</summary>
        public bool PlaceFrom(Player p, Mech m, ItemDrop.ItemData item)
        {
            if (_escrow != null || item == null || m.Def.Block == null) return false;
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (prefab == null || !m.Def.Block.Accepts.Any(a => TriggerReader.ItemPrefab(Layout.Pack, a) == prefab)) { p.Message(MessageHud.MessageType.Center, T("dungeons_pedestal_cant")); return false; }
            // only an item as it was found: what comes back off a pedestal is a new one (no upgrades, wear or marks to lose)
            bool marked = item.m_customData.Keys.Any(k => k != BlockKeys.BoundKey);
            if (item.m_quality > 1 || (item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability()) || marked)
            { p.Message(MessageHud.MessageType.Center, T("dungeons_pedestal_unfit")); return false; }
            if (BlockKeys.IsKey(prefab) && !BlockKeys.BoundTo(item, Site)) { p.Message(MessageHud.MessageType.Center, T("dungeons_pedestal_cant")); return false; }
            ItemDrop.ItemData one = item.Clone();
            one.m_stack = 1;
            if (!p.GetInventory().RemoveItem(item, 1)) return false;
            _escrow = new Escrow { Site = Site.Id, Mech = m.Key, Prefab = prefab, Item = one, Sent = MechClock.Now, Until = Time.time + EscrowWait };
            Site.Act("blk:place:" + m.Key + ":" + prefab);
            return true;
        }

        /// <summary>The owner's word on what this game sent: an item on a pedestal (the owner says who put it there), a lock opened with
        /// this player's key (taken now). No word in time: the item is given back.</summary>
        private void Settle(double now)
        {
            Player p = Player.m_localPlayer;
            long me = ZDOMan.GetSessionID();
            if (_escrow != null && _escrow.Site == Site.Id)
            {
                string who = TrigValue("w|" + _escrow.Mech);
                string[] f = (who ?? "").Split(',');
                if (f.Length >= 2 && TriggerState.L(f[0]) == me && TriggerState.D(f[1]) >= _escrow.Sent - 2.0 && TrigValue("p|" + _escrow.Mech) == _escrow.Prefab)
                    _escrow = null;   // (it's on: done)
                else if (Time.time > _escrow.Until)
                {
                    if (p != null) GiveToPlayer(p, _escrow.Item);
                    Plugin.Log.LogInfo($"Dungeon {Site.Id}: the pedestal {_escrow.Mech} didn't take {_escrow.Prefab}: given back");
                    _escrow = null;
                }
            }
            if (_unlocking != null && p != null && ByKey.TryGetValue(_unlocking, out Mech lockM))
            {
                string[] f = (TrigValue("w|" + _unlocking) ?? "").Split(',');
                if (f.Length >= 2 && TriggerState.L(f[0]) == me && StateOf(lockM) > 0)
                {
                    BlockKeys.TakeOne(p, lockM.Def.Block?.Key == null ? null : TriggerReader.ItemPrefab(Layout.Pack, lockM.Def.Block.Key), Site);
                    _unlocking = null;
                }
                else if (Time.time - _unlockAsked > EscrowWait) _unlocking = null;   // (no word: the key stays theirs)
            }
        }

        /// <summary>The local player, once a second (BlockKeys.Tick): an item on its way to a pedestal whose dungeon went from this game
        /// before the owner's word came (its Settle never runs again) is given back.</summary>
        public static void SettleOrphan(Player p)
        {
            if (_escrow == null || p == null || Time.time < _escrow.Until + 2f) return;
            GiveToPlayer(p, _escrow.Item);
            Plugin.Log.LogInfo($"An item on its way to pedestal {_escrow.Mech} ({_escrow.Prefab}) had no answer: given back");
            _escrow = null;
        }

        /// <summary>Leaving the world: nothing kept for the next.</summary>
        public static void ForgetStatic() { _escrow = null; _unlocking = null; }

        private static void GiveToPlayer(Player p, ItemDrop.ItemData item)
        {
            if (p.GetInventory().CanAddItem(item)) p.GetInventory().AddItem(item);
            else ItemDrop.DropItem(item, item.m_stack, p.transform.position + p.transform.forward * 0.6f + Vector3.up, Quaternion.identity);
        }

        /// <summary>The owner: a pedestal's item dropped as a saved item (at its foot, or outside the entrance for a dungeon removed); a key
        /// item isn't (it belongs to the dungeon). The pedestal empty again.</summary>
        private void GiveBackItem(Mech m, bool outside)
        {
            string prefab = TrigValue("p|" + m.Key);
            if (string.IsNullOrEmpty(prefab)) return;
            Vector3 at = outside ? Dungeons.OutsidePoint(Site) : FootOf(m);
            if (!BlockKeys.IsKey(prefab) || !outside) DropPrefab(prefab, at, !outside);
            PutTrig(new[] { new KeyValuePair<string, string>("p|" + m.Key, null), new KeyValuePair<string, string>("w|" + m.Key, null) });
            Change(new[] { new KeyValuePair<Mech, int>(m, 0) }, $"{m.Key} gives its {prefab} back");
        }

        /// <summary>In front of a block, a little up (its +z, as a shortcut's front).</summary>
        private static Vector3 FootOf(Mech m) => (m.Base != null ? m.Base.position + m.Base.forward * 0.9f : m.WorldPos) + Vector3.up * 0.4f;

        private void DropPrefab(string prefab, Vector3 at, bool bind)
        {
            GameObject source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (source?.GetComponent<ItemDrop>() == null) { WarnOnce($"Dungeon {Site.Id}: no item {prefab} to give back"); return; }
            GameObject go = Object.Instantiate(source, at, Quaternion.identity);
            ItemDrop drop = go.GetComponent<ItemDrop>();
            ZNetView view = go.GetComponent<ZNetView>();
            if (bind && drop != null && view != null && view.IsValid() && BlockKeys.IsKey(prefab))
            {
                BlockKeys.Bind(drop.m_itemData, Site);
                ItemDrop.SaveToZDO(drop.m_itemData, view.GetZDO());
            }
        }

        // ---- traps

        /// <summary>Whether a trap fires now, and since when its firing (or rest) began; window: which firing (for hurting once each).</summary>
        private bool Firing(Mech m, double now, out double since, out long window)
        {
            BlockDef b = m.Def.Block;
            MechMotion mo = Motion(m, now);
            since = 0.0;
            window = long.MinValue;
            if (mo.To != 1 || now < mo.Start) { since = double.IsInfinity(mo.Start) ? 99.0 : Math.Max(0.0, now - mo.Start); return false; }
            if (b.Every > 0f)
            {
                double t = now - b.Offset;
                window = (long)Math.Floor(t / b.Every);
                double c = t - window * (double)b.Every;
                since = c < b.For ? c : c - b.For;
                return c < b.For;
            }
            window = (long)Math.Round(mo.Start * 10.0);
            double age = now - mo.Start;
            since = age < b.For ? age : age - b.For;
            return age < b.For;
        }

        private void Trap(Mech m, BlockRun r, double now, Player p)
        {
            BlockDef b = m.Def.Block;
            if (b == null) return;
            bool firing = Firing(m, now, out double since, out long window);
            // its parts: up in a tenth of a second, back down in under half a second
            if (r.TrapPart != null && b.TrapPoses.Count >= 2)
            {
                float k = firing ? Mathf.Clamp01((float)since / 0.12f) : 1f - Mathf.Clamp01((float)since / 0.4f);
                // (the pivot stands at the rest pose: the child moves by the difference)
                Pose rest = b.TrapPoses[0], fired = b.TrapPoses[1];
                Quaternion inv = Quaternion.Inverse(Quaternion.Euler(rest.Euler));
                r.TrapPart.localPosition = inv * (Vector3.Lerp(rest.At, fired.At, k) - rest.At);
                r.TrapPart.localRotation = inv * Quaternion.Slerp(Quaternion.Euler(rest.Euler), Quaternion.Euler(fired.Euler), k);
            }
            if (!firing || window == r.Window) return;
            bool fresh = r.Window != long.MinValue || since < 0.3;
            r.Window = window;
            if (fresh && p != null && Vector3.Distance(p.transform.position, m.WorldPos) < 40f)
                foreach (string fx in b.Fx) PlayAt(fx, m.WorldPos);
            // this game's player (each game hurts its own), and on the owner the creatures it owns
            if (p != null && !p.IsDead() && r.Area.Contains(m.Base.InverseTransformPoint(p.transform.position + Vector3.up * 0.5f))) Hurt(m, p);
            if (!Site.IsOwner) return;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c is Player || c.IsDead() || !c.IsOwner()) continue;
                if (!r.Area.Contains(m.Base.InverseTransformPoint(c.transform.position + Vector3.up * 0.5f))) continue;
                if (_trapHit.TryGetValue(c, out long w) && w == window) continue;
                _trapHit[c] = window;
                Hurt(m, c);
            }
            if (_trapHit.Count > 200) _trapHit.Clear();
        }

        /// <summary>A trap's damage, fitted to the land: its tier's base (Meadows 8 ... Deep North 95) times its "scale", never more than a
        /// share of the victim's health (a third up to the Black Forest, half to the Mountain, 70% after): no one-shot kill at low tiers.</summary>
        private void Hurt(Mech m, Character c)
        {
            BlockDef b = m.Def.Block;
            int tier = WorldGenerator.instance != null ? Builder.TierOf(WorldGenerator.instance.GetBiome(Site.EntrancePos)) : 1;
            float[] table = { 8f, 8f, 15f, 25f, 35f, 50f, 65f, 80f, 95f };
            float damage = table[Mathf.Clamp(tier, 1, 8)] * Mathf.Clamp(b.Scale, 0.1f, 5f);
            float share = tier <= 2 ? 0.34f : tier <= 4 ? 0.5f : 0.7f;
            damage = Mathf.Min(damage, c.GetMaxHealth() * share);
            var hit = new HitData { m_point = c.transform.position + Vector3.up, m_dir = Vector3.up, m_hitType = HitData.HitType.Undefined };
            switch (b.Damage)
            {
                case "blunt": hit.m_damage.m_blunt = damage; break;
                case "slash": hit.m_damage.m_slash = damage; break;
                case "fire": hit.m_damage.m_fire = damage; break;
                case "frost": hit.m_damage.m_frost = damage; break;
                case "lightning": hit.m_damage.m_lightning = damage; break;
                case "poison": hit.m_damage.m_poison = damage; break;
                case "spirit": hit.m_damage.m_spirit = damage; break;
                default: hit.m_damage.m_pierce = damage; break;
            }
            c.Damage(hit);
        }

        // ---- pushblocks

        /// <summary>This game's player walking into a pushblock (close to one face, moving towards it) for a moment: asks the owner to push
        /// it a cell the other way.</summary>
        private void Push(Mech m, BlockRun r, Player p)
        {
            if (p.IsDead() || !p.IsOnGround() || Time.time < r.NextPush || m.Base == null) { r.PushFor = 0f; return; }
            Vector3 half = r.Area.extents;
            Vector3 local = m.Pivot.InverseTransformPoint(p.transform.position) - r.Area.center;
            Vector3 move = m.Pivot.InverseTransformDirection(p.GetMoveDir());
            if (Mathf.Abs(local.y) > half.y + 1f || move.sqrMagnitude < 0.25f) { r.PushFor = 0f; return; }
            int dir = -1;
            // which face: the player beyond it on one axis, within its width on the other, moving into it
            if (Mathf.Abs(local.z) < half.z && local.x < -half.x && local.x > -half.x - 0.9f && move.x > 0.7f) dir = 0;        // pushes +x
            else if (Mathf.Abs(local.z) < half.z && local.x > half.x && local.x < half.x + 0.9f && move.x < -0.7f) dir = 1;    // pushes -x
            else if (Mathf.Abs(local.x) < half.x && local.z < -half.z && local.z > -half.z - 0.9f && move.z > 0.7f) dir = 2;   // pushes +z
            else if (Mathf.Abs(local.x) < half.x && local.z > half.z && local.z < half.z + 0.9f && move.z < -0.7f) dir = 3;    // pushes -z
            if (dir < 0 || dir != r.PushDir) { r.PushDir = dir; r.PushFor = 0f; return; }
            r.PushFor += Time.deltaTime;
            if (r.PushFor < PushHold) return;
            r.PushFor = 0f;
            r.NextPush = Time.time + Mathf.Max(0.3f, m.Def.Seconds + 0.2f);
            Site.Act("blk:push:" + m.Key + ":" + dir);
        }

        /// <summary>The owner: a push, if the block is at rest, the cell ahead is floor in its grid, and no other pushblock stands there.</summary>
        private bool PushBlock(Mech m, int dir, out string why)
        {
            why = null;
            BlockDef b = m.Def.Block;
            double now = MechClock.Now;
            MechMotion mo = Motion(m, now);
            if (mo.Moving(now)) { why = "it's still moving"; return false; }
            int s = mo.To;
            if (b == null || s < 0 || s >= b.Cells.Count) { why = "it isn't on its grid"; return false; }
            Vector2Int at = b.Cells[s];
            // +x: the next column; +z: the row before (row 0 the farthest +z)
            int c = at.x + (dir == 0 ? 1 : dir == 1 ? -1 : 0), row = at.y + (dir == 2 ? -1 : dir == 3 ? 1 : 0);
            int to = TriggerReader.StateAt(b, c, row);
            if (to < 0) { why = "a wall"; return false; }
            Vector3 world = m.Base.TransformPoint(m.Def.Poses[to].At);
            foreach (Mech o in All)
                if (o != m && o.Def.Kind == "pushblock" && o.Pivot != null && Vector3.Distance(o.Pivot.position, world) < b.Cell * 0.5f) { why = "another block"; return false; }
            Change(new[] { new KeyValuePair<Mech, int>(m, to) }, $"{m.Key} pushed");
            return true;
        }

        // ---- teleporters

        /// <summary>A teleporter's other end (the pair's), or null.</summary>
        public Mech OtherTeleporter(Mech m) => All.FirstOrDefault(x => x != m && x.Def.Kind == "teleporter" && x.Def.Pair == m.Def.Pair);

        /// <summary>Whether a player may go from this end now: it sends, it's on, and its other end is there.</summary>
        public bool CanSend(Mech m, out Mech to)
        {
            to = OtherTeleporter(m);
            return to?.Base != null && (m.Def.Block == null || m.Def.Block.Send) && StateOf(m) >= 1;
        }

        /// <summary>This game's player to a teleporter's other end (their own game moves them: nothing to ask).</summary>
        public void Teleport(Player p, Mech from, Mech to)
        {
            Vector3 at = to.Base.position + to.Base.forward * 1.5f + Vector3.up * 0.2f;
            foreach (string fx in from.Def.Block?.Fx ?? new List<string>()) { PlayAt(fx, from.WorldPos); PlayAt(fx, at); }
            if (_blocks.TryGetValue(to, out BlockRun tr)) tr.NextStep = Time.time + StepCooldown;   // (arriving on a step-on end doesn't send back)
            Safety.OurTeleport();
            p.TeleportTo(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(to.Base.forward, Vector3.up).normalized), false);
        }

        private void StepOn(Mech m, BlockRun r, Player p)
        {
            if (p.IsDead() || p.IsTeleporting() || Time.time < r.NextStep || m.Base == null) return;
            Vector3 local = m.Base.InverseTransformPoint(p.transform.position);
            if (new Vector2(local.x, local.z).magnitude > 0.9f || Mathf.Abs(local.y) > 1.2f || !CanSend(m, out Mech to)) return;
            r.NextStep = Time.time + StepCooldown;
            Teleport(p, m, to);
        }

        // ---- locks

        /// <summary>This game's player asks the owner to open a lock with their key (taken from them once the owner says it's open).</summary>
        public void Unlock(Mech m)
        {
            _unlocking = m.Key;
            _unlockAsked = Time.time;
            Site.Act("blk:unlock:" + m.Key);
        }

        // ---- the owner: what players' games ask

        /// <summary>The owner: a block used ("blk:&lt;verb&gt;:&lt;key&gt;[:arg]"): only from someone standing near it.</summary>
        private void BlockUse(long sender, string verb, string rest)
        {
            int colon = rest.IndexOf(':');
            string key = colon < 0 ? rest : rest.Substring(0, colon), arg = colon < 0 ? "" : rest.Substring(colon + 1);
            if (!ByKey.TryGetValue(key, out Mech m) || m.Def.Block == null) return;
            if (!Near(sender, m, verb == "light" ? 30f : UseReach * 1.5f)) return;
            double now = MechClock.Now;
            string who = sender + "," + TriggerState.F(now);
            switch (verb)
            {
                case "light":
                    if (m.Def.Kind != "brazier" || !m.Def.Block.Lightable || StateOf(m) != 0) return;
                    Change(new[] { new KeyValuePair<Mech, int>(m, 1) }, $"{m.Key} lit");
                    break;
                case "push":
                    if (m.Def.Kind != "pushblock") return;
                    if (!PushBlock(m, TriggerState.I(arg), out string why)) Plugin.Log.LogInfo($"Dungeon {Site.Id}: {m.Key} won't move ({why})");
                    break;
                case "unlock":
                    if (m.Def.Kind != "lock" || StateOf(m) != 0) return;
                    PutTrig("w|" + m.Key, who);
                    Change(new[] { new KeyValuePair<Mech, int>(m, Math.Min(1, m.States - 1)) }, $"{m.Key} unlocked with {m.Def.Block.Key}");
                    break;
                case "place":
                {
                    if (m.Def.Kind != "pedestal" || !string.IsNullOrEmpty(TrigValue("p|" + m.Key))) return;   // (taken already: their game gives it back)
                    string prefab = arg;
                    if (!m.Def.Block.Accepts.Any(a => TriggerReader.ItemPrefab(Layout.Pack, a) == prefab)) return;
                    bool right = TriggerReader.ItemPrefab(Layout.Pack, m.Def.Block.Item) == prefab;
                    PutTrig(new[] { new KeyValuePair<string, string>("p|" + m.Key, prefab), new KeyValuePair<string, string>("w|" + m.Key, who) });
                    Change(new[] { new KeyValuePair<Mech, int>(m, right ? 1 : 2) }, $"{m.Key} takes {prefab}");
                    Fire(TriggerEvents.Place, m.Key, item: prefab);
                    break;
                }
                case "take":
                {
                    if (m.Def.Kind != "pedestal") return;
                    string prefab = TrigValue("p|" + m.Key);
                    if (string.IsNullOrEmpty(prefab)) return;
                    DropPrefab(prefab, FootOf(m), true);
                    PutTrig(new[] { new KeyValuePair<string, string>("p|" + m.Key, null), new KeyValuePair<string, string>("w|" + m.Key, null) });
                    Change(new[] { new KeyValuePair<Mech, int>(m, 0) }, $"{m.Key} gives up {prefab}");
                    Fire(TriggerEvents.Take, m.Key, item: prefab);
                    break;
                }
            }
        }

        /// <summary>Whether the sender stands within reach of a mechanism (their character if loaded here, else what their game last told,
        /// else, on a client owner that knows nothing of them, taken on trust as levers are).</summary>
        private bool Near(long sender, Mech m, float reach)
        {
            Player who = Player.GetAllPlayers().FirstOrDefault(x => x != null && x.GetOwner() == sender);
            if (who == null && sender == ZDOMan.GetSessionID()) who = Player.m_localPlayer;
            Vector3? at = who != null ? who.transform.position
                : _playerAt.TryGetValue(sender, out KeyValuePair<Vector3, float> told) && told.Value > Time.time - 3f ? told.Key
                : ZNet.instance?.GetPeer(sender) is ZNetPeer peer ? peer.m_refPos : (Vector3?)null;
            if (at == null) return true;
            float d = Vector3.Distance(at.Value, m.WorldPos);
            if (d <= (who != null ? reach : reach * 3f)) return true;
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: {m.Key} used from {d:0.0} m away: ignored");
            return false;
        }

        // ---- a reset, a hot reload

        private void ForgetBlocks()
        {
            foreach (BlockRun r in _blocks.Values) { r.ShownState = -2; r.Window = long.MinValue; r.PushFor = 0f; }
            _trapHit.Clear();
        }
    }
}
