using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// A player as the scripts know them: by their connection, not by a character loaded on this game. The game only loads another
    /// player's character within its own active area (about a hundred metres, and not at all on a dedicated server), but the server holds
    /// every player's world record wherever they are: their position, health and death. So a player a long way off is still in
    /// players(), still hears their chat, still gets items, effects and messages (their own game does those), and their events still count.
    /// </summary>
    internal sealed class Who
    {
        /// <summary>The connection whose game owns this player on the server; 0 for this game's own player.</summary>
        internal readonly long Peer;
        internal readonly long PlayerId;
        internal readonly string Name;
        internal readonly ZDOID Character;

        internal Who(long peer, long playerId, string name, ZDOID character) { Peer = peer; PlayerId = playerId; Name = name ?? ""; Character = character; }

        internal string Id => "p" + PlayerId;

        /// <summary>A player on another game, while this game runs the world (their game does what is asked of them).</summary>
        internal bool Remote => Peer != 0L && ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>Their character, when it is loaded on this game (near this game's own player), else null.</summary>
        internal Player Loaded
        {
            get
            {
                if (Peer == 0L) return Player.m_localPlayer;
                foreach (Player p in Player.GetAllPlayers()) if (p != null && p.GetPlayerID() == PlayerId) return p;
                return null;
            }
        }

        internal ZDO Zdo => Character.IsNone() || ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(Character);

        internal Vector3 Pos
        {
            get
            {
                Player p = Loaded;
                if (p != null) return p.transform.position;
                ZDO z = Zdo;
                if (z != null) return z.GetPosition();
                ZNetPeer peer = ZNet.instance != null && Peer != 0L ? ZNet.instance.GetPeer(Peer) : null;
                return peer != null ? peer.m_refPos : Vector3.zero;
            }
        }

        internal float Health { get { Player p = Loaded; if (p != null) return p.GetHealth(); ZDO z = Zdo; return z != null ? z.GetFloat(ZDOVars.s_health, 0f) : 0f; } }
        internal float MaxHealth { get { Player p = Loaded; if (p != null) return p.GetMaxHealth(); ZDO z = Zdo; return z != null ? z.GetFloat(ZDOVars.s_maxHealth, 25f) : 0f; } }
        internal bool Dead { get { Player p = Loaded; if (p != null) return p.IsDead(); ZDO z = Zdo; return z == null || z.GetBool(ZDOVars.s_dead); } }
        internal Vector3 Facing { get { Player p = Loaded; if (p != null) return p.GetLookDir(); ZDO z = Zdo; return z != null ? z.GetRotation() * Vector3.forward : Vector3.forward; } }
    }

    internal static class Roster
    {
        /// <summary>Everyone in the world: this game's own player, and on the server every connected player who has a character.</summary>
        internal static List<Who> All()
        {
            var list = new List<Who>();
            Player me = Player.m_localPlayer;
            if (me != null && me.GetPlayerID() != 0L) list.Add(new Who(0L, me.GetPlayerID(), me.GetPlayerName(), me.GetZDOID()));
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return list;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer == null || !peer.IsReady() || peer.m_characterID.IsNone()) continue;
                long id = peer.m_playerID;
                if (id == 0L) { ZDO z = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null; if (z != null) id = z.GetLong(ZDOVars.s_playerID, 0L); }
                if (id == 0L || list.Any(w => w.PlayerId == id)) continue;
                list.Add(new Who(peer.m_uid, id, peer.m_playerName, peer.m_characterID));
            }
            return list;
        }

        /// <summary>A player by handle id ("p" and their player id), or null when they are not in the world.</summary>
        internal static Who Of(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] != 'p') return null;
            foreach (Who w in All()) if (w.Id == id) return w;
            return null;
        }

        /// <summary>The player behind a loaded character (this game's own, or another player's loaded nearby).</summary>
        internal static Who Of(Player p)
        {
            if (p == null) return null;
            if (p == Player.m_localPlayer) return new Who(0L, p.GetPlayerID(), p.GetPlayerName(), p.GetZDOID());
            long id = p.GetPlayerID();
            foreach (Who w in All()) if (w.PlayerId == id) return w;
            return new Who(p.GetOwner(), id, p.GetPlayerName(), p.GetZDOID());
        }

        /// <summary>The player a connection plays (this game's own id is its own player).</summary>
        internal static Who OfPeer(long peer)
        {
            if (ZNet.instance != null && peer == ZNet.GetUID()) return All().FirstOrDefault(w => w.Peer == 0L);
            return All().FirstOrDefault(w => w.Peer == peer);
        }

        internal static Who Named(string name) => All().FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Is any player within `range` of a place (wherever they are, not only near this game's own).</summary>
        internal static bool Near(Vector3 pos, float range) => All().Any(w => Vector3.Distance(w.Pos, pos) <= range);
    }
}
