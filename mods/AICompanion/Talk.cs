using System;
using System.Collections;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace AICompanion
{
    /// <summary>
    /// Talking to a companion in chat: start a message with its name ("Rádvar, follow me", "Rádvar go home", "rádvar: be careful") and it
    /// does what you ask, and answers above its head. With a Jev key, Jev understands the message (a choice between the things it can do);
    /// without one, plain words do (follow, stay, guard, home, come here, aggressive, careful, passive, your things). The message still goes
    /// to chat as usual. Your own companions, or another player's that lets friends give it orders.
    /// </summary>
    internal static class Talk
    {
        private static readonly (string id, string meaning, string[] words, string reply)[] Intents =
        {
            ("follow", "Follow the player and fight beside them (go on an adventure together).", new[] { "follow", "come with", "with me", "let's go", "lets go", "adventure", "join" }, "Right behind you."),
            ("come", "Come to the player right now.", new[] { "come here", "come", "here", "to me", "over here" }, "Coming!"),
            ("stay", "Stay where it is.", new[] { "stay", "wait", "hold", "stop" }, "I'll wait here."),
            ("guard", "Guard the spot where it stands.", new[] { "guard", "watch", "protect this" }, "Nothing gets past me."),
            ("home", "Go home and live its own life there (gather, store, craft).", new[] { "go home", "home", "live", "gather", "work", "chop", "mine" }, "Heading home."),
            ("aggressive", "Fight aggressively.", new[] { "aggressive", "attack everything", "kill", "go wild" }, "Let them come!"),
            ("defensive", "Fight defensively, stay close and careful.", new[] { "defensive", "careful", "be careful", "stay close" }, "I'll be careful."),
            ("passive", "Avoid fighting.", new[] { "passive", "don't fight", "dont fight", "no fighting", "peace" }, "I won't start anything."),
            ("balanced", "Fight normally.", new[] { "balanced", "normal", "fight normally" }, "As usual, then."),
            ("grave", "Go and get its things back from its tombstone.", new[] { "grave", "tombstone", "your stuff", "your things", "your gear" }, "I'll go get my things."),
        };

        /// <summary>Called for every chat line the local player sends.</summary>
        public static void Heard(string text)
        {
            Player me = Player.m_localPlayer;
            if (me == null || string.IsNullOrWhiteSpace(text)) return;
            string t = text.Trim();
            foreach (Humanoid c in Companion.All().Where(h => Companion.CanCommand(h, me) && Vector3.Distance(h.transform.position, me.transform.position) < 150f))
            {
                string name = Companion.NameOf(c);
                if (!StartsWithName(t, name)) continue;
                string rest = t.Substring(name.Length).TrimStart(',', ':', ' ', '!', '.').Trim();
                if (rest.Length == 0) { Say(c, "Yes?"); return; }
                if (!string.IsNullOrEmpty(Plugin.ApiKey.Value) && Plugin.UseJev.Value) Plugin.Instance.StartCoroutine(AskJev(c, me, rest));
                else Do(c, me, ByWords(rest), rest);
                return;
            }
        }

        private static bool StartsWithName(string text, string name)
        {
            if (text.Length < name.Length) return false;
            string head = text.Substring(0, name.Length);
            bool same = string.Compare(head, name, StringComparison.OrdinalIgnoreCase) == 0
                        || string.Compare(Plain(head), Plain(name), StringComparison.OrdinalIgnoreCase) == 0; // "Radvar" works for "Rádvar"
            return same && (text.Length == name.Length || !char.IsLetterOrDigit(text[name.Length]));
        }

        private static string Plain(string s) => new string(s.Normalize(NormalizationForm.FormD).Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

        private static string ByWords(string rest)
        {
            string r = rest.ToLowerInvariant();
            foreach (var i in Intents) if (i.words.Any(w => r.Contains(w))) return i.id;
            return null;
        }

        private static IEnumerator AskJev(Humanoid c, Player me, string rest)
        {
            var criteria = new JObject();
            foreach (var i in Intents) criteria[i.id] = i.meaning;
            criteria["none"] = "None of these (small talk, or something it cannot do).";
            var body = new JObject
            {
                ["state"] = new JObject { ["player_says_to_companion"] = rest, ["companion"] = Companion.NameOf(c), ["doing_now"] = Companion.StatusOf(c) },
                ["model"] = Plugin.Model.Value,
                ["questions"] = new JObject { ["intent"] = new JObject { ["type"] = "choice", ["instructions"] = "What is the player asking the companion to do?", ["criteria"] = criteria } },
            };
            string intent = null;
            using (var req = new UnityWebRequest(Plugin.Endpoint.Value, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + Plugin.ApiKey.Value.Trim());
                req.timeout = Mathf.CeilToInt(Plugin.Timeout.Value);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        JObject a = JObject.Parse(req.downloadHandler.text)["answers"]?["intent"] as JObject;
                        string choice = (string)a?["choice"];
                        float p = (float?)a?["probabilities"]?[choice ?? ""] ?? 0f;
                        if (choice != null && choice != "none" && p >= 0.3f) intent = choice;
                    }
                    catch (Exception) { }
                }
            }
            if (c == null) yield break;
            Do(c, me, intent ?? ByWords(rest), rest);
        }

        private static void Do(Humanoid c, Player me, string intent, string said)
        {
            var match = Intents.FirstOrDefault(i => i.id == intent);
            if (intent == null || match.id == null) { Say(c, "Hm? I don't follow."); return; }
            bool ok = true;
            switch (intent)
            {
                case "follow": ok = Companion.Write(c, z => z.Set(Keys.Order, (int)Order.Follow)); break;
                case "come":
                    ok = Companion.Write(c, z => z.Set(Keys.Order, (int)Order.Follow));
                    if (ok && Vector3.Distance(c.transform.position, me.transform.position) > 15f) Brain.TeleportBehind(c, me, "came to");
                    break;
                case "stay": ok = Companion.Write(c, z => z.Set(Keys.Order, (int)Order.Stay)); break;
                case "guard": ok = Companion.Write(c, z => { z.Set(Keys.Order, (int)Order.Guard); z.Set(Keys.Post, c.transform.position); }); break;
                case "home": ok = Companion.Write(c, z => { z.Set(Keys.Order, (int)Order.Gather); if (!z.GetBool(Keys.HasBed, false)) z.Set(Keys.Post, c.transform.position); }); break;
                case "aggressive": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Aggressive)); break;
                case "defensive": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Defensive)); break;
                case "passive": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Passive)); break;
                case "balanced": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Balanced)); break;
                case "grave":
                    if (!Grave.Has(c)) { Say(c, "I have nothing lying in a grave."); return; }
                    Brain.Get(c).NextGraveLook = 0f;
                    break;
            }
            if (!ok) { Say(c, "Not now, someone is going through my things."); return; }
            Say(c, match.reply);
            Brain.Get(c)?.Remember($"you said \"{said}\": {intent}");
            Plugin.Instance?.Note($"{me.GetPlayerName()} told {Companion.NameOf(c)} \"{said}\" -> {intent}");
        }

        private static void Say(Humanoid c, string text)
        {
            if (Chat.instance != null) Chat.instance.SetNpcText(c.gameObject, Vector3.up * 2.3f, 30f, 4f, "", text, false);
        }
    }

    [HarmonyPatch(typeof(Chat), nameof(Chat.SendText))]
    internal static class Chat_SendText
    {
        private static void Postfix(string text)
        {
            try { Talk.Heard(text); }
            catch (Exception e) { Plugin.Instance?.Warn("Companion chat: " + e.Message); }
        }
    }
}
