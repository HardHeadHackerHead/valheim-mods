using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BuildFromChests
{
    /// <summary>
    /// In the hammer's build menu each requirement box shows how many you NEED. This adds how many you HAVE (inventory plus
    /// every chest in range) in the opposite bottom corner, green when it's enough and orange when it isn't. Hovering the
    /// box shows the split between inventory and chests.
    /// </summary>
    internal static class HaveLabel
    {
        private const string LabelName = "BFC_Have";

        internal static void Show(Transform elementRoot, Piece.Requirement req, int quality, int multiplier)
        {
            Transform amount = elementRoot.Find("res_amount");
            if (amount == null) return;

            TMP_Text label = GetOrCreate(elementRoot, amount);
            if (label == null) return;

            string item = req.m_resItem.m_itemData.m_shared.m_name;
            Inventory inventory = Player.m_localPlayer.GetInventory();

            // The inventory count normally already includes the chests (that's what makes building work), so ask for it
            // with our own counting switched off to get the two parts separately.
            ChestScanner.Suspend = true;
            int inInventory;
            try { inInventory = inventory.CountItems(item); }
            finally { ChestScanner.Suspend = false; }
            int inChests = ChestScanner.CountInChests(item, -1, true);

            int total = inInventory + inChests;
            int need = req.GetAmount(quality) * multiplier;

            label.gameObject.SetActive(true);
            label.text = total.ToString();
            label.color = total >= need ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.62f, 0.45f);

            // The game rewrites the tooltip every frame, so add our line after it has.
            UITooltip tooltip = elementRoot.GetComponent<UITooltip>();
            if (tooltip != null && !string.IsNullOrEmpty(tooltip.m_text))
                tooltip.m_text += $"\nYou have {total}  (inventory {inInventory}, chests {inChests})";
        }

        internal static void Hide(Transform elementRoot)
        {
            Transform label = elementRoot != null ? elementRoot.Find(LabelName) : null;
            if (label != null) label.gameObject.SetActive(false);
        }

        /// <summary>Make the label by copying the game's own "needed" number and mirroring it to the other side of the box.</summary>
        private static TMP_Text GetOrCreate(Transform elementRoot, Transform amount)
        {
            Transform existing = elementRoot.Find(LabelName);
            if (existing != null) return existing.GetComponent<TMP_Text>();

            GameObject copy = UnityEngine.Object.Instantiate(amount.gameObject, elementRoot);
            copy.name = LabelName;

            var source = (RectTransform)amount;
            var target = (RectTransform)copy.transform;
            target.anchorMin = new Vector2(1f - source.anchorMax.x, source.anchorMin.y);
            target.anchorMax = new Vector2(1f - source.anchorMin.x, source.anchorMax.y);
            target.pivot = new Vector2(1f - source.pivot.x, source.pivot.y);
            target.anchoredPosition = new Vector2(-source.anchoredPosition.x, source.anchoredPosition.y);
            target.sizeDelta = source.sizeDelta;

            var label = copy.GetComponent<TMP_Text>();
            label.horizontalAlignment = HorizontalAlignmentOptions.Left;
            label.enableAutoSizing = false;
            label.fontSize = amount.GetComponent<TMP_Text>().fontSize * 0.9f;
            return label;
        }

        /// <summary>Remove every label we added (used when this mod is reloaded or unloaded).</summary>
        internal static void DestroyAll()
        {
            if (Hud.instance == null) return;
            foreach (TMP_Text t in Hud.instance.GetComponentsInChildren<TMP_Text>(true))
                if (t != null && t.gameObject.name == LabelName) UnityEngine.Object.Destroy(t.gameObject);
        }
    }

    // Runs after the game fills in one requirement box. The build menu and the crafting window share this method.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    internal static class InventoryGui_SetupRequirement
    {
        private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, int quality, int craftMultiplier, bool __result)
        {
            try
            {
                // Boxes inside the inventory screen are crafting (CraftFromChests' job); the rest are the build menu.
                bool inCraftingWindow = InventoryGui.instance != null && elementRoot.IsChildOf(InventoryGui.instance.transform);
                bool show = !inCraftingWindow && __result && Plugin.Enabled.Value && Plugin.ShowHaveCounts.Value
                            && req.m_resItem != null && player != null && player == Player.m_localPlayer
                            && ChestScanner.Applies(player.GetInventory());

                if (show) HaveLabel.Show(elementRoot, req, quality, craftMultiplier);
                else HaveLabel.Hide(elementRoot);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not show the 'you have' count: " + e.Message);
            }
        }
    }
}
