using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace BountyBoard
{
    /// <summary>
    /// The group's contracts live on the host's game: it decides what is posted, who has taken what and how far along it is, and tells everyone.
    /// Players send it what they do (take a contract, kill something, hand in loot, collect a reward) and it answers. The state is also kept
    /// in a small file next to the host's settings, so it survives the host restarting.
    /// </summary>
    public partial class Plugin
    {
        private const string RpcReq = "DH_BB_Req", RpcState = "DH_BB_State", RpcKill = "DH_BB_Kill", RpcTake = "DH_BB_Take", RpcHand = "DH_BB_Hand",
                             RpcClaim = "DH_BB_Claim", RpcAbandon = "DH_BB_Abandon", RpcMsg = "DH_BB_Msg", RpcReward = "DH_BB_Reward", RpcRefund = "DH_BB_Refund",
                             RpcNote = "DH_BB_Note";
        private static readonly string[] AllRpcs = { RpcReq, RpcState, RpcKill, RpcTake, RpcHand, RpcClaim, RpcAbandon, RpcMsg, RpcReward, RpcRefund, RpcNote };

        private ZRoutedRpc _registeredOn;
        private int _awakeFrame;

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();
        private static long MyId => ZDOMan.GetSessionID();

        /// <summary>
        /// On the host: the character (its player id) playing on the connection a message came from. Worked out from the connection, never
        /// from what the message says, so nobody can collect a reward for another character. 0 when it can't be told.
        /// </summary>
        private static long CharacterOf(long sender)
        {
            if (sender == MyId) return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            ZNetPeer peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
            if (peer == null) return 0L;
            ZDO character = !peer.m_characterID.IsNone() ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            long id = character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
            return id != 0L ? id : peer.m_playerID;
        }

        /// <summary>Note a character as having earned a contract's reward.</summary>
        private static void Earn(Bounty b, long character)
        {
            if (character != 0L) (b.Earned ?? (b.Earned = new HashSet<long>())).Add(character);
        }

        // ---- wiring ----

        private void UpdateNetwork()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }
            if (Time.frameCount <= _awakeFrame + 2) return; // after a hot reload the old copy removes its handlers first

            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                UnregisterRpc(); // Valheim throws if a name is registered twice
                rpc.Register<string>(RpcReq, ServerReq);
                rpc.Register<string>(RpcKill, ServerKill);
                rpc.Register<string>(RpcTake, ServerTake);
                rpc.Register<string>(RpcHand, ServerHand);
                rpc.Register<string>(RpcClaim, ServerClaim);
                rpc.Register<string>(RpcAbandon, ServerAbandon);
                rpc.Register<string>(RpcState, ClientState);
                rpc.Register<string>(RpcMsg, ClientMsg);
                rpc.Register<string>(RpcNote, ClientNote);
                rpc.Register<string>(RpcReward, ClientReward);
                rpc.Register<string>(RpcRefund, ClientRefund);
                _askedAt = -999f;
            }

            if (IsServer) ServerTick();
            if (Player.m_localPlayer != null && Time.time >= _askedAt + (Window.IsOpen ? 6f : 25f)) { _askedAt = Time.time; ToServer(RpcReq, ""); }
        }

        private static void UnregisterRpc()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            if (table == null) return;
            foreach (string name in AllRpcs) table.Remove(name.GetStableHashCode());
        }

        /// <summary>Send to the host's game (or handle it right here if this game is the host).</summary>
        private static void ToServer(string rpc, string payload)
        {
            if (ZRoutedRpc.instance == null) return;
            if (!IsServer) { ZRoutedRpc.instance.InvokeRoutedRPC(rpc, payload); return; }
            Plugin p = Instance;
            if (p == null) return;
            switch (rpc)
            {
                case RpcReq: p.ServerReq(MyId, payload); break;
                case RpcKill: p.ServerKill(MyId, payload); break;
                case RpcTake: p.ServerTake(MyId, payload); break;
                case RpcHand: p.ServerHand(MyId, payload); break;
                case RpcClaim: p.ServerClaim(MyId, payload); break;
                case RpcAbandon: p.ServerAbandon(MyId, payload); break;
            }
        }

        private static void Reply(long to, string rpc, string payload)
        {
            if (to == MyId) { Deliver(rpc, payload); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(to, rpc, payload);
        }

        private static void Broadcast(string rpc, string payload)
        {
            Deliver(rpc, payload);
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, rpc, payload);
        }

        private static void Deliver(string rpc, string payload)
        {
            Plugin p = Instance;
            if (p == null) return;
            if (rpc == RpcState) p.ClientState(MyId, payload);
            else if (rpc == RpcMsg) p.ClientMsg(MyId, payload);
            else if (rpc == RpcNote) p.ClientNote(MyId, payload);
            else if (rpc == RpcReward) p.ClientReward(MyId, payload);
            else if (rpc == RpcRefund) p.ClientRefund(MyId, payload);
        }

        // =============================================================== the host's side

        private State _state;
        private string _statePath; // the file _state belongs to: hosting another world without restarting must not carry it over
        private float _nextDayCheck;
        private bool _dirty;
        private float _sentAt = -999f; // when the contracts last went out to everyone
        private bool _sendSoon;        // a kill's progress is waiting to go out (ServerTick sends it)

        // Kept per world by the world's unique id (two worlds can share a name); older versions used the name.
        private static string WorldFile(string world)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) world = world.Replace(c, '_');
            return Path.Combine(BepInEx.Paths.ConfigPath, "DHack.BountyBoard." + world + ".txt");
        }

        private string SavePath => WorldFile(ZNet.instance != null ? ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture) : "world");
        private string OldSavePath => WorldFile(ZNet.instance != null ? ZNet.instance.GetWorldName() : "world");

        private State Server()
        {
            string path = SavePath;
            if (_state != null && path == _statePath) return _state;
            if (_state != null && _dirty) WriteState(_statePath, _state); // the last world's unsaved changes go to its own file
            _statePath = path;
            _dirty = false;
            string from = File.Exists(path) ? path : File.Exists(OldSavePath) ? OldSavePath : null;
            try { _state = from != null ? State.Parse(File.ReadAllText(from)) : new State(); }
            catch (Exception e) { Logger.LogWarning("Could not read the saved contracts: " + e.Message); _state = new State(); }
            return _state;
        }

        private void SaveState() => WriteState(SavePath, Server());

        private void WriteState(string path, State state)
        {
            try { File.WriteAllText(path, state.Serialize()); }
            catch (Exception e) { Logger.LogWarning("Could not save the contracts: " + e.Message); }
        }

        private static int Today()
        {
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : (int)(ZNet.instance.GetTimeSeconds() / 1800.0);
            return day / Mathf.Max(1, RefreshDays.Value);
        }

        /// <summary>Post new notices when the day has turned (and the first time ever).</summary>
        private void EnsureToday()
        {
            State s = Server();
            int day = Today();
            if (s.Day == day && s.Posted.Count > 0) return;
            s.Day = day;
            s.Posted = Rules.MakeNotices(day, NoticesPerBoard.Value);
            SaveState();
            SendState();
        }

        private void ServerTick()
        {
            if (_sendSoon && Time.time >= _sentAt + 1f) SendState();
            if (Time.time < _nextDayCheck) return;
            _nextDayCheck = Time.time + 5f;
            EnsureToday();
        }

        private void Changed()
        {
            SaveState();
            SendState();
        }

        /// <summary>
        /// A kill moved a contract along: saved at once, but sent to everyone at most once a second. The whole board goes out each time
        /// (a few KB), and a sweep in a big fight counts several kills a second. The killer is told their progress at once (RpcNote).
        /// </summary>
        private void ChangedSoon()
        {
            SaveState();
            if (Time.time >= _sentAt + 1f) SendState();
            else _sendSoon = true;
        }

        private void SendState()
        {
            _sentAt = Time.time;
            _sendSoon = false;
            Broadcast(RpcState, Server().Serialize());
        }

        private void ServerReq(long sender, string _)
        {
            if (!IsServer) return;
            EnsureToday();
            Reply(sender, RpcState, Server().Serialize());
        }

        private void ServerTake(long sender, string id)
        {
            if (!IsServer) return;
            EnsureToday();
            State s = Server();
            Bounty posted = s.Posted.FirstOrDefault(b => b.Id == id);
            if (posted == null || s.Active.Count >= MaxActive.Value || s.Active.Any(a => a.Id == id) || s.Done.Any(d => d.Id == id))
            {
                Reply(sender, RpcNote, Note("unavailable"));
                return;
            }
            Bounty taken = posted.Copy();
            taken.Progress = 0;
            taken.Earned = new HashSet<long>();
            Earn(taken, CharacterOf(sender));
            s.Active.Add(taken);
            Broadcast(RpcNote, Note("taken", taken));
            Changed();
        }

        private void ServerAbandon(long sender, string id)
        {
            if (!IsServer) return;
            State s = Server();
            Bounty b = s.Active.FirstOrDefault(a => a.Id == id);
            if (b == null || b.Progress > 0) return; // once the group has made progress it has to be finished
            s.Active.Remove(b);
            Changed();
        }

        private void ServerKill(long sender, string payload)
        {
            if (!IsServer) return;
            string[] f = payload.Split('|');
            if (f.Length < 2) return;
            string prefab = f[0];
            bool starred = f[1] == "1";
            // (the game that owns the creature reports it, for the player who killed it: credit that player if they are playing here)
            if (f.Length > 2 && long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long killer) && killer != 0 &&
                (killer == MyId || ZNet.instance.GetPeer(killer) != null)) sender = killer;
            long who = CharacterOf(sender);
            State s = Server();
            bool any = false, finished = false;
            foreach (Bounty b in s.Active.ToList())
            {
                if (b.Progress >= b.Count || b.Kind == Kind.Gather) continue;
                bool match = b.Kind == Kind.Sweep ? Rules.InTier(int.Parse(b.Target), prefab) : b.Target == prefab && (b.Kind != Kind.Elite || starred);
                if (!match) continue;
                b.Progress++;
                Earn(b, who);
                any = true;
                if (b.Progress >= b.Count) { Finish(s, b); finished = true; }
                else Reply(sender, RpcNote, Note("progress", b));
            }
            if (finished) Changed(); // a finished contract goes out at once
            else if (any) ChangedSoon();
        }

        private void ServerHand(long sender, string payload)
        {
            if (!IsServer) return;
            string[] f = payload.Split('|');
            if (f.Length < 2 || !int.TryParse(f[1], out int n) || n <= 0) return;
            State s = Server();
            Bounty b = s.Active.FirstOrDefault(a => a.Id == f[0]);
            if (b == null || b.Kind != Kind.Gather) { Reply(sender, RpcRefund, f.Length > 2 ? f[2] + "|" + n : "|" + n); return; }
            int take = Mathf.Min(n, b.Count - b.Progress);
            b.Progress += take;
            if (take > 0) Earn(b, CharacterOf(sender));
            if (n > take) Reply(sender, RpcRefund, b.Target + "|" + (n - take));
            if (b.Progress >= b.Count) Finish(s, b);
            else Reply(sender, RpcNote, Note("handed", b, take));
            Changed();
        }

        /// <summary>A contract is done: it leaves the active list and waits for everyone to collect their reward.</summary>
        private void Finish(State s, Bounty b)
        {
            s.Active.Remove(b);
            b.Progress = b.Count;
            // everyone playing when it is finished has a share too (holding the fort counts)
            Earn(b, CharacterOf(MyId));
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) Earn(b, CharacterOf(peer.m_uid));
            s.Done.Insert(0, b);
            while (s.Done.Count > 12) s.Done.RemoveAt(s.Done.Count - 1);
            s.Total++;
            Broadcast(RpcNote, Note("complete", b));
        }

        private void ServerClaim(long sender, string payload)
        {
            if (!IsServer) return;
            string[] f = payload.Split('|'); // (the player id older versions add is not used: the host goes by who sent it)
            long player = CharacterOf(sender);
            if (player == 0L) return;
            State s = Server();
            Bounty b = s.Done.FirstOrDefault(d => d.Id == f[0]);
            if (b == null) return;
            if (b.Earned != null && !b.Earned.Contains(player)) { Reply(sender, RpcNote, Note("notearned")); return; }
            if (!b.Claimed.Add(player)) return;

            int coins = Rules.WithBonus(b.Coins, s.Total);
            string items = string.Join(",", b.Items.Select(i => i.Key + ":" + Rules.WithBonus(i.Value, s.Total)).ToArray());
            Reply(sender, RpcReward, coins + "|" + items);
            Changed();
        }

        // =============================================================== each player's side

        internal State Current;
        internal float CurrentAt = -999f;
        private float _askedAt;
        private readonly HashSet<int> _counted = new HashSet<int>();

        private void ClientState(long sender, string payload)
        {
            Current = State.Parse(payload);
            CurrentAt = Time.time;
        }

        /// <summary>
        /// A message for the players, sent as what happened (a code, the contract as data, a number), never as words: each player's game
        /// writes it in their own language (ClientNote). Older versions sent the English sentence (RpcMsg).
        /// </summary>
        private static string Note(string code, Bounty b = null, int n = 0) =>
            code + "|" + (b != null ? b.Serialize() : "") + "|" + n.ToString(CultureInfo.InvariantCulture);

        private void ClientNote(long sender, string payload)
        {
            Player me = Player.m_localPlayer;
            if (me == null) return;
            int first = payload.IndexOf('|'), last = payload.LastIndexOf('|');
            if (first < 0 || last <= first) return;
            string code = payload.Substring(0, first);
            Bounty b = last > first + 1 ? Bounty.Parse(payload.Substring(first + 1, last - first - 1)) : null;
            int.TryParse(payload.Substring(last + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
            string text = null;
            bool center = false;
            switch (code)
            {
                case "unavailable": text = T("bountyboard_msg_unavailable"); center = true; break;
                case "notearned": text = T("bountyboard_msg_not_earned"); center = true; break;
                case "taken": if (b != null) text = T("bountyboard_msg_taken", Rules.Describe(b)); break;
                case "progress": if (b != null) text = T("bountyboard_msg_progress", Rules.TargetName(b), b.Progress, b.Count); break;
                case "handed": if (b != null) text = T("bountyboard_msg_handed", n, b.Progress, b.Count); break;
                case "complete": if (b != null) text = T("bountyboard_msg_complete", Rules.Describe(b)); center = true; break;
            }
            if (text != null) me.Message(center ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft, text);
        }

        /// <summary>A message from an older host: its English sentence, shown as it is.</summary>
        private void ClientMsg(long sender, string payload)
        {
            Player me = Player.m_localPlayer;
            if (me == null) return;
            int bar = payload.IndexOf('|');
            if (bar < 0) return;
            me.Message(payload.Substring(0, bar) == "center" ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft, payload.Substring(bar + 1));
        }

        private void ClientReward(long sender, string payload)
        {
            Player me = Player.m_localPlayer;
            if (me == null) return;
            string[] f = payload.Split('|');
            int coins = f.Length > 0 && int.TryParse(f[0], out int c) ? c : 0;
            Give(me, "Coins", coins);
            var parts = new List<string>();
            if (coins > 0) parts.Add(T("bountyboard_amount", coins, Localization.instance.Localize("$item_coins")));
            foreach (string item in (f.Length > 1 ? f[1] : "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = item.Split(':');
                if (kv.Length != 2 || !int.TryParse(kv[1], out int n)) continue;
                Give(me, kv[0], n);
                parts.Add(T("bountyboard_amount", n, Localization.instance.Localize(Rules.ItemName(kv[0]))));
            }
            me.Message(MessageHud.MessageType.Center, T("bountyboard_reward", string.Join(", ", parts.ToArray())));
            BountyBoardStation.PlayEffects(me.transform.position, true);
        }

        private void ClientRefund(long sender, string payload)
        {
            Player me = Player.m_localPlayer;
            string[] f = payload.Split('|');
            if (me != null && f.Length == 2 && int.TryParse(f[1], out int n)) Give(me, f[0], n); // the contract no longer needed them: you get them back
        }

        /// <summary>Put items into the player's bag; what does not fit drops at their feet.</summary>
        internal static void Give(Player player, string itemPrefab, int amount)
        {
            GameObject prefab = ObjectDB.instance != null && itemPrefab.Length > 0 ? ObjectDB.instance.GetItemPrefab(itemPrefab) : null;
            if (prefab == null || amount <= 0) return;
            int max = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            while (amount > 0)
            {
                int n = Mathf.Min(amount, max);
                amount -= n;
                if (player.GetInventory().CanAddItem(prefab, n)) player.GetInventory().AddItem(prefab, n);
                else
                {
                    GameObject drop = UnityEngine.Object.Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity);
                    drop.GetComponent<ItemDrop>().SetStack(n);
                }
            }
        }

        // ---- what this player does ----

        internal void Take(Bounty b) => ToServer(RpcTake, b.Id);
        internal void Abandon(Bounty b) => ToServer(RpcAbandon, b.Id);
        internal void Claim(Bounty b) => ToServer(RpcClaim, b.Id + "|" + Player.m_localPlayer.GetPlayerID().ToString(CultureInfo.InvariantCulture));

        /// <summary>Hand in as much of the loot as you have (up to what is still needed).</summary>
        internal int HandIn(Bounty b)
        {
            Player me = Player.m_localPlayer;
            string name = Rules.ItemName(b.Target);
            int n = Mathf.Min(me.GetInventory().CountItems(name), b.Count - b.Progress);
            if (n <= 0) return 0;
            me.GetInventory().RemoveItem(name, n);
            ToServer(RpcHand, b.Id + "|" + n + "|" + b.Target);
            return n;
        }

        /// <summary>
        /// A player landed the last blow on something: tell the host, which counts it for every contract it fits. A creature only
        /// dies on the game that owns it (usually whoever loaded the area first), so that game reports it for whichever player
        /// killed it, with the killer's id so the host tells them the progress.
        /// </summary>
        internal void ReportKill(Character victim)
        {
            if (Player.m_localPlayer == null || victim == null || victim.IsPlayer() || victim.IsTamed()) return;
            ZNetView view = victim.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            HitData hit = LastHitField(victim);
            if (!(hit?.GetAttacker() is Player killer)) return;
            if (!_counted.Add(victim.GetInstanceID())) return; // death can be reported more than once
            if (_counted.Count > 200) _counted.Clear();
            ToServer(RpcKill, Utils.GetPrefabName(victim.gameObject) + "|" + (victim.GetLevel() >= 2 ? "1" : "0") + "|" +
                              killer.GetZDOID().UserID.ToString(CultureInfo.InvariantCulture));
        }

        private static readonly AccessTools.FieldRef<Character, HitData> LastHitField = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        // ---- helpers for the screen ----

        internal static bool IsReady(Bounty b, Player player) =>
            b.Kind == Kind.Gather ? player.GetInventory().CountItems(Rules.ItemName(b.Target)) > 0 : false;

        internal bool Claimable(Bounty done) => Player.m_localPlayer != null && !done.Claimed.Contains(Player.m_localPlayer.GetPlayerID()) &&
                                                (done.Earned == null || done.Earned.Contains(Player.m_localPlayer.GetPlayerID()));

        internal int ToClaim() => Current == null || Player.m_localPlayer == null ? 0 : Current.Done.Count(Claimable);
    }
}
