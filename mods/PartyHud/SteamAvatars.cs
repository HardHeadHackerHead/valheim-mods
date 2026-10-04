using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace PartyHud
{
    /// <summary>
    /// Looks up a player's Steam profile picture from their Steam ID (the game already tells us everyone's ID).
    /// Pictures that aren't downloaded yet are requested from Steam and picked up a moment later.
    /// </summary>
    internal static class SteamAvatars
    {
        private static readonly Dictionary<ulong, Texture2D> Cache = new Dictionary<ulong, Texture2D>();
        private static readonly Dictionary<ulong, float> NextTry = new Dictionary<ulong, float>();

        /// <summary>The avatar, or null if it isn't available (yet). Safe to call every frame.</summary>
        public static Texture2D Get(ulong steamId)
        {
            if (steamId == 0) return null;
            if (Cache.TryGetValue(steamId, out Texture2D cached)) return cached;

            // Don't ask Steam more than about once a second per player.
            if (NextTry.TryGetValue(steamId, out float next) && Time.unscaledTime < next) return null;
            NextTry[steamId] = Time.unscaledTime + 1.2f;

            try
            {
                var id = new CSteamID(steamId);
                int handle = SteamFriends.GetMediumFriendAvatar(id); // 64x64
                if (handle == -1)
                {
                    // Not downloaded yet: ask Steam to fetch it; we'll find it on a later try.
                    SteamFriends.RequestUserInformation(id, false);
                    return null;
                }
                if (handle == 0) return null; // this account has no picture

                if (!SteamUtils.GetImageSize(handle, out uint width, out uint height) || width == 0 || height == 0) return null;

                var rgba = new byte[width * height * 4];
                if (!SteamUtils.GetImageRGBA(handle, rgba, rgba.Length)) return null;

                // Steam's rows run top to bottom; Unity's textures run bottom to top, so flip.
                int stride = (int)width * 4;
                var flipped = new byte[rgba.Length];
                for (int row = 0; row < height; row++)
                    Buffer.BlockCopy(rgba, row * stride, flipped, (int)(height - 1 - row) * stride, stride);

                var texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                texture.LoadRawTextureData(flipped);
                texture.Apply();
                Cache[steamId] = texture;
                return texture;
            }
            catch (Exception)
            {
                return null; // Steam not running / not initialised: show the coloured initial instead
            }
        }

        /// <summary>This player's own Steam ID, or 0 if Steam isn't available.</summary>
        public static ulong Own()
        {
            try { return SteamUser.GetSteamID().m_SteamID; }
            catch (Exception) { return 0; }
        }

        public static void Clear()
        {
            foreach (Texture2D t in Cache.Values) if (t != null) UnityEngine.Object.Destroy(t);
            Cache.Clear();
            NextTry.Clear();
        }
    }
}
