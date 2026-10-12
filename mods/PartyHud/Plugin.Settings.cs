using BepInEx.Configuration;

namespace PartyHud
{
    public partial class Plugin
    {
        private ConfigEntry<float> _hudOpacity, _panelWidth, _compactWidth, _rowSpacing, _textScale;
        private ConfigEntry<float> _headerX, _headerY, _headerFontSize, _headerWidth;
        private ConfigEntry<bool> _anchorBottom, _fitToScreen, _keepOnScreen;

        private void BindLayoutSettings()
        {
            _offsetX = Config.Bind("Layout", "OffsetX", 10f,
                new ConfigDescription("Gap from the chosen left or right edge, in UI pixels before Scale is applied.", new AcceptableValueRange<float>(0f, 4096f)));
            _offsetY = Config.Bind("Layout", "OffsetY", 300f,
                new ConfigDescription("Gap from the chosen top or bottom edge, in UI pixels before Scale is applied.", new AcceptableValueRange<float>(0f, 4096f)));
            _scale = Config.Bind("Layout", "Scale", 1f,
                new ConfigDescription("Size of the whole HUD (1 = normal, 1.25 = bigger).", new AcceptableValueRange<float>(0.35f, 2f)));
            _onLeft = Config.Bind("Layout", "OnLeft", false, "Anchor the panel to the left edge instead of the right.");
            _anchorBottom = Config.Bind("Layout", "AnchorBottom", false, "Anchor the panel to the bottom edge instead of the top. OffsetY is then the gap from the bottom.");
            _compact = Config.Bind("Layout", "Compact", false, "Use smaller portraits and thinner bars.");
            _panelWidth = Config.Bind("Layout", "Width", 320f,
                new ConfigDescription("Width of each member row in normal mode, before Scale is applied.", new AcceptableValueRange<float>(240f, 640f)));
            _compactWidth = Config.Bind("Layout", "CompactWidth", 252f,
                new ConfigDescription("Width of each member row in compact mode, before Scale is applied.", new AcceptableValueRange<float>(200f, 400f)));
            _rowSpacing = Config.Bind("Layout", "RowSpacing", 3f,
                new ConfigDescription("Gap between member rows, in UI pixels before Scale is applied.", new AcceptableValueRange<float>(0f, 20f)));
            _fitToScreen = Config.Bind("Layout", "FitToScreen", true, "Shrink the HUD when necessary to fit its width and height inside the screen.");
            _keepOnScreen = Config.Bind("Layout", "KeepOnScreen", true, "Keep the HUD inside the screen when an offset would push it outside.");
            _avoidShipHud = Config.Bind("Layout", "AvoidShipHud", true, "Move the panel away from the ship display while you steer.");

            // Keep the original key so existing configurations retain their appearance.
            _opacity = Config.Bind("Layout", "Opacity", 0.85f,
                new ConfigDescription("Opacity of the wood and carved frame: 0 = clear, 1 = solid. Text, portraits and bars stay visible.", new AcceptableValueRange<float>(0f, 1f)));
            _hudOpacity = Config.Bind("Appearance", "HudOpacity", 1f,
                new ConfigDescription("Opacity of the entire HUD, including text, portraits, icons and bars: 0 = invisible, 1 = fully visible.", new AcceptableValueRange<float>(0f, 1f)));
            _textScale = Config.Bind("Appearance", "TextScale", 1f,
                new ConfigDescription("Size of member names, distances, status text and bar numbers. Header/FontSize controls the party title separately.", new AcceptableValueRange<float>(0.75f, 1.25f)));
            _headerX = Config.Bind("Header", "OffsetX", 36f,
                new ConfigDescription("Position of the party title's text box from the panel's left edge. Smaller values move it left; larger values move it right.", new AcceptableValueRange<float>(-128f, 256f)));
            _headerY = Config.Bind("Header", "OffsetY", 10f,
                new ConfigDescription("Position of the party title's text box from the panel's top edge. Smaller values move it up; larger values move it down.", new AcceptableValueRange<float>(-64f, 128f)));
            _headerFontSize = Config.Bind("Header", "FontSize", 15f,
                new ConfigDescription("Size of the party title and count before the whole HUD's Scale is applied. Long translated titles shrink to fit their text box.", new AcceptableValueRange<float>(8f, 28f)));
            _headerWidth = Config.Bind("Header", "Width", 76f,
                new ConfigDescription("Width of the party title's text box. Increase this with FontSize when a larger title needs more room.", new AcceptableValueRange<float>(48f, 180f)));
        }
    }
}
