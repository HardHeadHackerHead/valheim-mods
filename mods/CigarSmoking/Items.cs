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
                string name = s.Display + " Tobacco";
                MakeItem(source, s.Seeds, name + " Seeds", "Seeds of the " + s.Display.ToLower() + " tobacco plant. Sow them in cultivated soil with the cultivator.",
                         ItemDrop.ItemData.ItemType.Material, 50, 0.05f, 3, Icons.Get("seeds_" + s.Id), "seeds_" + s.Id);
                MakeItem(source, s.Fresh, name + " Leaf", "A big green " + s.Display.ToLower() + " leaf, fresh from the plant and bitter. Hang it on a drying rack.",
                         ItemDrop.ItemData.ItemType.Material, 50, 0.1f, 4, Icons.Get("leaf_" + s.Id + "_fresh"), "leaf_" + s.Id + "_fresh");
                MakeItem(source, s.Dried, "Dried " + s.Display + " Leaf", "A " + s.Display.ToLower() + " leaf dried on the rack until it is golden and brittle. Pack it in a curing barrel to age it, or roll it as it is.",
                         ItemDrop.ItemData.ItemType.Material, 50, 0.1f, 8, Icons.Get("leaf_" + s.Id + "_dried"), "leaf_" + s.Id + "_dried");
                MakeItem(source, s.Aged, "Aged " + s.Display + " Leaf", "A " + s.Display.ToLower() + " leaf cured in the barrel until it is dark, oily and smooth. The wrapper and filler of a fine cigar.",
                         ItemDrop.ItemData.ItemType.Material, 50, 0.1f, 14, Icons.Get("leaf_" + s.Id + "_aged"), "leaf_" + s.Id + "_aged");
            }

            foreach (CigarType t in Types.All)
            {
                Sprite icon = Icons.Get("cigar_" + t.Id);
                SE_Smoking effect = Smoking.Make(t, icon);
                Effects.Add(effect);
                GameObject cigar = MakeItem(source, t.Prefab, t.Display, t.Flavour, ItemDrop.ItemData.ItemType.Consumable, 20, 0.1f, t.Level == 1 ? 20 : 40, icon, null);
                Cigar.AddModels(cigar, t);
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
                ModelBuilder.Build(go.transform, parts, go.layer, "model");
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
