using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// For a helper that builds the plans for you (a companion at home): three functions published through AppDomain data, as BuildFromChests
    /// does for its chests, so no types cross between the mods. The helper pays for the pieces itself; these never touch your materials.
    ///   DHack.BuildOrders.Count (Vector3 centre, float radius) -> int: the planned pieces within that distance of the centre;
    ///   DHack.BuildOrders.Next  (Vector3 centre, float radius, int max) -> string[] "id|prefab|x|y|z": up to max planned pieces to build next,
    ///     in the order to build them, that will stand with what is built, whose station is in reach of the spot and that you have unlocked;
    ///   DHack.BuildOrders.Place (string id) -> bool: build that planned piece now (as you, with no materials taken), and mark it done.
    /// They work on the game of the player whose ghosts they are (the local player), and only for ghosts that are showing.
    /// </summary>
    public partial class Plugin
    {
        private const string HelperCountKey = "DHack.BuildOrders.Count", HelperNextKey = "DHack.BuildOrders.Next", HelperPlaceKey = "DHack.BuildOrders.Place";
        private Func<Vector3, float, int> _helperCount;
        private Func<Vector3, float, int, string[]> _helperNext;
        private Func<string, bool> _helperPlace;

        private void PublishHelperHooks()
        {
            _helperCount = HelperCount; _helperNext = HelperNext; _helperPlace = HelperPlace;
            AppDomain.CurrentDomain.SetData(HelperCountKey, _helperCount);
            AppDomain.CurrentDomain.SetData(HelperNextKey, _helperNext);
            AppDomain.CurrentDomain.SetData(HelperPlaceKey, _helperPlace);
        }

        private void RemoveHelperHooks()
        {
            if (ReferenceEquals(AppDomain.CurrentDomain.GetData(HelperCountKey), _helperCount)) AppDomain.CurrentDomain.SetData(HelperCountKey, null);
            if (ReferenceEquals(AppDomain.CurrentDomain.GetData(HelperNextKey), _helperNext)) AppDomain.CurrentDomain.SetData(HelperNextKey, null);
            if (ReferenceEquals(AppDomain.CurrentDomain.GetData(HelperPlaceKey), _helperPlace)) AppDomain.CurrentDomain.SetData(HelperPlaceKey, null);
        }

        private int HelperCount(Vector3 centre, float radius)
        {
            if (!WorldKnown || !_enabled.Value) return 0;
            float r2 = radius * radius;
            return _orders.Values.Count(o => (o.Pos - centre).sqrMagnitude <= r2);
        }

        /// <summary>Whether a helper may build this order now: the piece exists, you have unlocked it, the ground is not protected, and its station is by it.</summary>
        private bool HelperMayBuild(Player player, Order o, out Piece piece)
        {
            piece = PieceOf(o);
            if (piece == null || !player.IsRecipeKnown(piece.m_name)) return false;
            if (!PrivateArea.CheckAccess(o.Pos, 0f, false, false) || Location.IsInsideNoBuildLocation(o.Pos)) return false;
            return piece.m_craftingStation == null || CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, o.Pos) != null;
        }

        private string[] HelperNext(Vector3 centre, float radius, int max)
        {
            Player player = Player.m_localPlayer;
            if (!ApiReady(player, out _) || max <= 0) return new string[0];
            float r2 = radius * radius;
            List<Order> batch = _orders.Values
                .Where(o => (o.Pos - centre).sqrMagnitude <= r2 && _ghosts.ContainsKey(o.Id) && HelperMayBuild(player, o, out _))
                .OrderBy(o => Mathf.Round(o.Pos.y * 2f)).ThenBy(o => (o.Pos - centre).sqrMagnitude).Take(600).ToList();
            if (batch.Count == 0) return new string[0];
            List<Order> ordered = StandingOrder(player, batch, out _);
            return ordered.Take(max).Select(o => string.Join("|", new[] { o.Id, o.Prefab, F(o.Pos.x), F(o.Pos.y), F(o.Pos.z) })).ToArray();
        }

        private bool HelperPlace(string id)
        {
            Player player = Player.m_localPlayer;
            if (!ApiReady(player, out _) || id == null || !_orders.TryGetValue(id, out Order o) || !HelperMayBuild(player, o, out Piece piece)) return false;
            try { player.PlacePiece(piece, o.Pos, o.Rot, doAttack: false); }
            catch (Exception e) { Logger.LogError($"A helper could not build {o.Prefab}: {e}"); return false; }
            RemoveOrder(o.Id, broadcast: true);
            _stabilityDirty = true;
            return true;
        }
    }
}
