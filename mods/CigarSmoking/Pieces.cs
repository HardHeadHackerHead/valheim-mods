using System.Collections.Generic;
using UnityEngine;

namespace CigarSmoking
{
    internal static partial class Things
    {
        private static GameObject Clone(ZNetScene scene, string source, string name)
        {
            GameObject src = scene.GetPrefab(source);
            if (src == null) { Debug.LogWarning("[" + Plugin.Name + "] the " + source + " prefab was not found, so there is no " + name); return null; }
            GameObject go = Object.Instantiate(src, _holder.transform);
            go.name = name;
            Made[name] = go;
            return go;
        }

        /// <summary>Hides what the copy was (renderers, its LOD groups, its hit boxes), keeping the game's behaviours on it.</summary>
        private static void Strip(GameObject go)
        {
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r.GetComponentInParent<CircleProjector>() != null) continue; // the placement marker stays
                r.enabled = false;
            }
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        }

        /// <summary>Dresses a copy of a piece in a model of ours, with one hit box round it.</summary>
        private static void Reskin(GameObject go, object[][] parts)
        {
            Strip(go);
            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) layer = go.layer;
            Bounds b = ModelBuilder.BoundsOf(parts);
            var hit = new GameObject("Hitbox") { layer = layer };
            hit.transform.SetParent(go.transform, false);
            var box = hit.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size;
            ModelBuilder.Build(go.transform, parts, go.layer);
        }

        private static void SetPiece(GameObject go, string name, string description, string icon, params Piece.Requirement[] cost)
        {
            Piece piece = go.GetComponent<Piece>();
            piece.m_name = name;
            piece.m_description = description;
            piece.m_icon = Icons.Get(icon);
            piece.m_category = Piece.PieceCategory.Crafting;
            piece.m_resources = cost;
        }

        private static void BuildPieces(ZNetScene scene)
        {
            CraftingStation workbench = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();

            // --- the Cigar Rolling Table: a crafting station of its own, built near a workbench
            GameObject table = Clone(scene, "piece_workbench", "dh_cigar_table");
            if (table != null)
            {
                Transform area = table.transform.Find("PlayerBase");
                if (area != null) Object.DestroyImmediate(area.gameObject);   // a table is not a base
                Reskin(table, ModelIndex.Parts("table"));
                CraftingStation cs = table.GetComponent<CraftingStation>();
                cs.m_name = "Cigar Rolling Table";
                cs.m_icon = Icons.Get("table");
                cs.m_craftRequireRoof = false;
                cs.m_craftRequireFire = false;
                SetPiece(table, "Cigar Rolling Table", "Roll cigars from cured leaf. Build a Humidor beside it for the finer cigars.", "table",
                         Req("Wood", 10), Req("Resin", 4), Req("LeatherScraps", 4));
                table.GetComponent<Piece>().m_craftingStation = workbench;
                TableStation = cs;
                HammerPieces.Add(table);
            }

            // --- the Humidor: raises the table's level, which the finer cigars need
            GameObject humidor = Clone(scene, "piece_workbench_ext1", "dh_humidor");
            if (humidor != null)
            {
                Reskin(humidor, ModelIndex.Parts("humidor"));
                StationExtension ext = humidor.GetComponent<StationExtension>();
                if (ext != null && TableStation != null) ext.m_craftingStation = TableStation;
                SetPiece(humidor, "Humidor", "A cedar cabinet that keeps leaf and cigars at their best. Build it near a Cigar Rolling Table to roll the finer cigars.", "humidor",
                         Req("FineWood", 10), Req("LeatherScraps", 3), Req("Resin", 3));
                humidor.GetComponent<Piece>().m_craftingStation = TableStation;
                HammerPieces.Add(humidor);
            }

            // --- the Drying Rack and the Curing Barrel: copies of the wooden chest that hold leaves while they change
            GameObject rack = Clone(scene, "piece_chest_wood", "dh_drying_rack");
            if (rack != null)
            {
                Object.DestroyImmediate(rack.GetComponent<Container>());
                Reskin(rack, ModelIndex.Parts("rack"));
                SetPiece(rack, "Tobacco Drying Rack", "Hang fresh tobacco leaves here to dry in the air.", "rack", Req("Wood", 8), Req("Resin", 2));
                rack.GetComponent<Piece>().m_craftingStation = workbench;
                Curer c = rack.AddComponent<Curer>();
                c.Title = "Tobacco Drying Rack"; c.Doing = "Drying"; c.Done = "dried"; c.Capacity = 8; c.Seconds = Plugin.DryMinutes.Value * 60f;
                c.Drying = true;
                HammerPieces.Add(rack);
            }
            GameObject barrel = Clone(scene, "piece_chest_wood", "dh_curing_barrel");
            if (barrel != null)
            {
                Object.DestroyImmediate(barrel.GetComponent<Container>());
                Reskin(barrel, ModelIndex.Parts("barrel"));
                SetPiece(barrel, "Tobacco Curing Barrel", "Pack dried tobacco leaves in here to age and ferment into dark, oily leaf.", "barrel",
                         Req("Wood", 12), Req("Resin", 4), Req("LeatherScraps", 2));
                barrel.GetComponent<Piece>().m_craftingStation = workbench;
                Curer c = barrel.AddComponent<Curer>();
                c.Title = "Tobacco Curing Barrel"; c.Doing = "Curing"; c.Done = "aged"; c.Capacity = 8; c.Seconds = Plugin.CureMinutes.Value * 60f;
                c.Drying = false;
                HammerPieces.Add(barrel);
            }

            // --- the plants of each strain
            foreach (Strain s in Strains.All) BuildPlants(scene, s);
        }

        private static void Replace(Transform at, object[][] parts)
        {
            for (int i = at.childCount - 1; i >= 0; i--) Object.DestroyImmediate(at.GetChild(i).gameObject);
            foreach (MeshRenderer r in at.GetComponents<MeshRenderer>()) Object.DestroyImmediate(r);
            foreach (MeshFilter m in at.GetComponents<MeshFilter>()) Object.DestroyImmediate(m);
            // the game's own parts are imported meshes, turned and scaled to suit them: ours want a plain, upright place
            at.localRotation = Quaternion.identity;
            at.localScale = Vector3.one;
            ModelBuilder.Build(at, parts, at.gameObject.layer);
        }

        private static void BuildPlants(ZNetScene scene, Strain s)
        {
            string tobacco = s.Display + " Tobacco";
            ItemDrop fresh = Find(s.Fresh)?.GetComponent<ItemDrop>();
            ItemDrop seeds = Find(s.Seeds)?.GetComponent<ItemDrop>();
            object[][] mature = ModelIndex.Parts("plant_" + s.Id + "_mature");

            // the full-grown plant you pick leaves from (what a sapling turns into)
            GameObject ripe = Clone(scene, "Pickable_Carrot", s.Pickable);
            if (ripe != null)
            {
                foreach (LODGroup lod in ripe.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
                for (int i = ripe.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(ripe.transform.GetChild(i).gameObject);
                GameObject model = ModelBuilder.Build(ripe.transform, mature, ripe.layer);
                Pickable p = ripe.GetComponent<Pickable>();
                p.m_hideWhenPicked = model;
                p.m_itemPrefab = fresh != null ? fresh.gameObject : null;
                p.m_amount = 3;
                p.m_overrideName = tobacco;
                p.m_extraDrops = SeedDrop(seeds, 0.6f);
                p.m_respawnTimeMinutes = 0f;
                FitPlantCollider(ripe, s.Height);
            }

            // the wild plant: the same, found growing in its biome, and it grows back
            GameObject wild = Clone(scene, "Pickable_Dandelion", s.WildPickable);
            if (wild != null)
            {
                Transform visual = wild.transform.Find("visual");
                foreach (LODGroup lod in wild.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
                GameObject model;
                if (visual != null)
                {
                    foreach (Collider c in visual.GetComponents<Collider>()) Object.DestroyImmediate(c);
                    Replace(visual, mature);
                    model = visual.gameObject;
                }
                else
                {
                    for (int i = wild.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(wild.transform.GetChild(i).gameObject);
                    model = ModelBuilder.Build(wild.transform, mature, wild.layer);
                }
                Pickable p = wild.GetComponent<Pickable>();
                p.m_hideWhenPicked = model;
                p.m_itemPrefab = fresh != null ? fresh.gameObject : null;
                p.m_amount = 3;
                p.m_overrideName = "Wild " + tobacco;
                p.m_extraDrops = SeedDrop(seeds, 1f);
                p.m_respawnTimeMinutes = 300f;
                FitPlantCollider(wild, s.Height);
            }

            // the sapling: planted with the cultivator from seeds; grows through two stages and turns into the full-grown plant
            GameObject sapling = Clone(scene, "sapling_carrot", s.Sapling);
            if (sapling != null)
            {
                foreach (LODGroup lod in sapling.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
                Replace(sapling.transform.Find("healthy"), ModelIndex.Parts("plant_" + s.Id + "_seedling"));
                Replace(sapling.transform.Find("unhealthy"), ModelIndex.Parts("plant_" + s.Id + "_seedling_sick"));
                Replace(sapling.transform.Find("healthy_grown"), ModelIndex.Parts("plant_" + s.Id + "_growing"));
                Replace(sapling.transform.Find("unhealthy_grown"), ModelIndex.Parts("plant_" + s.Id + "_growing_sick"));
                Plant plant = sapling.GetComponent<Plant>();
                plant.m_name = tobacco;
                plant.m_grownPrefabs = ripe != null ? new[] { ripe } : new GameObject[0];
                plant.m_growTime = Plugin.GrowMinutes.Value * 60f;
                plant.m_growTimeMax = plant.m_growTime * 1.4f;
                plant.m_biome = s.Plant;
                plant.m_needCultivatedGround = true;
                SetPiece(sapling, tobacco, "Sow " + s.Display.ToLower() + " tobacco seeds in cultivated soil. It grows tall in a few days and gives leaves and seeds.", "plant_" + s.Id,
                         new Piece.Requirement { m_resItem = seeds, m_amount = 1, m_recover = false });
                sapling.GetComponent<Piece>().m_category = Piece.PieceCategory.Misc;
                CultivatorPieces.Add(sapling);
                Debug.Log("[" + Plugin.Name + "] " + s.Sapling + " grows into " + string.Join(", ", System.Array.ConvertAll(plant.m_grownPrefabs, g => g != null ? g.name : "NOTHING"))
                          + " (the harvest plant " + (ripe != null ? "was built" : "is MISSING") + ")");
            }
        }

        private static DropTable SeedDrop(ItemDrop seeds, float chance)
        {
            var table = new DropTable { m_dropMin = 1, m_dropMax = 1, m_dropChance = chance };
            if (seeds != null)
                table.m_drops.Add(new DropTable.DropData { m_item = seeds.gameObject, m_stackMin = 1, m_stackMax = 2, m_weight = 1f });
            return table;
        }

        private static void FitPlantCollider(GameObject go, float height)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.28f;
            capsule.height = height * 0.9f;
            capsule.center = new Vector3(0f, height * 0.45f, 0f);
        }
    }
}

namespace CigarSmoking
{
    // A sapling remembers what it grows into by reference. If this mod was reloaded since it was planted (an update while the game runs),
    // that reference is dead: point it at the current plant just before it grows.
    [HarmonyLib.HarmonyPatch(typeof(Plant), nameof(Plant.Grow))]
    internal static class Plant_Grow
    {
        private static void Prefix(Plant __instance)
        {
            string name = Utils.GetPrefabName(__instance.gameObject);
            foreach (Strain s in Strains.All)
                if (s.Sapling == name && Things.Made.TryGetValue(s.Pickable, out GameObject ripe) && ripe != null)
                    __instance.m_grownPrefabs = new[] { ripe };
        }
    }
}
