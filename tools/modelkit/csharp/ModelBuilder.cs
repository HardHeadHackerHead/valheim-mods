using System.Collections.Generic;
using UnityEngine;

namespace YourMod
{
    /// <summary>
    /// Builds a model written by modelkit (Model.to_csharp: a class with a Parts table) out of Unity's boxes, cylinders, spheres and
    /// quads, dressed by Look. Copy this file and Look.cs into your mod and change the namespace.
    ///
    ///   Look.Harvest(ZNetScene.instance, fallbackMaterial);              // once the scene exists (ZNetScene.Awake postfix)
    ///   GameObject model = ModelBuilder.Build(prefab.transform, ModelData.Parts, prefab.layer);
    ///   model.transform.localScale = Vector3.one * ModelData.Scale;
    /// </summary>
    internal static class ModelBuilder
    {
        internal static GameObject Build(Transform parent, object[][] parts, int layer)
        {
            var root = new GameObject("Model") { layer = layer };
            root.transform.SetParent(parent, false);
            var groups = new Dictionary<string, Transform>();

            foreach (object[] p in parts)
            {
                string name = (string)p[0], shape = (string)p[1], group = (string)p[2];
                var pos = new Vector3((float)p[3], (float)p[4], (float)p[5]);
                var scale = new Vector3((float)p[6], (float)p[7], (float)p[8]);
                var euler = new Vector3((float)p[9], (float)p[10], (float)p[11]);
                Transform at = group.Length > 0 && groups.TryGetValue(group, out Transform g) ? g : root.transform;

                GameObject go;
                if (shape == "Group")
                {
                    go = new GameObject(name) { layer = layer }; // an empty parent: spin or move it to move its parts together
                    groups[name] = go.transform;
                }
                else
                {
                    PrimitiveType type = shape == "Cube" ? PrimitiveType.Cube : shape == "Cylinder" ? PrimitiveType.Cylinder
                                       : shape == "Quad" ? PrimitiveType.Quad : PrimitiveType.Sphere;
                    go = GameObject.CreatePrimitive(type);
                    go.name = name;
                    Object.DestroyImmediate(go.GetComponent<Collider>()); // give the whole piece one collider of its own instead
                    go.layer = layer;
                    go.GetComponent<Renderer>().sharedMaterial = Look.Of((string)p[12]);
                    go.transform.localScale = scale;
                }
                go.transform.SetParent(at, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(euler);
            }
            return root;
        }
    }
}
