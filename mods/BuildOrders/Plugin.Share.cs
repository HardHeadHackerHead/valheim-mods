using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Sharing blueprints between players. Share sends a blueprint to everyone in the world over the game's own connection; it arrives in their
    /// Plans window under "Shared with you" (kept in BepInEx/blueprints/_inbox until they save, place or dismiss it). A share code is the same
    /// blueprint as text, to paste in a chat when you are not playing together. A shared blueprint is only a list of pieces: it is placed only
    /// by you, where you look, it is capped in size, and its file name is cleaned.
    /// </summary>
    public partial class Plugin
    {
        private const string CodePrefix = "BO1:";
        private const int MaxShareBytes = 2 * 1024 * 1024;   // a blueprint's text, unpacked
        private static string InboxDir => Path.Combine(BlueprintDir, "_inbox");

        internal class SharedBlueprint
        {
            public string Path, Name, From;
            public int Pieces;
            public List<KeyValuePair<string, int>> Materials = new List<KeyValuePair<string, int>>();
            public Texture2D Picture;
        }

        private List<SharedBlueprint> _inbox = new List<SharedBlueprint>();
        private float _inboxAt = -99f;

        // ---- packing ----

        private static string ToCode(JObject doc)
        {
            byte[] raw = Encoding.UTF8.GetBytes(doc.ToString(Formatting.None));
            return CodePrefix + Convert.ToBase64String(Utils.Compress(raw));
        }

        /// <summary>A blueprint from a share code, made safe to keep; null (and why) if it is not one.</summary>
        private static JObject FromCode(string code, out string error)
        {
            error = null;
            try
            {
                code = (code ?? "").Trim();
                int at = code.IndexOf(CodePrefix, StringComparison.Ordinal);
                if (at < 0) { error = "that is not a blueprint share code"; return null; }
                string b64 = new string(code.Substring(at + CodePrefix.Length).Where(c => !char.IsWhiteSpace(c)).ToArray());
                byte[] packed = Convert.FromBase64String(b64);
                if (packed.Length > MaxShareBytes) { error = "too big"; return null; }
                byte[] raw = Utils.Decompress(packed);
                if (raw.Length > MaxShareBytes) { error = "too big"; return null; }
                JObject doc = JObject.Parse(Encoding.UTF8.GetString(raw));
                if (!(doc["pieces"] is JArray pieces) || pieces.Count == 0) { error = "it has no pieces"; return null; }
                if (pieces.Count > 1500) { error = "it has more than 1500 pieces"; return null; }
                return doc;
            }
            catch (Exception e) { error = "the code is damaged (" + e.Message + ")"; return null; }
        }

        /// <summary>What a received blueprint may do: be placed where you look, by you. Nothing else from the sender is kept.</summary>
        private static JObject Tame(JObject doc, string from)
        {
            var clean = new JObject
            {
                ["name"] = ((string)doc["name"] ?? "Shared blueprint").Trim(),
                ["anchor"] = "look",
                ["yaw"] = 0,
                ["offset"] = doc["offset"] is JArray o && o.Count == 3 ? o : new JArray(0, 0, 0),
                ["sharedBy"] = from,
                ["pieces"] = new JArray(((JArray)doc["pieces"]).Take(1500).OfType<JObject>()
                    .Where(p => p["p"] != null)
                    .Select(p => new JObject(p.Properties().Where(q => q.Name == "p" || q.Name == "x" || q.Name == "y" || q.Name == "z" || q.Name == "rx" || q.Name == "ry" || q.Name == "rz" || q.Name == "g")))),
            };
            if (clean["name"].ToString().Length > 60) clean["name"] = clean["name"].ToString().Substring(0, 60);
            return clean;
        }

        private static string SafeFileName(string name)
        {
            string s = new string((name ?? "blueprint").Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_').ToArray()).Trim();
            s = s.Replace(' ', '_').ToLowerInvariant();
            if (s.Length == 0) s = "blueprint";
            return s.Length > 50 ? s.Substring(0, 50) : s;
        }

        private static string FreePath(string dir, string baseName)
        {
            string path = Path.Combine(dir, baseName + ".json");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, baseName + "_" + n + ".json");
            return path;
        }

        // ---- sending ----

        private void ShareWithEveryone(string file)
        {
            Player me = Player.m_localPlayer;
            if (me == null) return;
            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(file));
                string code = ToCode(Tame(doc, me.GetPlayerName()));
                int others = ZNet.instance != null ? ZNet.instance.GetPlayerList().Count - 1 : 0;
                byte[] pic = PictureForSharing(file);   // the picture goes along (a share code is text only: it gets a picture made on arrival)
                Send("B|" + me.GetPlayerName().Replace("|", "/") + "|" + code + (pic != null ? "|" + Convert.ToBase64String(pic) : ""));
                PlansToast(others > 0 ? $"Shared \"{(string)doc["name"]}\" with {others} other player(s) here" : $"Shared \"{(string)doc["name"]}\" (nobody else is here right now)");
            }
            catch (Exception e) { PlansToast("Could not share: " + e.Message); }
        }

        private void CopyCode(string file)
        {
            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(file));
                string code = ToCode(Tame(doc, Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "someone"));
                GUIUtility.systemCopyBuffer = code;
                PlansToast(code.Length <= 2000 ? $"Share code copied ({code.Length} characters): paste it in a chat"
                                               : $"Share code copied, but it is long ({code.Length} characters): send the file from the blueprints folder instead if the chat cuts it off");
            }
            catch (Exception e) { PlansToast("Could not make a share code: " + e.Message); }
        }

        private void PasteCode()
        {
            JObject doc = FromCode(GUIUtility.systemCopyBuffer, out string error);
            if (doc == null) { PlansToast("Nothing pasted: " + error + ". Copy a code starting with " + CodePrefix + " first"); return; }
            string from = (string)doc["sharedBy"] ?? "a share code";
            JObject clean = Tame(doc, from);
            try
            {
                Directory.CreateDirectory(BlueprintDir);
                string path = FreePath(BlueprintDir, SafeFileName((string)clean["name"]));
                File.WriteAllText(path, clean.ToString(Formatting.Indented));
                _libraryAt = -99f;
                PlansToast($"Added \"{clean["name"]}\" to your blueprints");
            }
            catch (Exception e) { PlansToast("Could not save it: " + e.Message); }
        }

        // ---- receiving ----

        private void OnSharedBlueprint(string payload)
        {
            string[] parts = payload.Split(new[] { '|' }, 3);   // who | the blueprint | its picture (optional)
            if (parts.Length < 2 || parts[0].Length == 0) return;
            string from = parts[0];
            if (from.Length > 40) from = from.Substring(0, 40);
            JObject doc = FromCode(parts[1], out string error);
            if (doc == null) { Logger.LogWarning($"A blueprint shared by {from} could not be read: {error}"); return; }
            JObject clean = Tame(doc, from);
            Directory.CreateDirectory(InboxDir);
            string path = FreePath(InboxDir, SafeFileName((string)clean["name"]) + "__from_" + SafeFileName(from));
            File.WriteAllText(path, clean.ToString(Formatting.Indented));
            if (parts.Length > 2) SaveSharedPicture(path, parts[2]);
            _inboxAt = -99f;
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"{from} shared a blueprint: \"{clean["name"]}\" ({_blueprintKey.Value} to see it)");
            Logger.LogInfo($"{from} shared the blueprint '{clean["name"]}' ({((JArray)clean["pieces"]).Count} pieces): {path}");
        }

        private void RefreshInbox()
        {
            if (Time.unscaledTime - _inboxAt < 3f) return;
            _inboxAt = Time.unscaledTime;
            var list = new List<SharedBlueprint>();
            if (Directory.Exists(InboxDir))
                foreach (string file in Directory.GetFiles(InboxDir, "*.json").OrderByDescending(File.GetLastWriteTime))
                {
                    try
                    {
                        JObject doc = JObject.Parse(File.ReadAllText(file));
                        List<Entry> entries = EntriesFrom(doc["pieces"] as JArray ?? new JArray());
                        list.Add(new SharedBlueprint
                        {
                            Path = file, Name = (string)doc["name"] ?? Path.GetFileNameWithoutExtension(file), From = (string)doc["sharedBy"] ?? "someone",
                            Pieces = entries.Count, Materials = MaterialsOf(entries.Select(e => e.Prefab)),
                        });
                        string pic = PictureFile(file);
                        if (pic != null) list[list.Count - 1].Picture = Picture(pic); else WantPicture(file);
                    }
                    catch (Exception) { }
                }
            _inbox = list;
        }

        private void SaveShared(SharedBlueprint s)
        {
            try
            {
                string path = FreePath(BlueprintDir, SafeFileName(s.Name));
                File.Move(s.Path, path);
                string pic = PictureFile(s.Path);
                if (pic != null) File.Move(pic, Path.ChangeExtension(path, Path.GetExtension(pic)));
                _inboxAt = -99f; _libraryAt = -99f;
                PlansToast($"Saved \"{s.Name}\" to your blueprints");
            }
            catch (Exception e) { PlansToast("Could not save it: " + e.Message); }
        }

        private void DismissShared(SharedBlueprint s)
        {
            try { string pic = PictureFile(s.Path); File.Delete(s.Path); if (pic != null) File.Delete(pic); } catch (Exception) { }
            _inboxAt = -99f;
        }

        private void PlaceShared(Player player, SharedBlueprint s)
        {
            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(s.Path));
                StartPlacement(s.Name, s.Path, EntriesFrom(doc["pieces"] as JArray ?? new JArray()), player.transform.eulerAngles.y, 0f, null);
            }
            catch (Exception e) { PlansToast("Could not read it: " + e.Message); }
        }
    }
}
