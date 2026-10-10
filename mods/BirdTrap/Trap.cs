using System.Linq;
using UnityEngine;

namespace BirdTrap
{
    /// <summary>
    /// The trap itself. Use it to put in bait (berries or seeds, one at a time; the alternate use fills it). Baited and under the open sky,
    /// a bird comes in a while (sooner or later, at random): it hops in for the bait, the prop falls and the door drops. Use it then to pluck
    /// the bird (two or three feathers) and let it go; the door goes back up, and with bait left the next bird is on its way.
    ///
    /// Everything is kept on the trap (how much bait, when the next bird comes, whether one is in), in game time, so birds come while you
    /// are away and everyone nearby sees the same trap. The game that owns the trap (usually the nearest player's) decides the catch, and
    /// it alone changes the trap: pressing E asks it (an RPC), and the feathers come only on its yes, so two players plucking one bird at
    /// once can't both get them.
    /// </summary>
    public class TrapPiece : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyBait = "bt_bait", KeyNext = "bt_next", KeyBird = "bt_bird", KeyWhy = "bt_why", KeyFrom = "bt_from";

        // Where the model's moving parts sit (tools/modelkit/birdtrap.py): the door raised when set, and lowered by Lift when it drops.
        private const float Lift = 0.3f;
        internal static readonly Vector3 PerchPos = new Vector3(0.16f, 0.85f, -0.295f);   // the bird on the door frame (build-menu picture)
        internal const float PerchYaw = 205f;

        /// <summary>What a bird will come for: berries and seeds, by item prefab name.</summary>
        private static readonly string[] Baits = { "Raspberry", "Blueberries", "Cloudberry", "BeechSeeds", "BirchSeeds", "Barley", "CarrotSeeds", "TurnipSeeds", "OnionSeeds" };

        internal static readonly System.Collections.Generic.HashSet<TrapPiece> All = new System.Collections.Generic.HashSet<TrapPiece>();

        private ZNetView _nview;
        private Transform _door, _prop, _bait, _bird, _birdBody;
        private Vector3 _doorUp, _birdHome;
        private Quaternion _birdTurn;
        private float _doorAt = 1f;          // 1 = up (set), 0 = down (sprung): where the door is drawn
        private bool _hadBird, _seen;
        private float _nextThink, _pluckAskedAt = -9f;
        private readonly System.Collections.Generic.List<string> _baitSent = new System.Collections.Generic.List<string>(); // bait on its way to the owner (given back if it doesn't fit)
        private float _baitSentAt;

        private const string RpcBait = "bt_Bait", RpcBaitTaken = "bt_BaitTaken", RpcPluck = "bt_Pluck", RpcPlucked = "bt_Plucked";
        private static readonly string[] Rpcs = { RpcBait, RpcBaitTaken, RpcPluck, RpcPlucked };

        private void OnDestroy()
        {
            All.Remove(this);
            if (_nview != null) foreach (string rpc in Rpcs) _nview.Unregister(rpc); // (a reloaded copy registers them again)
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (_nview != null && _nview.GetZDO() != null)
            {
                All.Add(this); // (not the prefab's copy)
                foreach (string rpc in Rpcs) _nview.Unregister(rpc); // an older copy's, left behind by a reload
                _nview.Register<string>(RpcBait, RPC_Bait);
                _nview.Register<int>(RpcBaitTaken, RPC_BaitTaken);
                _nview.Register(RpcPluck, RPC_Pluck);
                _nview.Register<int>(RpcPlucked, RPC_Plucked);
            }
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "door": _door = t; break;
                    case "prop": _prop = t; break;
                    case "bait": _bait = t; break;
                    case "bird": _bird = t; break;
                    case "birdBody": _birdBody = t; break;
                }
            }
            if (_door != null) _doorUp = _door.localPosition;
            if (_bird != null) { _birdHome = _bird.localPosition; _birdTurn = _bird.localRotation; }
        }

        private ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
        private int Bait => Zdo?.GetInt(KeyBait, 0) ?? 0;
        private bool HasBird => Zdo?.GetBool(KeyBird, false) ?? false;
        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        // ---- the catch (on the game that owns the trap) ----------------------------------------------------------------------------------

        private void Update()
        {
            ZDO zdo = Zdo;
            if (zdo == null) return;
            if (zdo.IsOwner() && Time.time >= _nextThink)
            {
                _nextThink = Time.time + 1f;
                Think(zdo);
            }
            Show(zdo);
        }

        private void Think(ZDO zdo)
        {
            if (zdo.GetBool(KeyBird, false)) return;               // sprung: waits for you
            int bait = zdo.GetInt(KeyBait, 0);
            double next = (double)zdo.GetLong(KeyNext, 0L);
            if (bait <= 0) { if (next != 0.0) zdo.Set(KeyNext, 0L); return; }
            if (next == 0.0) { zdo.Set(KeyNext, (long)(Now + Wait())); zdo.Set(KeyFrom, (long)Now); return; }   // just baited: a bird is on its way
            // Due: its time has come, or a night has gone by since it was set (a bird comes at first light: so after you sleep, every
            // trap that was set the evening before has its bird).
            bool overnight = EnvMan.instance != null && !EnvMan.IsNight() && EnvMan.instance.GetDay(Now) > EnvMan.instance.GetDay(zdo.GetLong(KeyFrom, 0L));
            if (Now < next && !overnight) return;

            // A bird is due. Not under a roof, and not at night (birds roost): it comes later.
            string why = null;
            if (Plugin.NeedsSky.Value && !OpenSky()) why = "roof";
            else if (Plugin.RestsAtNight.Value && EnvMan.instance != null && EnvMan.IsNight()) why = "night";
            if (why != null)
            {
                zdo.Set(KeyNext, (long)(Now + 60.0));
                if (zdo.GetString(KeyWhy, "") != why) zdo.Set(KeyWhy, why);
                return;
            }
            zdo.Set(KeyWhy, "");
            zdo.Set(KeyBait, bait - 1);   // it ate the bait it came for
            zdo.Set(KeyNext, 0L);
            zdo.Set(KeyBird, true);
        }

        /// <summary>Its state, for the Claude Tools "traps" command.</summary>
        internal Newtonsoft.Json.Linq.JObject Report(Vector3 from)
        {
            ZDO zdo = Zdo;
            var o = new Newtonsoft.Json.Linq.JObject
            {
                ["position"] = new Newtonsoft.Json.Linq.JArray(System.Math.Round(transform.position.x, 1), System.Math.Round(transform.position.y, 1), System.Math.Round(transform.position.z, 1)),
                ["distance"] = System.Math.Round(Vector3.Distance(transform.position, from), 1),
                ["openSky"] = OpenSky(),
            };
            if (zdo == null) return o;
            o["owner"] = zdo.IsOwner() ? "this game" : zdo.GetOwner().ToString();
            o["bait"] = zdo.GetInt(KeyBait, 0);
            o["bird"] = zdo.GetBool(KeyBird, false);
            long next = zdo.GetLong(KeyNext, 0L), from_ = zdo.GetLong(KeyFrom, 0L);
            o["nextInMinutes"] = next == 0L ? (Newtonsoft.Json.Linq.JToken)null : System.Math.Round((next - Now) / 60.0, 1);
            o["setOnDay"] = from_ == 0L || EnvMan.instance == null ? (Newtonsoft.Json.Linq.JToken)null : EnvMan.instance.GetDay(from_);
            o["why"] = zdo.GetString(KeyWhy, "");
            return o;
        }

        private static double Wait()
        {
            float lo = Mathf.Min(Plugin.MinMinutes.Value, Plugin.MaxMinutes.Value), hi = Mathf.Max(Plugin.MinMinutes.Value, Plugin.MaxMinutes.Value);
            return Random.Range(lo, hi) * 60.0;
        }

        /// <summary>Nothing overhead (a roof, a tree's canopy is fine: only built pieces count) for 40 m.</summary>
        private bool OpenSky()
        {
            int mask = LayerMask.GetMask("piece", "piece_nonsolid");
            foreach (RaycastHit hit in Physics.RaycastAll(transform.position + Vector3.up * 1.0f, Vector3.up, 40f, mask))
                if (hit.collider != null && !hit.collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        // ---- how it looks (on every game) -------------------------------------------------------------------------------------------------

        private void Show(ZDO zdo)
        {
            bool bird = zdo.GetBool(KeyBird, false);
            int bait = zdo.GetInt(KeyBait, 0);
            if (!_seen) { _seen = true; _hadBird = bird; _doorAt = bird ? 0f : 1f; }
            if (bird != _hadBird)
            {
                _hadBird = bird;
                Effect(bird ? Plugin.DoorShut : Plugin.DoorOpen);
            }
            // the door drops in a blink and is lifted back slowly
            _doorAt = bird ? Mathf.MoveTowards(_doorAt, 0f, Time.deltaTime / 0.12f) : Mathf.MoveTowards(_doorAt, 1f, Time.deltaTime / 0.7f);
            if (_door != null) _door.localPosition = _doorUp - new Vector3(0f, Lift * (1f - _doorAt), 0f);
            if (_prop != null && _prop.gameObject.activeSelf != (!bird && _doorAt > 0.99f)) _prop.gameObject.SetActive(!bird && _doorAt > 0.99f);
            if (_bait != null && _bait.gameObject.activeSelf != (bait > 0)) _bait.gameObject.SetActive(bait > 0);
            if (_bird != null)
            {
                if (_bird.gameObject.activeSelf != bird) _bird.gameObject.SetActive(bird);
                if (bird && _birdBody != null)
                {
                    // it hops about a little and turns its head this way and that
                    float t = Time.time + GetInstanceID() * 0.37f;
                    float hop = Mathf.Max(0f, Mathf.Sin(t * 2.3f)) * Mathf.Max(0f, Mathf.Sin(t * 0.7f)) * 0.025f;
                    _birdBody.localPosition = new Vector3(0f, hop, 0f);
                    _bird.localRotation = _birdTurn * Quaternion.Euler(0f, Mathf.Sin(t * 0.9f) * 25f, 0f);
                    _birdBody.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(t * 3.1f + 1f)) * 18f, 0f, 0f); // a peck now and then
                }
            }
        }

        private void Effect(EffectList fx)
        {
            try { fx?.Create(transform.position + Vector3.up * 0.4f, transform.rotation); }
            catch { /* effects are only for show */ }
        }

        // ---- using it -------------------------------------------------------------------------------------------------------------------

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || player != Player.m_localPlayer) return false;
            if (!PrivateArea.CheckAccess(transform.position)) return true;
            ZDO zdo = Zdo;
            if (zdo == null) return false;

            // the trap's owner makes the change (whoever presses E asks it): see RPC_Pluck and RPC_Bait
            if (zdo.GetBool(KeyBird, false))
            {
                if (Time.time - _pluckAskedAt > 1f) { _pluckAskedAt = Time.time; _nview.InvokeRPC(RpcPluck); } // (once: the answer is on its way)
                return true;
            }

            int bait = zdo.GetInt(KeyBait, 0), max = Plugin.MaxBait.Value;
            if (bait >= max) { player.Message(MessageHud.MessageType.Center, "It holds all the bait it can"); return true; }
            if (_baitSent.Count > 0 && Time.time - _baitSentAt < 5f) return true; // (still waiting for the owner's answer)
            _baitSent.Clear(); // (no answer came: the owner left, and that bait went with it, as the game's own fermenter does)
            Inventory inv = player.GetInventory();
            var sent = new System.Collections.Generic.List<string>();
            while (bait + sent.Count < max)
            {
                ItemDrop.ItemData food = inv.GetAllItems().FirstOrDefault(IsBait);
                if (food == null) break;
                sent.Add(food.m_dropPrefab.name);
                inv.RemoveItem(food, 1);
                if (!alt) break; // one at a time; the alternate use fills it
            }
            if (sent.Count == 0) { player.Message(MessageHud.MessageType.Center, "You need berries or seeds for bait"); return true; }
            _baitSent.AddRange(sent);
            _baitSentAt = Time.time;
            _nview.InvokeRPC(RpcBait, string.Join(",", sent.ToArray()));
            return true;
        }

        // ---- the owner's answers --------------------------------------------------------------------------------------------------------

        /// <summary>The owner: someone puts bait in. It takes what fits and says how many (the rest goes back to them).</summary>
        private void RPC_Bait(long sender, string items)
        {
            ZDO zdo = Zdo;
            if (zdo == null || !_nview.IsOwner()) { _nview.InvokeRPC(sender, RpcBaitTaken, 0); return; } // (not ours any more: they get it all back)
            int offered = (items ?? "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
            int bait = zdo.GetInt(KeyBait, 0);
            int taken = Mathf.Clamp(Plugin.MaxBait.Value - bait, 0, offered);
            if (zdo.GetBool(KeyBird, false)) taken = 0; // (a bird is in: pluck it first)
            if (taken > 0) zdo.Set(KeyBait, bait + taken);
            _nview.InvokeRPC(sender, RpcBaitTaken, taken);
        }

        /// <summary>The player who baited it: the owner took this many; what it didn't take comes back.</summary>
        private void RPC_BaitTaken(long sender, int taken)
        {
            Player player = Player.m_localPlayer;
            if (player == null || _baitSent.Count == 0) return;
            taken = Mathf.Clamp(taken, 0, _baitSent.Count);
            string used = _baitSent[0];
            for (int i = taken; i < _baitSent.Count; i++)
            {
                GameObject back = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(_baitSent[i]) : null;
                if (back == null) continue;
                if (player.GetInventory().CanAddItem(back, 1)) player.GetInventory().AddItem(back, 1);
                else Instantiate(back, player.transform.position + Vector3.up, Quaternion.identity);
            }
            _baitSent.Clear();
            ZDO zdo = Zdo;
            int now = zdo != null ? zdo.GetInt(KeyBait, 0) : taken, max = Plugin.MaxBait.Value;
            GameObject usedPrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(used) : null;
            string name = usedPrefab != null ? Localization.instance.Localize(usedPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToLowerInvariant() : used;
            player.Message(MessageHud.MessageType.Center, taken > 0 ? $"Baited with {name} ({Mathf.Max(now, taken)}/{max})" : "It holds all the bait it can");
        }

        /// <summary>The owner: someone plucks the bird. Only the first asker gets it (the bird is gone before the answer goes out).</summary>
        private void RPC_Pluck(long sender)
        {
            ZDO zdo = Zdo;
            if (zdo == null || !_nview.IsOwner() || !zdo.GetBool(KeyBird, false)) { _nview.InvokeRPC(sender, RpcPlucked, 0); return; }
            zdo.Set(KeyBird, false);
            int n = Random.Range(Mathf.Min(Plugin.MinFeathers.Value, Plugin.MaxFeathers.Value), Mathf.Max(Plugin.MinFeathers.Value, Plugin.MaxFeathers.Value) + 1);
            _nview.InvokeRPC(sender, RpcPlucked, Mathf.Max(1, n));
        }

        /// <summary>The player who plucked it: the owner's answer (0: someone else got to it first).</summary>
        private void RPC_Plucked(long sender, int n)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            if (n <= 0) { player.Message(MessageHud.MessageType.Center, "The bird is already gone"); return; }
            Pluck(player, n);
        }

        /// <summary>Using an item from the hotbar on it: bait goes in (as the use key does with bait you carry).</summary>
        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!IsBait(item) || !(user is Player player) || player != Player.m_localPlayer || HasBird) return false;
            return Interact(user, false, false);
        }

        public string GetHoverName() => "Bird Trap";
        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            ZDO zdo = Zdo;
            if (zdo == null) return "Bird Trap";
            int bait = zdo.GetInt(KeyBait, 0), max = Plugin.MaxBait.Value;
            string text = "Bird Trap";
            if (zdo.GetBool(KeyBird, false))
                text += "\n<color=#9BE37A>A gull is caught!</color>\n[<color=yellow><b>$KEY_Use</b></color>] Pluck it for feathers and let it go";
            else
            {
                string state;
                if (bait <= 0) state = "No bait: birds won't come to an empty trap";
                else
                {
                    string why = zdo.GetString(KeyWhy, "");
                    double left = (double)zdo.GetLong(KeyNext, 0L) - (ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0);
                    state = why == "roof" ? "<color=#E8B04B>Birds won't come under a roof</color>"
                          : why == "night" ? "Birds are roosting for the night: one will come in the morning"
                          : left > 60.0 ? $"Set. A bird should come in about {Mathf.CeilToInt((float)(left / 60.0))} min"
                          : "Set. A bird should come any moment";
                }
                text += $"\nBait: {bait}/{max}  ·  {state}";
                if (bait < max)
                    text += "\n[<color=yellow><b>$KEY_Use</b></color>] Add bait (berries or seeds)\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Fill it with bait";
            }
            return Localization.instance.Localize(text);
        }

        private static bool IsBait(ItemDrop.ItemData i) => i?.m_dropPrefab != null && Baits.Contains(i.m_dropPrefab.name);

        /// <summary>The feathers the owner said yes to, into your bag (or at your feet), and the bird flies off.</summary>
        private void Pluck(Player player, int n)
        {
            GameObject feathers = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Feathers") : null;
            if (feathers != null)
            {
                Inventory inv = player.GetInventory();
                if (inv.CanAddItem(feathers, n)) inv.AddItem(feathers, n);
                else Instantiate(feathers, transform.position + Vector3.up * 0.9f, Quaternion.identity).GetComponent<ItemDrop>().SetStack(n); // full: at your feet
            }
            player.Message(MessageHud.MessageType.Center, $"You pluck the gull and let it go: {n} feathers");
            FlyAway();
        }

        /// <summary>A copy of the bird flutters up out of the trap and away (on this game, just for show).</summary>
        private void FlyAway()
        {
            if (_bird == null) return;
            GameObject copy = Instantiate(_bird.gameObject, _bird.parent);
            copy.SetActive(true);
            copy.transform.localPosition = _birdHome + Vector3.up * 0.5f;
            copy.AddComponent<Flutter>();
        }
    }

    /// <summary>The let-go bird: up and away over a couple of seconds, flapping, then gone.</summary>
    internal class Flutter : MonoBehaviour
    {
        private float _t;
        private Vector3 _dir;
        private Transform _wingL, _wingR;

        private void Start()
        {
            _dir = (transform.parent != null ? transform.parent.rotation : Quaternion.identity) * new Vector3(Random.Range(-0.4f, 0.4f), 0f, -1f);
            foreach (Transform t in GetComponentsInChildren<Transform>()) if (t.name == "wing") { if (_wingL == null) _wingL = t; else _wingR = t; }
            transform.SetParent(null, true);
        }

        private void Update()
        {
            _t += Time.deltaTime;
            transform.position += (_dir.normalized * 3.5f + Vector3.up * (2.5f + _t * 2f)) * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(-_dir.normalized, Vector3.up) * Quaternion.Euler(15f, 0f, 0f); // (its beak is its -z: towards where it flies, nose up)
            float flap = Mathf.Sin(_t * 40f) * 60f;
            if (_wingL != null) _wingL.localRotation = Quaternion.Euler(-14f, 0f, flap);
            if (_wingR != null) _wingR.localRotation = Quaternion.Euler(-14f, 0f, -flap);
            if (_t > 2.5f) Destroy(gameObject);
        }
    }
}
