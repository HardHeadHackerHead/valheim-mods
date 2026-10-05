using System.Collections.Generic;
using UnityEngine;

namespace BountyBoard
{
    /// <summary>Builds the model described in ModelData.cs (made by tools/modelkit) out of Unity's boxes, cylinders and spheres.</summary>
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
                    go = new GameObject(name) { layer = layer };
                    groups[name] = go.transform;
                }
                else
                {
                    go = GameObject.CreatePrimitive(shape == "Cube" ? PrimitiveType.Cube : shape == "Cylinder" ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
                    go.name = name;
                    Object.DestroyImmediate(go.GetComponent<Collider>()); // one hitbox on the whole piece does the work
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
