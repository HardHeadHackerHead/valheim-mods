using UnityEngine;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>
    /// Builds a gun's item and its round. An item is copied from the Arbalest (so the hands, the aim pose, the Crossbows skill and the item
    /// type are the game's), with its own model and no ammo item. It is not fired by the game's attack (Fire.cs does that), so its Attack here
    /// only has to look like a crossbow's to the rest of the game.
    /// </summary>
    internal static class Gun
    {
        /// <summary>The additive material of the Arbalest round's trail: bright whatever the light, for the flash and the tracer.</summary>
        internal static Material Glow;
        /// <summary>The Arbalest's own material: a shader the game ships (a copy of Unity's default one for a primitive is pink here).</summary>
        internal static Material BaseMaterial;

        private const string KeyPrefix = "guns_";   // (the words are $guns_<gun> and $guns_<gun>_desc in translations/)

        // The barrel points along +Z in the model's own space; this turns it the way the Arbalest's model lies in a hand (tuned in the game).
        private static readonly Vector3 ModelEuler = new Vector3(0f, 90f, 0f);

        internal static GameObject BuildItem(ZNetScene scene, Transform holder, GunDef d)
        {
            GameObject source = scene.GetPrefab("CrossbowArbalest");
            if (source == null) { Plugin.Log.LogWarning("the CrossbowArbalest prefab was not found, so there is no " + d.Section); return null; }

            GameObject go = Object.Instantiate(source, holder);
            go.name = d.Id;
            Material baseMaterial = FirstMaterial(go);
            if (baseMaterial != null) BaseMaterial = baseMaterial;
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach (Collider c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);

            var attach = new GameObject("attach") { layer = go.layer };
            attach.transform.SetParent(go.transform, false);
            attach.transform.localRotation = Quaternion.Euler(ModelEuler);
            var model = new GameObject("model") { layer = go.layer };
            model.transform.SetParent(attach.transform, false);
            Bounds bounds = d.BuildModel(model.transform, baseMaterial);
            var box = go.AddComponent<BoxCollider>();
            Quaternion turn = attach.transform.localRotation;
            Vector3 size = turn * bounds.size;
            box.center = turn * bounds.center;
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) + Vector3.one * 0.02f;

            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = "$" + KeyPrefix + d.Key;
            s.m_description = "$" + KeyPrefix + d.Key + "_desc";
            s.m_ammoType = "";                 // no ammo item yet: the magazine is kept on the gun (Ammo.cs)
            s.m_maxQuality = 1;
            s.m_variants = 0;
            s.m_weight = d.Weight;
            s.m_value = 0;                     // never sold: the trader buys anything worth more than 0
            s.m_useDurability = false;
            s.m_dlc = "";
            s.m_questItem = false;
            s.m_teleportable = true;
            s.m_attackForce = d.Force;
            s.m_backstabBonus = 1f;
            ApplyDamage(s, d);
            d.CDamage.SettingChanged += (a, e) => { if (go != null) ApplyDamage(s, d); };
            if (d.Dual) s.m_animationState = ItemDrop.ItemData.AnimationState.OneHanded;   // a pair: arms down at the sides, not a crossbow carry with one hand reaching forward
            s.m_blockPower = 0f;               // right mouse aims (Aim.cs): it must not also be a shield
            s.m_deflectionForce = 0f;
            s.m_attack.m_requiresReload = false;   // the game would queue a reload animation for a weapon that wants one
            s.m_attack.m_bowDraw = false;
            s.m_attack.m_attackType = Attack.AttackType.None;   // Fire.cs shoots; if the game's own attack ever runs (a reload's gap) it fires nothing, instead of a round with no projectile
            s.m_attack.m_attackStamina = 0f;
            SoundOnlyAll(s, "item");             // the Arbalest's fire effects are a shockwave bigger than the player: keep sounds only
            SoundOnlyAll(s.m_attack, "attack");
            SoundOnlyAll(s.m_secondaryAttack, "secondary attack");
            s.m_triggerEffect.m_effectPrefabs = Fx.FireEffects(d);   // the gun's own sound and flash (Fx.cs), played at the muzzle by Fire.cs
            drop.m_itemData.m_dropPrefab = go;
            drop.m_itemData.m_stack = 1;

            Sprite icon = GunIcon.Make(model.transform, d.IconRoll);   // (the shared Icon.cs gave nothing for this shape: the Arbalest's icon stayed)
            if (icon != null) s.m_icons = new[] { icon };
            else Plugin.Log.LogWarning("the " + d.Section + " has no icon of its own");
            return go;
        }

        private static void ApplyDamage(ItemDrop.ItemData.SharedData s, GunDef d)
        {
            s.m_damages = d.Flame ? new HitData.DamageTypes { m_fire = d.DamageNow } : new HitData.DamageTypes { m_pierce = d.DamageNow };
            s.m_damagesPerLevel = new HitData.DamageTypes();
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
                if (e.m_prefab != null && e.m_prefab.name.StartsWith("sfx_")) keep.Add(e);
            list.m_effectPrefabs = keep.ToArray();
        }

        /// <summary>The round: the Arbalest's bolt with a tracer for a body, a flat flight and nothing left behind where it lands.</summary>
        internal static GameObject BuildBullet(ZNetScene scene, Transform holder, GunDef d)
        {
            GameObject source = scene.GetPrefab("arbalest_projectile_iron");
            if (source == null) { Plugin.Log.LogWarning("the arbalest_projectile_iron prefab was not found, so there are no rounds"); return null; }
            GameObject go = Object.Instantiate(source, holder);
            go.name = d.BulletId;
            Material material = FirstMaterial(go);
            // Its particle effects (a shockwave ring, a smoke trail) are drawn for a slow heavy bolt: far too big for a gun's round.
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
                if (Glow == null && pr != null) Glow = pr.sharedMaterial;
                if (pr != null) Object.DestroyImmediate(pr);
                Object.DestroyImmediate(ps);
            }
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            Material look = Glow != null ? Glow : material;
            if (d.TracerLength > 0f)
            {
                var tracer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(tracer.GetComponent<Collider>());
                tracer.name = "tracer";
                tracer.layer = go.layer;
                tracer.transform.SetParent(go.transform, false);
                tracer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // (a capsule stands along Y; the round flies along Z)
                tracer.transform.localPosition = new Vector3(0f, 0f, -d.TracerLength);   // a streak trailing behind the head of the round
                tracer.transform.localScale = new Vector3(0.014f, d.TracerLength, 0.014f);
                Model.Paint(tracer, look, d.Tracer, emissive: true);
            }
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.name = "head";
            head.layer = go.layer;
            head.transform.SetParent(go.transform, false);
            head.transform.localScale = d.Pellets > 1 ? new Vector3(0.025f, 0.025f, 0.04f) : new Vector3(0.03f, 0.03f, 0.07f);
            Model.Paint(head, look, Color.Lerp(d.Tracer, Color.white, 0.6f), emissive: true);

            Projectile p = go.GetComponent<Projectile>();
            if (p != null)
            {
                p.m_ttl = d.Ttl;
                p.m_gravity = d.Gravity;
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
    }
}
