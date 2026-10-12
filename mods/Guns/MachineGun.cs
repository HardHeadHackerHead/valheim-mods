using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>
    /// The Machine Gun: an item copied from the Arbalest (so the hands, the aim pose, the Crossbows skill and the item type are the game's),
    /// with its own model, no ammo and no reload. It is not fired by the game's attack (Fire.cs does that), so its Attack here only has to
    /// look like a crossbow's to the rest of the game.
    /// </summary>
    internal static class MachineGun
    {
        public const string Prefab = "dh_machinegun";
        public const string BulletPrefab = "dh_machinegun_bullet";
        internal const float BulletSpeed = 130f;
        /// <summary>The additive material of the Arbalest round's trail: bright whatever the light, for the flash and the tracer.</summary>
        internal static Material Glow;
        /// <summary>The Arbalest's own material: a shader the game ships (a copy of Unity's default one for a primitive is pink here).</summary>
        internal static Material BaseMaterial;
        internal const float Gravity = 3f;   // the round's (Projectile.m_gravity), which the aim lifts for

        internal static bool Is(ItemDrop.ItemData item) => item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == Prefab;

        // The barrel points along +Z in the model's own space; this turns it the way the Arbalest's model lies in a hand (tuned in the game).
        private static readonly Vector3 ModelEuler = new Vector3(0f, 90f, 0f);

        internal static GameObject BuildItem(ZNetScene scene, Transform holder)
        {
            GameObject source = scene.GetPrefab("CrossbowArbalest");
            if (source == null) { Plugin.Log.LogWarning("the CrossbowArbalest prefab was not found, so there is no Machine Gun"); return null; }

            GameObject go = Object.Instantiate(source, holder);
            go.name = Prefab;
            Material baseMaterial = FirstMaterial(go);
            if (baseMaterial != null) BaseMaterial = baseMaterial;
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach (Collider c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);

            var attach = new GameObject("attach") { layer = go.layer };
            attach.transform.SetParent(go.transform, false);
            attach.transform.localRotation = Quaternion.Euler(ModelEuler);
            Bounds bounds = Model.Build(attach.transform, baseMaterial, go.layer);
            var box = go.AddComponent<BoxCollider>();
            Quaternion turn = attach.transform.localRotation;
            Vector3 size = turn * bounds.size;
            box.center = turn * bounds.center;
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) + Vector3.one * 0.02f;

            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = "$guns_machinegun";
            s.m_description = "$guns_machinegun_desc";
            s.m_ammoType = "";                 // no ammo while testing
            s.m_maxQuality = 1;
            s.m_variants = 0;
            s.m_weight = 6f;
            s.m_value = 0;                     // never sold: the trader buys anything worth more than 0
            s.m_useDurability = false;
            s.m_dlc = "";
            s.m_questItem = false;
            s.m_teleportable = true;
            s.m_attackForce = 8f;
            s.m_backstabBonus = 1f;
            s.m_damages = new HitData.DamageTypes { m_pierce = Plugin.Damage.Value };
            s.m_damagesPerLevel = new HitData.DamageTypes();
            Plugin.Damage.SettingChanged += (a, e) => { if (go != null) s.m_damages = new HitData.DamageTypes { m_pierce = Plugin.Damage.Value }; };
            s.m_blockPower = 0f;               // right mouse aims (Aim.cs): it must not also be a shield
            s.m_deflectionForce = 0f;
            s.m_attack.m_requiresReload = false;   // the game would queue a reload animation for a weapon that wants one
            s.m_attack.m_bowDraw = false;
            s.m_attack.m_attackType = Attack.AttackType.None;   // Fire.cs shoots; if the game's own attack ever runs (a reload's gap) it fires nothing, instead of a round with no projectile
            s.m_attack.m_attackStamina = 0f;
            SoundOnlyAll(s, "item");             // the Arbalest's fire effects are a shockwave bigger than the player: keep sounds only
            SoundOnlyAll(s.m_attack, "attack");
            SoundOnlyAll(s.m_secondaryAttack, "secondary attack");
            s.m_triggerEffect.m_effectPrefabs = FireEffects();   // the gun's own sound and flash (Fx.cs), played at the muzzle by Fire.cs
            drop.m_itemData.m_dropPrefab = go;
            drop.m_itemData.m_stack = 1;

            Sprite icon = GunIcon.Make(attach.transform.Find("model"));   // (the shared Icon.cs gave nothing for this shape: the Arbalest's icon stayed)
            if (icon != null) s.m_icons = new[] { icon };
            else Plugin.Log.LogWarning("the Machine Gun has no icon of its own");
            return go;
        }

        private static EffectList.EffectData[] FireEffects()
        {
            var list = new System.Collections.Generic.List<EffectList.EffectData>();
            if (Fx.SoundPrefab != null) list.Add(new EffectList.EffectData { m_prefab = Fx.SoundPrefab, m_enabled = true, m_variant = -1 });
            if (Fx.FlashPrefab != null) list.Add(new EffectList.EffectData { m_prefab = Fx.FlashPrefab, m_enabled = true, m_variant = -1 });
            return list.ToArray();
        }

        /// <summary>SoundOnly on every EffectList field of an object (the item's shared data, an Attack), so none is missed.</summary>
        private static void SoundOnlyAll(object owner, string what)
        {
            if (owner == null) return;
            foreach (System.Reflection.FieldInfo f in owner.GetType().GetFields())
                if (f.FieldType == typeof(EffectList)) SoundOnly((EffectList)f.GetValue(owner));
        }

        /// <summary>Takes every effect that isn't a sound (sfx_ prefabs) out of a list the Arbalest brought.</summary>
        private static void SoundOnly(EffectList list)
        {
            if (list?.m_effectPrefabs == null) return;
            var keep = new System.Collections.Generic.List<EffectList.EffectData>();
            foreach (EffectList.EffectData e in list.m_effectPrefabs)
            {
                if (e.m_prefab != null && e.m_prefab.name.StartsWith("sfx_")) keep.Add(e);
                
            }
            list.m_effectPrefabs = keep.ToArray();
        }

        /// <summary>The round: the Arbalest's bolt with a tracer for a body, a flat flight and nothing left behind where it lands.</summary>
        internal static GameObject BuildBullet(ZNetScene scene, Transform holder)
        {
            GameObject source = scene.GetPrefab("arbalest_projectile_iron");
            if (source == null) { Plugin.Log.LogWarning("the arbalest_projectile_iron prefab was not found, so there are no rounds"); return null; }
            GameObject go = Object.Instantiate(source, holder);
            go.name = BulletPrefab;
            Material material = FirstMaterial(go);
            // Its particle effects (a shockwave ring, a smoke trail) are drawn for a slow heavy bolt: far too big for a machine-gun round.
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
                if (Glow == null && pr != null) Glow = pr.sharedMaterial;
                if (pr != null) Object.DestroyImmediate(pr);
                Object.DestroyImmediate(ps);
            }
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            var tracer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(tracer.GetComponent<Collider>());
            tracer.name = "tracer";
            tracer.layer = go.layer;
            tracer.transform.SetParent(go.transform, false);
            tracer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // (a capsule stands along Y; the round flies along Z)
            tracer.transform.localPosition = new Vector3(0f, 0f, -0.45f);      // a streak trailing behind the head of the round
            tracer.transform.localScale = new Vector3(0.014f, 0.45f, 0.014f);
            Model.Paint(tracer, Glow != null ? Glow : material, new Color(1f, 0.74f, 0.30f), emissive: true);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.name = "head";
            head.layer = go.layer;
            head.transform.SetParent(go.transform, false);
            head.transform.localScale = new Vector3(0.03f, 0.03f, 0.07f);
            Model.Paint(head, Glow != null ? Glow : material, new Color(1f, 0.95f, 0.75f), emissive: true);

            Projectile p = go.GetComponent<Projectile>();
            if (p != null)
            {
                p.m_ttl = 3f;
                p.m_gravity = Gravity;
                p.m_drag = 0f;
                p.m_stayAfterHitStatic = false;
                p.m_stayAfterHitDynamic = false;
                p.m_respawnItemOnHit = false;
                p.m_spawnOnHit = null;
                p.m_spawnOnHitChance = 0f;
                p.m_spawnOnTtl = false;
                p.m_randomSpawnOnHit.Clear();
                p.m_showBreakMessage = false;
                p.m_hitNoise = 15f;
            }
            return go;
        }

        private static Material FirstMaterial(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null && !(r is ParticleSystemRenderer)) return r.sharedMaterial;
            return null;
        }

        private static Bounds BoundsOf(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
