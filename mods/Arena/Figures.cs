using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// People made from the game's own viking body (the Player prefab, as the main menu shows your character): the Arena Master, the crowd and
    /// the statues of the Hall of Fame. Each is a local copy that does not join the network, with everything that makes it *the* player taken
    /// off; the game's own equipment code dresses it (real armour, capes, helmets, hair and beards) and its animator moves it (idle, and the
    /// game's emotes: cheer, roar, toast, dance, sit, point...).
    /// </summary>
    internal static class Figures
    {
        internal sealed class Look
        {
            public int Model;                       // 0 a man, 1 a woman
            public Color Skin = new Color(0.9f, 0.75f, 0.62f), Hair = new Color(0.45f, 0.3f, 0.15f);
            public string HairItem, Beard, Chest, Legs, Helmet, Cape, Right, Left, Utility;
        }

        private static readonly string[] Chests = { "ArmorRagsChest", "ArmorLeatherChest", "ArmorTrollLeatherChest", "ArmorBronzeChest", "ArmorPaddedCuirass", "ArmorFenringChest", "ArmorRootChest", "ArmorWolfChest" };
        private static readonly string[] Legs = { "ArmorRagsLegs", "ArmorLeatherLegs", "ArmorTrollLeatherLegs", "ArmorBronzeLegs", "ArmorPaddedGreaves", "ArmorFenringLegs", "ArmorRootLegs", "ArmorWolfLegs" };
        private static readonly string[] Capes = { null, null, "CapeDeerHide", "CapeTrollHide", "CapeLinen", "CapeLox", "CapeWolf" };
        private static readonly string[] Helmets = { null, null, null, "HelmetLeather", "HelmetBronze", "HelmetTrollLeather", "HelmetIron", "HelmetPadded" };
        private static readonly string[] Holding = { null, null, null, "Tankard", "Torch", "TankardAnniversary", "Club" };

        private static int Hash(string item) => string.IsNullOrEmpty(item) ? 0 : item.GetStableHashCode();
        private static bool Has(string item) => !string.IsNullOrEmpty(item) && ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(item) != null;

        /// <summary>A viking from the crowd: a man or a woman, in what a farmer or a fighter might wear, some with a drink or a torch.</summary>
        internal static Look Spectator(System.Random rng)
        {
            bool woman = rng.NextDouble() < 0.35;
            int gear = rng.Next(Chests.Length);
            var look = new Look
            {
                Model = woman ? 1 : 0,
                Skin = Color.Lerp(new Color(0.95f, 0.8f, 0.68f), new Color(0.55f, 0.4f, 0.3f), (float)rng.NextDouble() * 0.7f),
                Hair = new[] { new Color(0.85f, 0.7f, 0.4f), new Color(0.4f, 0.25f, 0.12f), new Color(0.65f, 0.28f, 0.1f), new Color(0.15f, 0.12f, 0.1f), new Color(0.7f, 0.7f, 0.68f) }[rng.Next(5)],
                HairItem = "Hair" + (1 + rng.Next(woman ? 20 : 14)),
                Beard = woman ? null : rng.NextDouble() < 0.8 ? "Beard" + (1 + rng.Next(12)) : null,
                Chest = Chests[gear], Legs = Legs[Mathf.Clamp(gear + rng.Next(-1, 2), 0, Legs.Length - 1)],
                Cape = Capes[rng.Next(Capes.Length)], Helmet = rng.NextDouble() < 0.3 ? Helmets[rng.Next(Helmets.Length)] : null,
                Right = Holding[rng.Next(Holding.Length)],
            };
            return look;
        }

        /// <summary>The Arena Master: a big grey-bearded viking in a padded coat and a wolf cape, a tankard in his hand.</summary>
        internal static Look Master() => new Look
        {
            Model = 0, Skin = new Color(0.86f, 0.68f, 0.55f), Hair = new Color(0.72f, 0.7f, 0.66f),
            HairItem = "Hair5", Beard = "Beard9", Chest = "ArmorPaddedCuirass", Legs = "ArmorPaddedGreaves", Cape = "CapeWolf", Helmet = null, Right = "TankardOdin", Utility = "BeltStrength",
        };

        /// <summary>
        /// How a player looks now, written short ("m=0;h=Hair5;b=Beard3;c=ArmorIronChest;..."), to be kept with their place in the Hall of
        /// Champions: their body, hair and beard, and what they had on and in hand.
        /// </summary>
        internal static string LookOf(Player p)
        {
            if (p == null) return "";
            var parts = new List<string> { "m=" + p.GetPlayerModel(), "h=" + p.GetHair(), "b=" + p.GetBeard() };
            foreach (ItemDrop.ItemData item in p.GetInventory().GetEquippedItems())
            {
                if (item?.m_dropPrefab == null) continue;
                string name = item.m_dropPrefab.name;
                switch (item.m_shared.m_itemType)
                {
                    case ItemDrop.ItemData.ItemType.Chest: parts.Add("c=" + name); break;
                    case ItemDrop.ItemData.ItemType.Legs: parts.Add("l=" + name); break;
                    case ItemDrop.ItemData.ItemType.Helmet: parts.Add("hm=" + name); break;
                    case ItemDrop.ItemData.ItemType.Utility: parts.Add("u=" + name); break;
                    case ItemDrop.ItemData.ItemType.Shield:
                    case ItemDrop.ItemData.ItemType.Bow: parts.Add("lf=" + name); break;
                    case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                    case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                    case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    case ItemDrop.ItemData.ItemType.Torch:
                    case ItemDrop.ItemData.ItemType.Tool: parts.Add("r=" + name); break;
                }
            }
            return string.Join(";", parts).Replace("|", "").Replace("\n", "");
        }

        /// <summary>A look written by LookOf, as a figure to make (null when there is none).</summary>
        internal static Look FromLook(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var look = new Look();
            foreach (string kv in text.Split(';'))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                string k = kv.Substring(0, eq), v = kv.Substring(eq + 1);
                if (v.Length == 0) continue;
                switch (k)
                {
                    case "m": int.TryParse(v, out look.Model); break;
                    case "h": look.HairItem = v; break;
                    case "b": look.Beard = v; break;
                    case "c": look.Chest = v; break;
                    case "l": look.Legs = v; break;
                    case "hm": look.Helmet = v; break;
                    case "u": look.Utility = v; break;
                    case "r": look.Right = v; break;
                    case "lf": look.Left = v; break;
                }
            }
            return look;   // (no cape even if they wore one: cloth cannot be set in stone)
        }

        /// <summary>A champion for the Hall of Fame: dressed for war (carved in stone afterwards), different for each name.</summary>
        internal static Look Champion(int seed)
        {
            var rng = new System.Random(seed);
            bool woman = rng.NextDouble() < 0.4;
            return new Look
            {
                Model = woman ? 1 : 0, HairItem = "Hair" + (1 + rng.Next(woman ? 20 : 14)), Beard = woman ? null : "Beard" + (1 + rng.Next(12)),
                Chest = new[] { "ArmorIronChest", "ArmorBronzeChest", "ArmorPaddedCuirass", "ArmorWolfChest" }[rng.Next(4)],
                Legs = new[] { "ArmorIronLegs", "ArmorBronzeLegs", "ArmorPaddedGreaves", "ArmorWolfLegs" }[rng.Next(4)],
                // (no cape: a cape is cloth, moved by its own simulation, and cannot be set in stone)
                Helmet = new[] { "HelmetIron", "HelmetBronze", "HelmetDrake", null }[rng.Next(4)],
                Right = new[] { "SwordIron", "AxeIron", "MaceIron", "SpearBronze", "SwordSilver" }[rng.Next(5)],
                Left = new[] { "ShieldBanded", "ShieldBronzeBuckler", "ShieldWood", null }[rng.Next(4)],
            };
        }

        /// <summary>Makes one: a local copy of the viking body, dressed. Keeps a collider only when asked (to be looked at and spoken to).</summary>
        internal static GameObject Make(Transform parent, Vector3 position, Quaternion rotation, Look look, bool collider)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
            if (prefab == null) return null;
            GameObject go;
            ZNetView.m_forceDisableInit = true;
            try { go = Object.Instantiate(prefab, position, rotation, parent); }
            finally { ZNetView.m_forceDisableInit = false; }
            go.name = "ArenaFigure";
            // what makes it the player, and what moves it by physics, is taken off (at once, so nothing of it runs)
            foreach (System.Type t in new[] { typeof(PlayerController), typeof(Player), typeof(Talker), typeof(Skills), typeof(ZSyncTransform), typeof(ZSyncAnimation), typeof(FootStep) })
            {
                Component c = go.GetComponent(t);
                if (c != null) Object.DestroyImmediate(c);
            }
            foreach (Rigidbody b in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(b);
            if (!collider) foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
            {
                a.updateMode = AnimatorUpdateMode.Normal; a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                a.SetBool("onGround", true);   // (standing, not falling: nothing else tells it so now the player's code is gone)
            }

            VisEquipment vis = go.GetComponent<VisEquipment>();
            if (vis != null) Dress(vis, look);
            return go;
        }

        internal static void Dress(VisEquipment vis, Look look)
        {
            vis.SetModel(look.Model);
            vis.SetSkinColor(new Vector3(look.Skin.r, look.Skin.g, look.Skin.b));
            vis.SetHairColor(new Vector3(look.Hair.r, look.Hair.g, look.Hair.b));
            vis.SetHairItem(Hash(look.HairItem));
            vis.SetBeardItem(Hash(look.Beard));
            vis.SetChestItem(Has(look.Chest) ? Hash(look.Chest) : 0);
            vis.SetLegItem(Has(look.Legs) ? Hash(look.Legs) : 0);
            vis.SetHelmetItem(Has(look.Helmet) ? Hash(look.Helmet) : 0);
            vis.SetShoulderItem(Has(look.Cape) ? Hash(look.Cape) : 0, 0, 1);
            vis.SetUtilityItem(Has(look.Utility) ? Hash(look.Utility) : 0);
            vis.SetRightItem(Has(look.Right) ? Hash(look.Right) : 0, 1);
            vis.SetLeftItem(Has(look.Left) ? Hash(look.Left) : 0, 0, 1);
        }

        // ---- moving them ----------------------------------------------------------------------------------------------------

        /// <summary>A one-off emote (cheer, roar, toast, flex, challenge, point, wave, laugh, nonono, thumbsup, bow...).</summary>
        internal static void Emote(GameObject figure, string emote)
        {
            Animator a = figure != null ? figure.GetComponentInChildren<Animator>() : null;
            if (a == null) return;
            a.ResetTrigger("emote_stop");
            a.SetTrigger("emote_" + emote);
        }

        /// <summary>A lasting emote (sit, dance, headbang, relax...), or none.</summary>
        internal static void Hold(GameObject figure, string emote, bool on)
        {
            Animator a = figure != null ? figure.GetComponentInChildren<Animator>() : null;
            if (a == null) return;
            a.SetBool("emote_" + emote, on);
            if (!on) a.SetTrigger("emote_stop");
        }

        /// <summary>
        /// Turns a figure to stone: every part of it takes the game's own stone (from the stone wall), and once it has struck its pose the
        /// animation stops, so it stands there as a statue.
        /// </summary>
        internal static void Petrify(GameObject figure, Material stone, string pose, float groundY = float.NaN)
        {
            if (figure == null) return;
            foreach (Animator a in figure.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (SkinnedMeshRenderer sk in figure.GetComponentsInChildren<SkinnedMeshRenderer>(true)) sk.updateWhenOffscreen = true;   // (every part posed, seen or not)
            NoCloth(figure);
            var freeze = figure.AddComponent<Freeze>();
            freeze.Pose = pose;
            freeze.Stone = stone;
            freeze.Ground = groundY;
        }

        /// <summary>
        /// The figure as it stands now, set in stone: every moving part (the body, the armour, the cape, the hair) is turned into one still
        /// shape in this pose, the animation and the equipment code are taken off, and the statue is set down so its feet stand on the ground.
        /// </summary>
        /// <summary>
        /// Capes are cloth the game simulates: on a statue (or in a crowd) that stops, so the cape hangs as it was made, and is set in stone
        /// with the rest. (A cloth left running on a body turned to stone loses its hold and balloons into a great sheet.)
        /// </summary>
        internal static void NoCloth(GameObject figure)
        {
            foreach (Behaviour b in figure.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.GetType().Name.Contains("MagicaCloth")) b.enabled = false;
        }

        /// <summary>
        /// The figure as it stands now, set in stone: the animation holds this pose (every part kept posed, seen or not), the equipment code is
        /// taken off, every part takes the stone, and the statue is set down so its feet stand on the ground.
        /// </summary>
        private static void Bake(GameObject figure, Material stone, float groundY)
        {
            foreach (Animator a in figure.GetComponentsInChildren<Animator>(true)) a.speed = 0f;
            ToStone(figure, stone);
        }

        /// <summary>Every part in stone (armour, hair and all), and no fire or light left on it.</summary>
        private static void ToStone(GameObject figure, Material stone)
        {
            foreach (Renderer r in figure.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) { r.enabled = false; continue; }
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = stone;
                r.sharedMaterials = mats;
                r.SetPropertyBlock(null);
            }
            foreach (Light l in figure.GetComponentsInChildren<Light>(true)) l.enabled = false;
        }

        /// <summary>Strikes a pose, then holds it still.</summary>
        private class Freeze : MonoBehaviour
        {
            public string Pose;
            public Material Stone;
            public float Ground = float.NaN;
            private float _at;
            private bool _done;

            private void Start()
            {
                _at = Time.time + 1.1f;
                Animator a = GetComponentInChildren<Animator>();
                if (a != null && !string.IsNullOrEmpty(Pose) && Pose != "none") { a.SetBool("emote_" + Pose, true); a.SetTrigger("emote_" + Pose); }
            }

            private void Update()
            {
                if (_done || Time.time < _at) return;
                _done = true;
                if (Stone != null) Bake(gameObject, Stone, Ground);
                enabled = false;
            }
        }

        // ---- the stone ----------------------------------------------------------------------------------------------------------

        private static Material _stone;

        /// <summary>The game's own carved stone (the stone wall's), for statues.</summary>
        internal static Material Stone()
        {
            if (_stone != null) return _stone;
            // a plain carved-stone colour on the game's own standard material (the stone wall's material is made for still pieces, and
            // breaks up a moving body)
            _stone = Things.Plain(ZNetScene.instance, new Color(0.62f, 0.6f, 0.56f), 0.05f);
            if (_stone.HasProperty("_Glossiness")) _stone.SetFloat("_Glossiness", 0.08f);
            _stone.name = "ArenaStatueStone";
            return _stone;
        }
    }
}
