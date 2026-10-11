using System;
using UnityEngine;

namespace CigarSmoking
{
    internal static partial class Things
    {
        private static bool BuildItems()
        {
            GameObject source = Find("Dandelion");
            if (source == null) { Debug.LogWarning("[" + Plugin.Name + "] the Dandelion prefab was not found, so there are no items"); return false; }

            foreach (Strain s in Strains.All)
            {
                MakeItem(source, s.Seeds, s.Key("seeds"), s.Key("seeds_desc"),
                         ItemDrop.ItemData.ItemType.Material, 50, 0.05f, 3, Icons.Get("seeds_" + s.Id), "seeds_" + s.Id);
                MakeItem(source, s.Fresh, s.Key("leaf"), s.Key("leaf_desc"),
                         ItemDrop.ItemData.ItemType.Material, 50, 0.1f, 4, Icons.Get("leaf_" + s.Id + "_fresh"), "leaf_" + s.Id + "_fresh");
                MakeItem(source, s.Dried, s.Key("dried"), s.Key("dried_desc"),
                         ItemDrop.ItemData.ItemType.Material, 50, 0.1f, 8, Icons.Get("leaf_" + s.Id + "_dried"), "leaf_" + s.Id + "_dried");
                MakeItem(source, s.Aged, s.Key("aged"), s.Key("aged_desc"),
                         ItemDrop.ItemData.ItemType.Material, 50, 0.1f, 14, Icons.Get("leaf_" + s.Id + "_aged"), "leaf_" + s.Id + "_aged");
            }

            foreach (CigarType t in Types.All)
            {
                Sprite icon = Icons.Get("cigar_" + t.Id);
                SE_Smoking effect = Smoking.Make(t, icon);
                Effects.Add(effect);
                GameObject cigar = MakeItem(source, t.Prefab, t.Display, t.Flavour, ItemDrop.ItemData.ItemType.Consumable, 20, 0.1f, t.Level == 1 ? 20 : 40, icon, null);
                Safely(t.Prefab, () => Cigar.AddModels(cigar, t));
                FitHitbox(cigar, new Bounds(Vector3.zero, new Vector3(Cigar.Length, Cigar.Radius * 4f, Cigar.Radius * 4f)));
                ItemDrop.ItemData.SharedData shared = cigar.GetComponent<ItemDrop>().m_itemData.m_shared;
                shared.m_foodEatAnimTime = 2.5f;
                shared.m_consumeStatusEffect = effect;
            }
            return true;
        }

        /// <summary>A copy of the Dandelion's item prefab (so it has everything an item needs to lie on the ground and be picked up) dressed as a new item.</summary>
        private static GameObject MakeItem(GameObject source, string prefab, string display, string description, ItemDrop.ItemData.ItemType type,
                                           int stack, float weight, int value, Sprite icon, string model)
        {
            GameObject go = UnityEngine.Object.Instantiate(source, _holder.transform);
            go.name = prefab;
            go.layer = LayerMask.NameToLayer("item");   // what the game looks for to pick it up
            foreach (LODGroup lod in go.GetComponents<LODGroup>()) UnityEngine.Object.DestroyImmediate(lod);
            foreach (Collider c in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (MeshRenderer r in go.GetComponents<MeshRenderer>()) UnityEngine.Object.DestroyImmediate(r);
            foreach (MeshFilter m in go.GetComponents<MeshFilter>()) UnityEngine.Object.DestroyImmediate(m);
            for (int i = go.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);

            if (model != null)
            {
                object[][] parts = ModelIndex.Parts(model);
                Safely(prefab, () => ModelBuilder.Build(go.transform, parts, go.layer, "model"));
                FitHitbox(go, ModelBuilder.BoundsOf(parts));
            }

            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = display;
            s.m_description = description;
            s.m_itemType = type;
            s.m_icons = new[] { icon };
            s.m_maxStackSize = stack;
            s.m_weight = weight;
            s.m_value = value;
            s.m_maxQuality = 1;
            s.m_variants = 0;
            s.m_food = 0f;
            s.m_foodStamina = 0f;
            s.m_foodEitr = 0f;
            s.m_teleportable = true;
            s.m_questItem = false;
            s.m_dlc = "";
            s.m_consumeStatusEffect = null;
            s.m_equipStatusEffect = null;
            drop.m_itemData.m_dropPrefab = go;
            drop.m_itemData.m_stack = 1;
            Made[prefab] = go;
            ItemPrefabs.Add(go);
            return go;
        }

        private static void FitHitbox(GameObject go, Bounds b)
        {
            foreach (Collider c in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            var box = go.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = Vector3.Max(b.size, new Vector3(0.05f, 0.03f, 0.05f));
        }
    }
}
