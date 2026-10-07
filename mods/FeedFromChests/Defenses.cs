using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>
    /// The things that look after a base, kept going from nearby chests, all at once (no setup per piece), while you are within PlayerRange:
    ///   * shield generators get their fuel (bones and the like, whatever the generator takes) one at a time as they have room, so the shield
    ///     never runs dry in a raid (Defenses, FuelShieldGenerators);
    ///   * ballistas are reloaded with the ammo they already hold, or when empty the first ammo they take that the chests have
    ///     (Defenses, ReloadBallistas);
    ///   * sap extractors empty themselves into a chest assigned to sap (K), or one that already holds sap, like beehives (Defenses, CollectSap).
    /// Fuel and ammo come from chests within FeedRadius and leave KeepFuel of each in the chests; it goes in through the piece's own messages,
    /// as when a player adds it. One player's game looks after each piece (its owner).
    /// </summary>
    internal static class Defenses
    {
        private static float _next, _summaryAt = -100f;

        public static void Tick(Plugin plugin, Player player, float range, float feedRadius, float outRadius, bool shields, bool ballistas, bool sap)
        {
            if (!shields && !ballistas && !sap) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 3f;
            float max = range * range;
            int sh = 0, ba = 0, sa = 0;

            if (shields)
                foreach (ShieldGenerator g in Object.FindObjectsOfType<ShieldGenerator>())
                {
                    if ((g.transform.position - player.transform.position).sqrMagnitude > max || !Mine(g)) continue;
                    ZDO z = g.GetComponent<ZNetView>().GetZDO();
                    float fuel = z.GetFloat(ZDOVars.s_fuel, g.m_defaultFuel);
                    if (fuel > g.m_maxFuel - 1f || g.m_fuelItems.Count == 0) continue;
                    List<Container> chests = Chests.Near(g.transform.position, feedRadius);
                    ItemDrop item = g.m_fuelItems.FirstOrDefault(f => f != null && Chests.Count(chests, f.m_itemData.m_shared.m_name) > plugin.KeepFuel);
                    if (item == null || Chests.Take(chests, item.m_itemData.m_shared.m_name, 1) < 1) continue;
                    g.GetComponent<ZNetView>().InvokeRPC("RPC_AddFuel");
                    sh++;
                }

            if (ballistas)
                foreach (Turret t in Object.FindObjectsOfType<Turret>())
                {
                    if (t.m_maxAmmo <= 0 || (t.transform.position - player.transform.position).sqrMagnitude > max || !Mine(t)) continue;
                    if (t.GetAmmo() >= t.m_maxAmmo) continue;
                    List<Container> chests = Chests.Near(t.transform.position, feedRadius);
                    string current = t.GetAmmo() > 0 ? t.GetAmmoType() : null; // it holds one kind at a time
                    ItemDrop ammo = t.m_allowedAmmo.Select(a => a.m_ammo).FirstOrDefault(a => a != null && (current == null || a.gameObject.name == current)
                                                                                                   && Chests.Count(chests, a.m_itemData.m_shared.m_name) > plugin.KeepFuel);
                    if (ammo == null || Chests.Take(chests, ammo.m_itemData.m_shared.m_name, 1) < 1) continue;
                    t.GetComponent<ZNetView>().InvokeRPC("RPC_AddAmmo", ammo.gameObject.name);
                    ba++;
                }

            if (sap)
                foreach (SapCollector c in Object.FindObjectsOfType<SapCollector>())
                {
                    if ((c.transform.position - player.transform.position).sqrMagnitude > max || !Mine(c) || c.m_spawnItem == null) continue;
                    ZNetView view = c.GetComponent<ZNetView>();
                    int level = view.GetZDO().GetInt(ZDOVars.s_level);
                    if (level <= 0) continue;
                    int stored = 0;
                    for (int i = 0; i < level; i++)
                    {
                        int count = Game.instance != null ? Game.instance.ScaleDrops(c.m_spawnItem.m_itemData, 1) : 1; // the world's resource rate, as when extracted
                        int sent = AutoFeed.SendItem(c.transform.position, c.m_spawnItem, count, outRadius, "Materials", plugin.Info, Localization.instance.Localize(c.m_name));
                        if (sent < count) sent += Chests.AddToChestHolding(c.transform.position, c.m_spawnItem, count - sent, outRadius);
                        if (sent < count) break; // nowhere to put it: the rest stays in the extractor
                        stored++;
                    }
                    if (stored > 0)
                    {
                        view.GetZDO().Set(ZDOVars.s_level, level - stored);
                        view.InvokeRPC(ZNetView.Everybody, "RPC_UpdateEffects");
                        sa += stored;
                    }
                }

            if (sh + ba + sa > 0 && Time.unscaledTime - _summaryAt > 60f)
            {
                _summaryAt = Time.unscaledTime;
                plugin.Info($"Defenses near you: {sh} shield fuel added, {ba} ballista bolts loaded, {sa} sap collected");
            }
        }

        /// <summary>One player's game looks after each piece: its owner, or this one if it has none.</summary>
        private static bool Mine(Component c)
        {
            ZNetView v = c.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return false;
            if (v.IsOwner()) return true;
            if (v.HasOwner()) return false;
            v.ClaimOwnership();
            return true;
        }
    }
}
