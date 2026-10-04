using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CraftFromChests
{
    /// <summary>
    /// Adds two buttons beside the crafting tabs ("Chest lines" on/off and "Range" 5/10/15/20/30 m) and,
    /// when lines are on, draws a line from the crafting station to every chest we can pull materials from.
    /// </summary>
    internal class ChestOverlay : MonoBehaviour
    {
        private static readonly float[] RangeSteps = { 5f, 10f, 15f, 20f, 30f };

        /// <summary>One cloned UI button plus the text objects holding its label.</summary>
        private class ModButton
        {
            public GameObject Root;
            public TMP_Text[] Tmp;
            public Text[] Legacy;
            public Func<string> Label;

            public void Refresh()
            {
                string label = Label();
                foreach (TMP_Text t in Tmp) if (t != null && t.text != label) t.text = label;
                foreach (Text t in Legacy) if (t != null && t.text != label) t.text = label;
            }
        }

        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private Material _lineMaterial;
        private ModButton _linesButton;
        private ModButton _rangeButton;
        private bool _buttonsFailed;

        private void Update()
        {
            EnsureButtons();

            Player player = Player.m_localPlayer;
            CraftingStation station = player != null ? player.GetCurrentCraftingStation() : null;

            // Crafting at a station, or hand-crafting from the inventory screen (then chests are measured from the player).
            bool craftingOpen = player != null && InventoryGui.IsVisible() && Plugin.Enabled.Value
                                && (station != null || ChestScanner.HandCrafting());

            if (Alive(_linesButton)) _linesButton.Root.SetActive(craftingOpen);
            if (Alive(_rangeButton)) _rangeButton.Root.SetActive(craftingOpen);

            Vector3? origin = !craftingOpen || !Plugin.ShowLines.Value ? (Vector3?)null
                              : station != null ? station.transform.position : player.transform.position;
            DrawLines(origin);
        }

        // ---- buttons ---------------------------------------------------------------------------

        private void EnsureButtons()
        {
            InventoryGui gui = InventoryGui.instance;
            if (_buttonsFailed || gui == null || gui.m_tabUpgrade == null) return;

            // The game rebuilds its whole inventory UI when you leave a world and join another, which destroys our buttons.
            // A destroyed button still looks like a real object to ordinary C# null checks, so use Alive().
            if (!Alive(_linesButton) || !Alive(_rangeButton))
            {
                DestroyButtons(); // clear out whichever half survived, then build both fresh
                try
                {
                    _linesButton = CreateButton(gui, "CFC_LineToggle", ToggleLines,
                        () => Plugin.ShowLines.Value ? "Chest lines: ON" : "Chest lines: OFF");
                    _rangeButton = CreateButton(gui, "CFC_RangeToggle", CycleRange,
                        () => $"Range: {Plugin.Radius.Value:0.#}m");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Could not create the mod buttons: " + e);
                    DestroyButtons();
                    _buttonsFailed = true; // don't retry (and spam errors) every frame; the lines still work
                    return;
                }
            }

            // Lay out: [Craft][Upgrade][Chest lines][Range], left to right, each just right of the previous one.
            RectTransform tab = (RectTransform)gui.m_tabUpgrade.transform;
            float right = tab.anchoredPosition.x - tab.pivot.x * tab.sizeDelta.x + tab.sizeDelta.x;
            right = Place(_linesButton, tab, right, Plugin.ToggleWidth.Value);
            Place(_rangeButton, tab, right, Plugin.RangeButtonWidth.Value);

            _linesButton.Refresh(); // re-applied every frame in case the game rewrites the text
            _rangeButton.Refresh();
        }

        /// <summary>Clone the Upgrade tab so the button matches the panel's look.</summary>
        private ModButton CreateButton(InventoryGui gui, string name, UnityEngine.Events.UnityAction onClick, Func<string> label)
        {
            Transform parent = gui.m_tabUpgrade.transform.parent;
            Transform existing = parent.Find(name);
            if (existing != null) Destroy(existing.gameObject); // leftover from a previous (reloaded) copy of this mod

            GameObject clone = Instantiate(gui.m_tabUpgrade.gameObject, parent);
            clone.name = name;

            // Swap the tab's Button for a fresh one so a click can't trigger the tab's own behaviour.
            // DestroyImmediate (not Destroy) so the old Button is really gone before we add the new one.
            // ButtonTextColor is the tab's text-tinting script; it stays wired to the original tab and errors every frame.
            Image image = clone.GetComponent<Image>();
            foreach (MonoBehaviour mb in clone.GetComponents<MonoBehaviour>())
                if (mb is Button || mb.GetType().Name == "ButtonTextColor") DestroyImmediate(mb);
            Button button = clone.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            return new ModButton
            {
                Root = clone,
                Tmp = clone.GetComponentsInChildren<TMP_Text>(true),
                Legacy = clone.GetComponentsInChildren<Text>(true),
                Label = label,
            };
        }

        /// <summary>Put a button just right of <paramref name="leftEdge"/>; returns its own right edge for the next one.</summary>
        private static float Place(ModButton b, RectTransform tab, float leftEdge, float widthMultiple)
        {
            RectTransform rt = (RectTransform)b.Root.transform;
            rt.anchorMin = tab.anchorMin;
            rt.anchorMax = tab.anchorMax;
            rt.pivot = tab.pivot;
            rt.localScale = tab.localScale * Plugin.ToggleScale.Value;
            rt.sizeDelta = new Vector2(tab.sizeDelta.x * widthMultiple, tab.sizeDelta.y);

            float left = leftEdge + 8f;
            rt.anchoredPosition = new Vector2(left + rt.pivot.x * rt.sizeDelta.x + Plugin.ToggleOffsetX.Value,
                                              tab.anchoredPosition.y + Plugin.ToggleOffsetY.Value);
            return left + rt.sizeDelta.x;
        }

        private static void ToggleLines() => Plugin.ShowLines.Value = !Plugin.ShowLines.Value;

        /// <summary>Next preset above the current range, wrapping back to the smallest.</summary>
        private static void CycleRange()
        {
            float current = Plugin.Radius.Value;
            foreach (float step in RangeSteps)
            {
                if (step > current + 0.01f) { Plugin.Radius.Value = step; return; }
            }
            Plugin.Radius.Value = RangeSteps[0];
        }

        /// <summary>True if the button exists and the game hasn't destroyed it (Unity-aware null check on the GameObject).</summary>
        private static bool Alive(ModButton b) => b != null && b.Root != null;

        private void DestroyButtons()
        {
            // The buttons live in the game's UI, not under our plugin object, so we remove them ourselves.
            if (Alive(_linesButton)) Destroy(_linesButton.Root);
            if (Alive(_rangeButton)) Destroy(_rangeButton.Root);
            _linesButton = _rangeButton = null;
        }

        private void OnDestroy() => DestroyButtons();

        // ---- lines -----------------------------------------------------------------------------

        /// <param name="origin">Where the lines start (the station, or you when hand-crafting); null = draw nothing.</param>
        private void DrawLines(Vector3? origin)
        {
            List<Container> chests = origin.HasValue ? ChestScanner.GetNearby() : null;
            int count = chests != null ? chests.Count : 0;

            while (_lines.Count < count) _lines.Add(CreateLine());

            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 3f);
            Color color = new Color(0.3f, 0.9f, 1f, pulse);

            for (int i = 0; i < _lines.Count; i++)
            {
                LineRenderer lr = _lines[i];
                lr.gameObject.SetActive(i < count);
                if (i >= count) continue;

                lr.startColor = lr.endColor = color;
                lr.SetPosition(0, origin.Value + Vector3.up * 1f);
                lr.SetPosition(1, chests[i].transform.position + Vector3.up * 0.6f);
            }
        }

        private LineRenderer CreateLine()
        {
            if (_lineMaterial == null)
            {
                // Valheim strips unused shaders, so try a few common unlit ones.
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default") ?? Shader.Find("Hidden/Internal-Colored");
                _lineMaterial = new Material(shader);
            }

            var go = new GameObject("CFC_Line");
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = _lineMaterial;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = 0.05f;
            lr.numCapVertices = 4;
            return lr;
        }
    }
}
