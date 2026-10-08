using System.Collections.Generic;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// The Drying Rack and the Curing Barrel. Use it with leaves in your bag to hang (or pack) up to a batch of them; a while later (game time)
    /// they are done, and using it again gives them back dried (rack) or aged (barrel). One strain per batch.
    ///
    /// Everything is kept on the piece (what went in, how many, when), so it carries on while you are away and everyone nearby sees the same.
    /// It uses the game's own hover text and use key, with no window of its own.
    /// </summary>
    public class Curer : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyCount = "dh_c_count", KeyStart = "dh_c_start", KeyIn = "dh_c_in";

        // set when the piece is built (Pieces.cs)
        public string Title, Doing, Done;                 // "Drying Rack", "Drying", "dried"
        public float Seconds;
        public int Capacity;
        public readonly List<KeyValuePair<string, string>> Conversions = new List<KeyValuePair<string, string>>();   // input item -> output item

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
        }

        private ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;

        private double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        private State Read(out int count, out float left, out string input)
        {
            count = 0; left = 0f; input = "";
            ZDO zdo = Zdo;
            if (zdo == null) return State.Empty;
            count = zdo.GetInt(KeyCount, 0);
            if (count <= 0) return State.Empty;
            input = zdo.GetString(KeyIn, "");
            double started = zdo.GetLong(KeyStart, 0L) / 1000.0;
            left = Mathf.Max(0f, (float)(started + Seconds - Now));
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
            State s = Read(out int count, out float left, out string input);
            if (s == State.Empty) return Load(user, null);
            if (s == State.Working)
            {
                user.Message(MessageHud.MessageType.Center, $"{Doing}: about {Mathf.CeilToInt(left / 60f)} min to go");
                return true;
            }
            return Collect(user, count, input);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
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
                    if (inv.CountItems(Things.SharedName(c.Key)) > 0) { input = c.Key; break; }
            if (input == null)
            {
                player.Message(MessageHud.MessageType.Center, "You have no leaves for the " + Title.ToLower());
                return true;
            }
            string shared = Things.SharedName(input);
            int n = Mathf.Min(inv.CountItems(shared), Capacity);
            inv.RemoveItem(shared, n);
            _nview.ClaimOwnership();
            ZDO zdo = _nview.GetZDO();
            zdo.Set(KeyIn, input);
            zdo.Set(KeyCount, n);
            zdo.Set(KeyStart, (long)(Now * 1000.0));
            player.Message(MessageHud.MessageType.Center, $"{Doing} {n} leaves");
            Things.PlayEffects(transform.position);
            return true;
        }

        private bool Collect(Humanoid user, int count, string input)
        {
            var player = user as Player;
            string output = OutputFor(input);
            if (player == null || Zdo == null || output.Length == 0) return false;
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(KeyCount, 0);
            Things.Give(player, output, count);
            player.Message(MessageHud.MessageType.Center, $"You take {count} {Things.DisplayName(output)}");
            Things.PlayEffects(transform.position);
            return true;
        }

        public string GetHoverText()
        {
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
}
