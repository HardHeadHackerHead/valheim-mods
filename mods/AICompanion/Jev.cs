using System;
using System.Collections;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace AICompanion
{
    /// <summary>
    /// Asking Jev (TypeSafe's decision model) how to fight. One request carries the fight as `state` and up to four typed questions, which
    /// Jev answers together: what to do (choice), whom to hit (choice, when there is more than one enemy), bow or melee (choice, when it
    /// has both), and whether to drink a potion now (yes/no, when it has one). If Jev is unsure, fails or is slow, the built-in decision is
    /// used for that round. API: POST {Endpoint} with "Authorization: Bearer KEY" (docs.typesafe.ai/api).
    /// </summary>
    internal static class Jev
    {
        public static int Decisions, Failures, InRow;
        public static long Tokens;
        public static float LastMs, BlockedUntil;
        public static string LastError = "", LastModel = "";
        public static string Day = DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>False for a few seconds after repeated failures (a bad key, no internet), so we do not hammer the service.</summary>
        public static bool Ready => Time.time >= BlockedUntil;

        public static float Cost => Tokens * Plugin.PricePerMillion.Value / 1_000_000f;

        private static readonly JObject Actions = new JObject
        {
            ["attack"] = "Fight the chosen target with the current weapon.",
            ["defend_player"] = "Stay by the player and hit whatever is attacking them.",
            ["back_off"] = "Shield up and step back from the nearest enemy, to recover or avoid a big hit, then fight on.",
            ["retreat_to_player"] = "Fall back to the player's side with the shield up.",
            ["flee"] = "Run away from the fight. Only when about to die or hopelessly outmatched.",
        };

        public static IEnumerator Decide(BrainState st, Player master, Decision fallback, Action<Decision> done)
        {
            Humanoid me = st.Body;
            var enemies = st.Enemies.Take(8).ToList();
            ItemDrop.ItemData melee = Companion.BestMelee(me), ranged = Companion.BestRanged(me);
            int potions = Companion.HealingPotions(me).Sum(i => i.m_stack);
            Style style = Companion.StyleOf(me);

            var state = new JObject
            {
                ["situation"] = "Valheim (a viking survival game). You decide the tactics of an AI companion fighting beside the player. The game carries them out.",
                ["style"] = StyleText(style),
                ["rules"] = new JObject
                {
                    ["retreat_below_health_pct"] = Companion.RetreatOf(me),
                    ["protect_player_first"] = Companion.Protect(me),
                },
                ["companion"] = new JObject
                {
                    ["health_pct"] = Pct(me.GetHealthPercentage()),
                    ["max_health"] = Mathf.RoundToInt(me.GetMaxHealth()),
                    ["armor"] = Mathf.RoundToInt(Companion.Armor(me)),
                    ["melee_weapon"] = melee != null ? $"{Loc(melee)} ({Mathf.RoundToInt(melee.GetDamage().GetTotalDamage())} damage)" : "none (fists)",
                    ["bow"] = ranged != null ? $"{Loc(ranged)} with {me.GetInventory().CountItems(me.GetInventory().GetAmmoItem(ranged.m_shared.m_ammoType).m_shared.m_name)} arrows" : "none",
                    ["has_shield"] = Companion.Shield(me) != null,
                    ["healing_potions"] = potions,
                    ["doing_now"] = st.Current.Describe(st.Label),
                },
                ["player"] = master == null ? (JToken)"not here" : new JObject
                {
                    ["health_pct"] = Pct(master.GetHealthPercentage()),
                    ["distance_m"] = Round(Vector3.Distance(master.transform.position, me.transform.position)),
                },
                ["enemies"] = new JArray(enemies.Select(e => (JToken)new JObject
                {
                    ["id"] = st.Label(e),
                    ["stars"] = Mathf.Max(0, e.GetLevel() - 1),
                    ["health_pct"] = Pct(e.GetHealthPercentage()),
                    ["max_health"] = Mathf.RoundToInt(e.GetMaxHealth()),
                    ["distance_m"] = Round(Vector3.Distance(e.transform.position, me.transform.position)),
                    ["distance_to_player_m"] = master != null ? Round(Vector3.Distance(e.transform.position, master.transform.position)) : -1,
                    ["attacking"] = Targeting(e, me, master),
                })),
            };

            var questions = new JObject
            {
                ["action"] = new JObject { ["type"] = "choice", ["instructions"] = "What should the companion do for the next couple of seconds?", ["criteria"] = Actions },
            };
            if (enemies.Count > 1)
            {
                var options = new JObject();
                foreach (Character e in enemies) options[st.Label(e)] = $"{Pct(e.GetHealthPercentage())}% health, {Round(Vector3.Distance(e.transform.position, me.transform.position))} m away, attacking {Targeting(e, me, master)}";
                questions["target"] = new JObject { ["type"] = "choice", ["instructions"] = "Which enemy should the companion fight (if it fights)?", ["criteria"] = options };
            }
            if (melee != null && ranged != null)
                questions["weapon"] = new JObject
                {
                    ["type"] = "choice", ["instructions"] = "Which weapon should the companion use?",
                    ["criteria"] = new JObject { ["melee"] = $"{Loc(melee)}, up close", ["bow"] = $"{Loc(ranged)}, from a distance" },
                };
            if (potions > 0 && Companion.Potions(me) && me.GetHealthPercentage() < 0.9f)
                questions["drink"] = new JObject { ["type"] = "noul", ["instructions"] = "Should the companion drink a healing potion right now?" };

            var body = new JObject { ["state"] = state, ["model"] = Plugin.Model.Value, ["questions"] = questions };
            JObject answer = null;
            string error = null;
            float started = Time.realtimeSinceStartup;
            yield return Post(body, j => answer = j, e => error = e);
            LastMs = (Time.realtimeSinceStartup - started) * 1000f;

            if (answer == null)
            {
                Failed(error);
                fallback.Note = "Jev: " + error;
                done(fallback);
                yield break;
            }
            Succeeded(answer);

            var d = new Decision { FromJev = true, Ranged = fallback.Ranged, Target = fallback.Target };
            JObject answers = answer["answers"] as JObject;
            JObject act = answers?["action"] as JObject;
            string choice = (string)act?["choice"] ?? "";
            d.Confidence = (float?)act?["confidence"] ?? 0f;
            d.Action = choice switch
            {
                "attack" => Tactic.Attack, "defend_player" => Tactic.DefendPlayer, "back_off" => Tactic.BackOff,
                "retreat_to_player" => Tactic.Retreat, "flee" => Tactic.Flee, _ => fallback.Action,
            };
            if (answers?["target"] is JObject t) d.Target = enemies.FirstOrDefault(e => st.Label(e) == (string)t["choice"]) ?? fallback.Target;
            else if (enemies.Count == 1) d.Target = enemies[0];
            if (answers?["weapon"] is JObject w) d.Ranged = (string)w["choice"] == "bow";
            if (answers?["drink"] is JObject k) d.Drink = ((float?)k["noul"] ?? 0f) > 0.6f;

            if (d.Confidence < Plugin.MinConfidence.Value)
            {
                fallback.Note = $"Jev unsure ({d.Confidence * 100f:0}% {choice})";
                fallback.Drink |= d.Drink;
                done(fallback);
                yield break;
            }
            Plugin.Instance?.Note($"Jev ({LastMs:0} ms): {d.Describe(st.Label)}");
            done(d);
        }

        /// <summary>One tiny request, to check the key and the connection from the menu.</summary>
        public static IEnumerator Test(Action<string> result)
        {
            var body = new JObject
            {
                ["state"] = "A greyling is attacking the player.",
                ["model"] = Plugin.Model.Value,
                ["questions"] = new JObject { ["fight"] = new JObject { ["type"] = "noul", ["instructions"] = "Should the companion fight it?" } },
            };
            JObject answer = null;
            string error = null;
            float started = Time.realtimeSinceStartup;
            yield return Post(body, j => answer = j, e => error = e);
            LastMs = (Time.realtimeSinceStartup - started) * 1000f;
            if (answer == null) { Failed(error); result("Failed: " + error); yield break; }
            Succeeded(answer);
            result($"Connected: {LastModel}, {LastMs:0} ms");
        }

        private static IEnumerator Post(JObject body, Action<JObject> ok, Action<string> fail)
        {
            string key = Plugin.ApiKey.Value.Trim();
            if (string.IsNullOrEmpty(key)) { fail("no API key"); yield break; }
            using (var req = new UnityWebRequest(Plugin.Endpoint.Value, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + key);
                req.timeout = Mathf.CeilToInt(Plugin.Timeout.Value);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string text = req.downloadHandler?.text ?? "";
                    fail(req.responseCode switch
                    {
                        401 => "the API key was refused (401)",
                        422 => "the request was rejected (422): " + Short(text),
                        429 => "too many requests (429)",
                        529 => "Jev is overloaded (529)",
                        0 => req.error,
                        _ => $"{req.responseCode} {req.error} {Short(text)}",
                    });
                    yield break;
                }
                try { ok(JObject.Parse(req.downloadHandler.text)); }
                catch (Exception e) { fail("unreadable answer: " + e.Message); }
            }
        }

        private static void Succeeded(JObject answer)
        {
            NewDay();
            Decisions++;
            InRow = 0;
            LastError = "";
            LastModel = (string)answer["model"] ?? LastModel;
            Tokens += (long?)answer["usage"]?["input_tokens"] ?? 0L;
        }

        private static void Failed(string error)
        {
            NewDay();
            Failures++;
            InRow++;
            LastError = error ?? "unknown error";
            if (InRow >= 3) BlockedUntil = Time.time + 15f; // a bad key or no connection: try again in a while, the built-in brain fights meanwhile
            Plugin.Instance?.Warn("Jev: " + LastError);
        }

        private static void NewDay()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (today == Day) return;
            Day = today; Decisions = 0; Failures = 0; Tokens = 0;
        }

        private static string Targeting(Character e, Humanoid me, Player master)
        {
            Character t = e.GetBaseAI() is MonsterAI m ? m.GetTargetCreature() : null;
            return t == null ? "no one" : t == me ? "the companion" : t == master ? "the player" : t.IsPlayer() ? "another player" : "someone else";
        }

        private static string StyleText(Style s) => s switch
        {
            Style.Aggressive => "aggressive: press the attack, retreat only when badly hurt",
            Style.Defensive => "defensive: stay close to the player, fight what comes near, retreat early",
            Style.Passive => "passive: avoid fighting; keep safe by the player",
            _ => "balanced",
        };

        private static string Loc(ItemDrop.ItemData item) => Localization.instance.Localize(item.m_shared.m_name);
        private static int Pct(float f) => Mathf.RoundToInt(f * 100f);
        private static float Round(float f) => Mathf.Round(f * 10f) / 10f;
        private static string Short(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 160 ? s.Substring(0, 160) + "…" : s);
    }
}
