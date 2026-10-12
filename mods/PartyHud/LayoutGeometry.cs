using System;

namespace PartyHud
{
    /// <summary>Screen placement in UI coordinates, independent of Unity so edge and fit rules can be checked offline.</summary>
    internal static class LayoutGeometry
    {
        internal struct Placement
        {
            internal float X, Y, Scale;
        }

        internal static Placement Resolve(float screenWidth, float screenHeight, float panelWidth, float panelHeight,
            float requestedScale, float offsetX, float offsetY, bool left, bool bottom, bool fit, bool keepOnScreen)
        {
            screenWidth = Math.Max(1f, screenWidth); screenHeight = Math.Max(1f, screenHeight);
            panelWidth = Math.Max(1f, panelWidth); panelHeight = Math.Max(1f, panelHeight);
            float scale = Math.Max(0.01f, requestedScale);
            float x = Math.Max(0f, offsetX) * scale, y = Math.Max(0f, offsetY) * scale;
            if (keepOnScreen)
            {
                // Correct impossible offsets before fitting; a misplaced panel should not become a tiny speck.
                x = Math.Min(x, Math.Max(0f, screenWidth - panelWidth * scale));
                y = Math.Min(y, Math.Max(0f, screenHeight - panelHeight * scale));
            }
            if (fit)
            {
                scale = Math.Min(scale, Math.Max(1f, screenWidth - x) / panelWidth);
                scale = Math.Min(scale, Math.Max(1f, screenHeight - y - 12f) / panelHeight);
            }
            if (keepOnScreen)
            {
                x = Math.Min(x, Math.Max(0f, screenWidth - panelWidth * scale));
                y = Math.Min(y, Math.Max(0f, screenHeight - panelHeight * scale));
            }
            return new Placement { X = left ? x : -x, Y = bottom ? y : -y, Scale = scale };
        }
    }
}
