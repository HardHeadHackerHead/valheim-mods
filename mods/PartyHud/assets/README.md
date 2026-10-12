# Party HUD skin

The built-in imagegen tool generated these assets. Original PNGs and transparency are preserved; sprite rectangles trim the margins at runtime.

- `nordic-surround.png`: continuous outer frame with a raised, sloping header shoulder. Nine-slicing preserves the shoulder and corner shapes as the party grows.
- `nordic-panel-v2.png`: rounded member panels with flowing Nordic corner knots. Companion trim is recoloured at load time.
- `nordic-panel.png`: retained first-pass artwork; no longer embedded in the DLL.

Portraits, names, stats, food and effects remain live Unity UI. Bevels and bar grain are made in code. Shared textures and sprites are destroyed on unload.

## Outer frame prompt

Use case: stylized-concept. Production bitmap sprite for a Valheim party HUD. One EMPTY front-facing dark weathered charcoal wood frame on actual transparent background, wide 3:1 overall aspect. The OUTER SILHOUETTE has an integrated raised header shoulder at the TOP LEFT: top-left begins rounded, upper tab flat across first 27% of width, then slopes down diagonally gently into the long horizontal top edge at y approximately 12% of full height. This is ONE continuous carved frame, NOT a small separate rectangle above a larger one. The top-left header is empty dark wood; no seam or horizontal divider under header. Main rectangular body starts below shoulder and extends to bottom. Thin worn bronze and antique gold double trim follows entire silhouette continuously, dark beveled edges, softly rounded chamfered corners. Elegant small flowing curled Nordic ribbon knot flourishes tucked into the corners, curved loops like the user's reference, NOT heavy geometric triangular corner plates. Dark wood center extremely subdued with lots of empty space for live HUD. All artwork fully inside image, minimal transparent margin, orthographic no perspective. No text, no icons, no portraits, no bars, no mockup content, no outside drop shadow. Header shoulder must be clear and integrated.

## Member panel prompt

Use case: stylized-concept. Production game UI bitmap sprite: ONE empty front-facing rectangular Valheim party member panel, wide 3:1 aspect on actual transparent background. Almost black weathered wood center, very subdued grain. Thin worn gold and bronze double metallic trim with dark beveled edging. Softly rounded chamfered outer corners. Each of four corners has an elegant small FLOWING CURLED NORDIC RIBBON KNOT ornament: rounded interlaced curls with negative spaces like a looped triquetra, NOT angular solid triangular corner plates. Delicate open curled ornament limited to outer 10% of panel; lots of completely empty dark wood for live portraits and bars. It should feel flowing and hand carved, matching a fantasy Nordic HUD. All panel edges fully inside canvas, minimal transparent margin. Straight continuous border. Orthographic, no perspective, no outside shadow. No text, letters, icons, portraits, bars or interface contents.
