using System;
using System.Collections.Generic;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// The Drying Rack and the Curing Barrel. Use it with leaves in your bag to hang (or pack) up to a batch of them; a while later (game time)
    /// they are done, and using it again gives them back dried (rack) or aged (barrel). One strain per batch.
    ///
    /// Everything is kept on the piece (what went in, how many, when it is done), so it carries on while you are away and everyone nearby
    /// sees the same. It uses the game's own hover text and use key, with no window of its own.
    ///
    /// Only the piece's owner changes what is in it, as the game's Fermenter does: putting leaves in and taking them out are requests to the
    /// owner, who checks it is still empty (or ready), changes it, and sends the leaves to whoever asked. Two players using it at once can
    /// then neither both take the batch nor lose one's leaves under the other's.
    /// </summary>
    public class Curer : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyCount = "dh_c_count", KeyStart = "dh_c_start", KeyEnd = "dh_c_end", KeyIn = "dh_c_in";
        private const string RpcLoad = "dh_Curer_Load", RpcCollect = "dh_Curer_Collect", RpcGive = "dh_Curer_Give";

        // set when the piece is built (Pieces.cs)
        public string Title, Doing, Done;                 // "Drying Rack", "Drying", "dried"
        public int Capacity;
        public bool Drying;                               // the rack (fresh -> dried) or the barrel (dried -> aged)

        // input item -> output item. Worked out here and not stored as a field: the game builds a placed piece from the prefab, and a list
        // filled in by code is not copied (Unity only copies what it can save), so a placed rack or barrel would start with an empty one.
        private List<KeyValuePair<string, string>> _conversions;
        private List<KeyValuePair<string, string>> Conversions
        {
            get
            {
                if (_conversions == null)
                {
                    _conversions = new List<KeyValuePair<string, string>>();
                    foreach (Strain s in Strains.All)
                        _conversions.Add(Drying ? new KeyValuePair<string, string>(s.Fresh, s.Dried) : new KeyValuePair<string, string>(s.Dried, s.Aged));
                }
                return _conversions;
            }
        }

        private ZNetView _nview;
        private Transform _fresh, _dry, _lid, _contents;
        private float _nextLook;

        private enum State { Empty, Working, Ready }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "fresh") _fresh = t;
                else if (t.name == "dry") _dry = t;
                else if (t.name == "lidOn") _lid = t;
                else if (t.name == "contents") _contents = t;
            }
            if (_nview == null || _nview.GetZDO() == null) return;   // (the ghost shown while placing it)
            _nview.Register<string, int>(RpcLoad, RPC_Load);
            _nview.Register(RpcCollect, RPC_Collect);
            _nview.Register<string, int, bool>(RpcGive, RPC_Give);
            WearNTear wear = GetComponent<WearNTear>();
            if (wear != null) wear.m_onDestroyed += OnDestroyed;
        }

        private ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;

        private double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        /// <summary>How long a batch takes, from the settings (the server's in multiplayer) when it is put in.</summary>
        private float Duration => (Drying ? Plugin.DryMinutes.Value : Plugin.CureMinutes.Value) * 60f;

        /// <summary>Something is in it (working or ready): it can't be torn down until it is emptied.</summary>
        internal bool Loaded => Read(out _, out _, out _) != State.Empty;

        private State Read(out int count, out float left, out string input)
        {
            count = 0; left = 0f; input = "";
            ZDO zdo = Zdo;
            if (zdo == null) return State.Empty;
            count = zdo.GetInt(KeyCount, 0);
            if (count <= 0) return State.Empty;
            input = zdo.GetString(KeyIn, "");
            // the end time is saved with the batch, so every game agrees on it; a batch put in before 0.3.0 has only its start
            long end = zdo.GetLong(KeyEnd, 0L);
            double ends = end > 0L ? end / 1000.0 : zdo.GetLong(KeyStart, 0L) / 1000.0 + Duration;
            left = Mathf.Max(0f, (float)(ends - Now));
            return left > 0f ? State.Working : State.Ready;
        }

        private string OutputFor(string input)
        {
            foreach (KeyValuePair<string, string> c in Conversions) if (c.Key == input) return c.Value;
            return "";
        }

        private void Update()
        {
            if (Time.time < _nextLook) return;
            _nextLook = Time.time + 0.5f;
            State s = Read(out _, out _, out _);
            if (_fresh != null) _fresh.gameObject.SetActive(s == State.Working);
            if (_dry != null) _dry.gameObject.SetActive(s == State.Ready);
            if (_lid != null) _lid.gameObject.SetActive(s != State.Ready);
            if (_contents != null) _contents.gameObject.SetActive(s == State.Ready);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!PrivateArea.CheckAccess(transform.position)) return true;   // in someone else's ward
            State s = Read(out int count, out float left, out string input);
            if (s == State.Empty) return Load(user, null);
            if (s == State.Working)
            {
                user.Message(MessageHud.MessageType.Center, $"{Doing}: about {Mathf.CeilToInt(left / 60f)} min to go");
                return true;
            }
            if (!(user is Player) || Zdo == null) return false;
            _nview.InvokeRPC(RpcCollect);   // the owner hands the batch out, once
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!PrivateArea.CheckAccess(transform.position)) return false;
            if (Read(out _, out _, out _) != State.Empty || item == null) return false;
            foreach (KeyValuePair<string, string> c in Conversions)
                if (Things.SharedName(c.Key) == item.m_shared.m_name) return Load(user, c.Key);
            return false;
        }

        /// <summary>Takes up to a batch of one kind of leaf out of the player's bag (the one asked for, else the first kind they carry).</summary>
        private bool Load(Humanoid user, string input)
        {
            var player = user as Player;
            if (player == null || Zdo == null) return false;
            Inventory inv = player.GetInventory();
            if (input == null)
                foreach (KeyValuePair<string, string> c in Conversions)
                    if (inv.CountItems(Things.SharedName(c.Key), -1, false) > 0) { input = c.Key; break; }
            if (input == null)
            {
                player.Message(MessageHud.MessageType.Center, "You have no leaves for the " + Title.ToLower());
                return true;
            }
            string shared = Things.SharedName(input);
            int n = Mathf.Min(inv.CountItems(shared, -1, false), Capacity);   // leaves of any world level
            inv.RemoveItem(shared, n, -1, false);
            _nview.InvokeRPC(RpcLoad, input, n);   // the owner puts them in, or sends them back if someone was quicker
            player.Message(MessageHud.MessageType.Center, $"{Doing} {n} leaves");
            Things.PlayEffects(transform.position);
            return true;
        }

        // ---- on the owner's game ----

        private bool IsInput(string prefab)
        {
            foreach (KeyValuePair<string, string> c in Conversions) if (c.Key == prefab) return true;
            return false;
        }

        private bool IsLeaf(string prefab)
        {
            foreach (KeyValuePair<string, string> c in Conversions) if (c.Key == prefab || c.Value == prefab) return true;
            return false;
        }

        private void RPC_Load(long sender, string input, int n)
        {
            try
            {
                if (n <= 0 || !IsInput(input)) return;
                // not the owner any more, already in use, or more than fits: the leaves go back to whoever sent them, so nothing is lost
                if (!_nview.IsOwner() || Read(out _, out _, out _) != State.Empty || n > Capacity)
                {
                    _nview.InvokeRPC(sender, RpcGive, input, n, true);
                    return;
                }
                ZDO zdo = _nview.GetZDO();
                double now = Now;
                zdo.Set(KeyIn, input);
                zdo.Set(KeyCount, n);
                zdo.Set(KeyStart, (long)(now * 1000.0));
                zdo.Set(KeyEnd, (long)((now + Duration) * 1000.0));
            }
            catch (Exception e) { Debug.LogWarning("[" + Plugin.Name + "] " + Title + ": could not put leaves in: " + e); }
        }

        private void RPC_Collect(long sender)
        {
            try
            {
                if (!_nview.IsOwner() || Read(out int count, out _, out string input) != State.Ready) return;   // (someone else just took it)
                string output = OutputFor(input);
                if (output.Length == 0) return;
                ZDO zdo = _nview.GetZDO();
                zdo.Set(KeyCount, 0);
                zdo.Set(KeyEnd, 0L);
                _nview.InvokeRPC(sender, RpcGive, output, count, false);
            }
            catch (Exception e) { Debug.LogWarning("[" + Plugin.Name + "] " + Title + ": could not hand the leaves out: " + e); }
        }

        /// <summary>Broken or torn down with something in it: what it held lands on the ground (as a chest's contents do).</summary>
        private void OnDestroyed()
        {
            try
            {
                if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
                State s = Read(out int count, out _, out string input);
                if (s == State.Empty) return;
                string what = s == State.Ready ? OutputFor(input) : input;
                if (what.Length > 0) Things.Drop(what, Mathf.Min(count, Capacity), transform.position + Vector3.up);
                _nview.GetZDO().Set(KeyCount, 0);
            }
            catch (Exception e) { Debug.LogWarning("[" + Plugin.Name + "] " + Title + ": could not drop what it held: " + e); }
        }

        // ---- on the game of the player who asked ----

        private void RPC_Give(long sender, string prefab, int count, bool returned)
        {
            try
            {
                if (count <= 0 || !IsLeaf(prefab)) return;   // only our own leaves
                count = Mathf.Min(count, Capacity);
                Player player = Player.m_localPlayer;
                if (player == null) { Things.Drop(prefab, count, transform.position + Vector3.up); return; }   // (died meanwhile)
                Things.Give(player, prefab, count);
                if (returned) player.Message(MessageHud.MessageType.Center, $"The {Title.ToLower()} is already in use: your leaves are back");
                else
                {
                    player.Message(MessageHud.MessageType.Center, $"You take {count} {Things.DisplayName(prefab)}");
                    Things.PlayEffects(transform.position);
                }
            }
            catch (Exception e) { Debug.LogWarning("[" + Plugin.Name + "] " + Title + ": could not take the leaves: " + e); }
        }

        public string GetHoverText()
        {
            if (!PrivateArea.CheckAccess(transform.position, 0f, false)) return Localization.instance.Localize(Title + "\n$piece_noaccess");
            State s = Read(out int count, out float left, out string input);
            string text;
            if (s == State.Empty) text = $"{Title}\n[<color=yellow><b>$KEY_Use</b></color>] Put in leaves (up to {Capacity}) from your bag";
            else if (s == State.Working) text = $"{Title}\n{Doing} {count} {Things.DisplayName(input)}: about {Mathf.CeilToInt(left / 60f)} min to go";
            else text = $"{Title}\n[<color=yellow><b>$KEY_Use</b></color>] Take {count} {Things.DisplayName(OutputFor(input))}";
            return Localization.instance.Localize(text);
        }

        public string GetHoverName() => Title;
        public float GetHoverOffset() => 0f;
    }

    // A rack or barrel with leaves in it can't be torn down with the hammer (the game says "can't remove now"), as a chest that holds
    // something can't: the rack and barrel are copies of a chest without its Container, which is what the game asks.
    [HarmonyLib.HarmonyPatch(typeof(Piece), nameof(Piece.CanBeRemoved))]
    internal static class Piece_CanBeRemoved
    {
        private static void Postfix(Piece __instance, ref bool __result)
        {
            if (!__result) return;
            Curer curer = __instance.GetComponent<Curer>();
            if (curer != null && curer.Loaded) __result = false;
        }
    }
}
