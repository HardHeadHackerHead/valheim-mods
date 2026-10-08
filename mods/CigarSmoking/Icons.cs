using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>The pictures of the items and pieces: drawn offline by tools/modelkit and kept inside the mod (icons/*.png).</summary>
    internal static class Icons
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        // Texture2D.LoadImage lives in a module the build does not reference directly, so it is called by name.
        private static readonly MethodInfo LoadImage =
            System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        internal static Sprite Get(string name)
        {
            if (Cache.TryGetValue(name, out Sprite cached) && cached != null) return cached;
            Sprite sprite = null;
            try
            {
                using (System.IO.Stream s = typeof(Icons).Assembly.GetManifestResourceStream("icons/" + name + ".png"))
                {
                    if (s != null && LoadImage != null)
                    {
                        var bytes = new byte[s.Length];
                        s.Read(bytes, 0, bytes.Length);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if ((bool)LoadImage.Invoke(null, new object[] { tex, bytes }))
                            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[" + Plugin.Name + "] could not load the icon " + name + ": " + e.Message); }
            Cache[name] = sprite;
            return sprite;
        }

        internal static void Clear() => Cache.Clear();
    }
}
