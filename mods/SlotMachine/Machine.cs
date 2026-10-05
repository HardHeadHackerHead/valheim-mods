using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlotMachine
{
    /// <summary>
    /// The slot machine itself. Press the use key to pull the lever: your bet goes in, the reels spin and stop one after another, and a win
    /// is spat out of the tray as coins. The spin is stored on the machine (what each reel lands on, and when it started), so everyone
    /// nearby watches the same spin.
    /// </summary>
    public class SlotMachinePiece : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyStart = "sm_t", KeyCells = "sm_r", KeyPay = "sm_w", KeyWho = "sm_who", KeyBet = "sm_bet", KeyPaid = "sm_paid";
        private static readonly float[] StopAt = { 1.7f, 2.4f, 3.1f };   // seconds after the pull that each reel comes to rest
        private const float Total = 3.6f, LeverTime = 0.7f;

        private ZNetView _nview;
        private readonly Transform[] _reels = new Transform[3];
        private Transform _lever;
        private readonly List<Renderer> _bulbsA = new List<Renderer>(), _bulbsB = new List<Renderer>();
        private int[] _shown = { 0, 0, 0 };
        private long _spinSeen;
        private readonly System.Random _rng = new System.Random();

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "reel0") _reels[0] = t;
                else if (t.name == "reel1") _reels[1] = t;
                else if (t.name == "reel2") _reels[2] = t;
                else if (t.name == "lever") _lever = t;
                else if (t.name == "bulbA") { Renderer r = t.GetComponent<Renderer>(); if (r != null) _bulbsA.Add(r); }
                else if (t.name == "bulbB") { Renderer r = t.GetComponent<Renderer>(); if (r != null) _bulbsB.Add(r); }
            }
        }

        // ---- the stored spin ----

        private ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
        private static long NowMs => ZNet.instance != null ? (long)(ZNet.instance.GetTimeSeconds() * 1000.0) : 0L;

        private bool Spinning
        {
            get { ZDO z = Zdo; return z != null && NowMs - z.GetLong(KeyStart, 0L) < (long)(Total * 1000f) + 200L; }
        }

        private static int[] ParseCells(string s)
        {
            string[] f = (s ?? "").Split(',');
            var cells = new int[3];
            for (int i = 0; i < 3; i++) if (f.Length > i && int.TryParse(f[i], out int v)) cells[i] = Mathf.Clamp(v, 0, 7);
            return cells;
        }

        // ---- using it ----

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            var player = (Player)user;
            if (alt)
            {
                Plugin.NextBet();
                player.Message(MessageHud.MessageType.TopLeft, "Bet: " + Plugin.Bet.Value + " coins");
                return true;
            }
            Pull(player);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private void Pull(Player player)
        {
            ZDO zdo = Zdo;
            if (zdo == null) return;
            if (Spinning) { player.Message(MessageHud.MessageType.Center, "The reels are still turning"); return; }

            int bet = Plugin.Bet.Value;
            if (player.GetInventory().CountItems("$item_coins") < bet)
            {
                player.Message(MessageHud.MessageType.Center, "You need " + bet + " coins to play");
                return;
            }
            player.GetInventory().RemoveItem("$item_coins", bet);

            int[] cells = Payout.Roll(_rng);
            int pay = Mathf.RoundToInt(Payout.Multiplier(cells) * bet * Plugin.PayoutPercent.Value / 100f);

            _nview.ClaimOwnership();
            zdo.Set(KeyCells, cells[0] + "," + cells[1] + "," + cells[2]);
            zdo.Set(KeyPay, pay);
            zdo.Set(KeyBet, bet);
            zdo.Set(KeyWho, ZDOMan.GetSessionID());
            zdo.Set(KeyStart, NowMs);
            Effect(false);
        }

        public string GetHoverText()
        {
            string bet = Plugin.Bet.Value.ToString();
            return Localization.instance.Localize(
                $"Odin's Fortune\n[<color=yellow><b>$KEY_Use</b></color>] Pull the lever ({bet} coins)\n[<color=yellow><b>$KEY_AltPlace</b></color> + <color=yellow><b>$KEY_Use</b></color>] Change the bet\n<size=13>{Payout.Summary()}</size>");
        }

        public string GetHoverName() => "Odin's Fortune";
        public float GetHoverOffset() => 0f;

        // ---- the show ----

        private void Update()
        {
            ZDO zdo = Zdo;
            if (zdo == null) return;

            long start = zdo.GetLong(KeyStart, 0L);
            float t = start == 0L ? 999f : (NowMs - start) / 1000f;
            int[] target = ParseCells(zdo.GetString(KeyCells, "0,0,0"));

            for (int i = 0; i < 3; i++)
            {
                if (_reels[i] == null) continue;
                float stop = StopAt[i];
                float angle = -45f * target[i];
                if (t >= 0f && t < stop) angle += 360f * (5 + i) * (1f - Ease(t / stop));
                _reels[i].localRotation = Quaternion.Euler(angle, 0f, 0f);
            }
            _shown = target;

            if (_lever != null)
            {
                float pull = t < 0f || t > LeverTime ? 0f : t < 0.25f ? t / 0.25f : 1f - (t - 0.25f) / (LeverTime - 0.25f);
                _lever.localRotation = Quaternion.Euler(-62f * Mathf.SmoothStep(0f, 1f, pull), 0f, 0f);
            }

            Lamps(t, zdo.GetInt(KeyPay, 0) > 0);
            Settle(zdo, start, t, target);
        }

        private static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

        /// <summary>The lamps chase each other while the reels turn and flash after a win; otherwise they pulse slowly.</summary>
        private void Lamps(float t, bool won)
        {
            bool spinning = t >= 0f && t < Total;
            bool celebrating = won && t >= Total && t < Total + 5f;
            float rate = spinning ? 0.12f : celebrating ? 0.18f : 0.9f;
            bool a = Mathf.FloorToInt(Time.time / rate) % 2 == 0;
            foreach (Renderer r in _bulbsA) if (r != null) r.enabled = celebrating ? true : a;
            foreach (Renderer r in _bulbsB) if (r != null) r.enabled = celebrating ? a : !a;
        }

        /// <summary>When the spin ends, the player who pulled gets the result: coins spat out of the tray, and a message.</summary>
        private void Settle(ZDO zdo, long start, float t, int[] cells)
        {
            if (start == 0L || t < Total || _spinSeen == start) return;
            _spinSeen = start;
            if (t > Total + 8f) return; // an old spin we only just walked up to

            if (zdo.GetLong(KeyWho, 0L) != ZDOMan.GetSessionID() || zdo.GetLong(KeyPaid, 0L) == start) return;
            _nview.ClaimOwnership();
            zdo.Set(KeyPaid, start);

            int pay = zdo.GetInt(KeyPay, 0), bet = zdo.GetInt(KeyBet, 0);
            Player me = Player.m_localPlayer;
            if (pay <= 0)
            {
                if (me != null) me.Message(MessageHud.MessageType.TopLeft, "No luck. Try again?");
                return;
            }
            bool jackpot = Payout.IsJackpot(cells);
            if (me != null) me.Message(MessageHud.MessageType.Center, jackpot ? "JACKPOT! " + pay + " coins" : "You win " + pay + " coins!");
            Effect(true);
            StartCoroutine(Spit(pay));
        }

        /// <summary>Coins come out of the tray one stack at a time, so a big win pours out.</summary>
        private IEnumerator Spit(int coins)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Coins") : null;
            if (prefab == null) yield break;
            int max = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            while (coins > 0)
            {
                int n = Mathf.Min(coins, max);
                coins -= n;
                Vector3 from = transform.TransformPoint(new Vector3(0f, 0.36f, -0.5f) * ModelData.Scale);
                GameObject drop = Instantiate(prefab, from, Quaternion.identity);
                ItemDrop item = drop.GetComponent<ItemDrop>();
                item.SetStack(n);
                Rigidbody body = drop.GetComponent<Rigidbody>();
                if (body != null)
                    body.velocity = -transform.forward * Random.Range(1.2f, 1.8f) + Vector3.up * Random.Range(1.0f, 1.8f) + transform.right * Random.Range(-0.5f, 0.5f);
                Effect(false);
                yield return new WaitForSeconds(0.08f);
            }
        }

        /// <summary>The game's crafting sounds (a clunk when the lever is pulled, a chime on a win).</summary>
        private void Effect(bool done)
        {
            try
            {
                GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_workbench") : null;
                CraftingStation station = go != null ? go.GetComponent<CraftingStation>() : null;
                if (station != null) (done ? station.m_craftItemDoneEffects : station.m_craftItemEffects).Create(transform.position + Vector3.up, Quaternion.identity);
            }
            catch { /* sounds are optional */ }
        }
    }
}
