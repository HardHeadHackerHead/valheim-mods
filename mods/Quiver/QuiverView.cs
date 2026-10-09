using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Quiver
{
    /// <summary>
    /// The quiver on your back: while you have arrows (or bolts) equipped, a leather tube hangs from your spine, tilted a little, with arrows
    /// sticking out of it, the nock end up. The arrows are the real model of the arrow equipped (so wooden, flint and bronze ones look it),
    /// and there are more of them the more you carry. It stands where the settings say (Quiver: Back, Side, Height, TiltSide, TiltBack).
    /// </summary>
    internal class QuiverView : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> AmmoRef = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_ammoItem");

        private Humanoid _body;
        private GameObject _root;
        private string _shown = "";
        private float _next;
        private static Material _leather, _dark;

        private void Awake() => _body = GetComponent<Humanoid>();

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.3f;
            Refresh();
        }

        public void Clear()
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _shown = "";
        }

        private void Refresh()
        {
            ItemDrop.ItemData ammo = Plugin.ShowQuiver.Value && _body != null && !_body.IsDead() ? AmmoRef(_body) : null;
            if (ammo == null || ammo.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo || ammo.m_dropPrefab == null) { Clear(); return; }

            int carried = _body.GetInventory().CountItems(ammo.m_shared.m_name);
            int shown = Mathf.Clamp(1 + carried / 15, 1, 7);
            string key = $"{ammo.m_dropPrefab.name}|{shown}|{Plugin.Back.Value}|{Plugin.Side.Value}|{Plugin.Height.Value}|{Plugin.TiltSide.Value}|{Plugin.TiltBack.Value}";
            if (key == _shown && _root != null) return;
            Clear();
            _shown = key;
            Build(ammo, shown);
        }

        // ---- the model ----------------------------------------------------------------------------------------------

        private const float TubeHalf = 0.25f, TubeRadius = 0.06f, Sticks = 0.17f, MaxArrow = 0.6f;

        private void Build(ItemDrop.ItemData ammo, int arrows)
        {
            Transform bone = FindBone(transform, "Spine2") ?? FindBone(transform, "Spine1") ?? FindBone(transform, "Spine");
            if (bone == null) return;
            MakeMaterials();

            _root = new GameObject("Quiver");
            // The pose is worked out in the player's own directions (back, right, up) from where the spine is now, then the quiver is hung
            // from the spine bone keeping that pose, so it moves with the body but does not depend on how the bone's axes are turned.
            Vector3 up = transform.up, right = transform.right, back = -transform.forward;
            Vector3 axis = (up + right * Mathf.Tan(Plugin.TiltSide.Value * Mathf.Deg2Rad) + back * Mathf.Tan(Plugin.TiltBack.Value * Mathf.Deg2Rad)).normalized;
            _root.transform.position = bone.position + back * Plugin.Back.Value + right * Plugin.Side.Value + up * Plugin.Height.Value;
            _root.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.LookRotation(Vector3.forward, Vector3.up);
            _root.transform.localScale = Vector3.one;

            Part(PrimitiveType.Cylinder, "Tube", new Vector3(TubeRadius * 2f, TubeHalf, TubeRadius * 2f), Vector3.zero, _leather);
            Part(PrimitiveType.Cylinder, "Rim", new Vector3(TubeRadius * 2.25f, 0.012f, TubeRadius * 2.25f), Vector3.up * TubeHalf, _dark);
            Part(PrimitiveType.Cylinder, "Base", new Vector3(TubeRadius * 1.7f, 0.02f, TubeRadius * 1.7f), Vector3.down * TubeHalf, _dark);
            Part(PrimitiveType.Cylinder, "Band", new Vector3(TubeRadius * 2.15f, 0.014f, TubeRadius * 2.15f), Vector3.up * (TubeHalf * 0.45f), _dark);

            var random = new System.Random(ammo.m_dropPrefab.name.GetHashCode());
            for (int i = 0; i < arrows; i++)
            {
                float ring = i == 0 ? 0f : 0.032f;
                float angle = i * 137.5f * Mathf.Deg2Rad;
                var spot = new Vector3(Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);
                var lean = Quaternion.Euler((float)(random.NextDouble() * 10 - 5), 0f, (float)(random.NextDouble() * 10 - 5));
                Arrow(ammo.m_dropPrefab, spot, lean);
            }
            _root.transform.SetParent(bone, true);   // (keeps where it is in the world)
            foreach (Collider c in _root.GetComponentsInChildren<Collider>()) Destroy(c);
        }

        private void Part(PrimitiveType shape, string name, Vector3 scale, Vector3 at, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>One arrow made of the arrow item's own meshes and materials, nock end up, its tip well down in the tube.</summary>
        private void Arrow(GameObject prefab, Vector3 at, Quaternion lean)
        {
            var holder = new GameObject("Arrow");
            holder.transform.SetParent(_root.transform, false);
            float nock = float.MaxValue, tip = float.MinValue;
            Renderer[] parts = prefab.GetComponentInChildren<LODGroup>() is LODGroup lod && lod.GetLODs().Length > 0 ? lod.GetLODs()[0].renderers : prefab.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in parts)
            {
                MeshFilter mf = r != null ? r.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null || !(r is MeshRenderer mr)) continue;
                Matrix4x4 m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;   // (this part, in the arrow's own frame)
                var part = new GameObject(mf.name);
                part.transform.SetParent(holder.transform, false);
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                Bounds b = mf.sharedMesh.bounds;
                foreach (float x in new[] { b.min.x, b.max.x }) foreach (float y in new[] { b.min.y, b.max.y }) foreach (float z in new[] { b.min.z, b.max.z })
                {
                    float along = m.MultiplyPoint3x4(new Vector3(x, y, z)).z;
                    nock = Mathf.Min(nock, along);
                    tip = Mathf.Max(tip, along);
                }
            }
            if (nock == float.MaxValue || tip - nock < 0.01f) { Destroy(holder); return; }
            // an arrow flies along +Z with the nock at the back: turned so +Z points down, the nock end is up. Shrunk so the whole arrow
            // fits (its tip inside the tube) and only its nock end shows above the rim.
            float scale = Mathf.Min(1f, MaxArrow / (tip - nock));
            holder.transform.localScale = Vector3.one * scale;
            holder.transform.localRotation = lean * Quaternion.Euler(90f, 0f, 0f);
            holder.transform.localPosition = at + Vector3.up * (TubeHalf + Sticks + nock * scale);
        }

        private static Transform FindBone(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindBone(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>A plain tinted material: the game's own wood item material with its picture taken off (as Quad's Cigars does).</summary>
        private static void MakeMaterials()
        {
            if (_leather != null && _dark != null) return;
            Material basis = null;
            GameObject wood = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Wood") : null;
            if (wood != null)
                foreach (Renderer r in wood.GetComponentsInChildren<Renderer>(true))
                    basis = basis ?? r.sharedMaterials.FirstOrDefault(m => m != null && m.name == "wood_item");
            basis = basis ?? new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
            _leather = Plain(basis, new Color(0.40f, 0.24f, 0.12f));
            _dark = Plain(basis, new Color(0.17f, 0.10f, 0.06f));
        }

        private static Material Plain(Material basis, Color color)
        {
            var m = new Material(basis) { color = color };
            if (m.HasProperty("_MainTex")) m.mainTexture = null;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }
    }
}
