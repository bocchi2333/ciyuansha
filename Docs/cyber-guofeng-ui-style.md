# CiyuanSha Ink Wuxia UI Style v2

## Direction

The current visual pass has pivoted from cyber-guofeng to a dark ink-wuxia card table:

- Dark courtyard and stone-table battlefield atmosphere.
- Aged paper cards with ink-black text and worn bronze borders.
- Muted bronze gold for primary flow and selected state.
- Vermilion for danger, damage, decline, and response pressure.
- Muted jade for usable hints, hand count, and helpful actions.
- Fire orange and thunder violet remain reserved for elemental feedback.

The target is roughly 70% of the reference-board feeling in the first pass: readable, moody, card-like, and stable in LAN play before custom illustration and particle polish.

## UI Rules

- Gold means primary flow: Start Match, End Play, selected cards, phase banners, and important headers.
- Vermilion means danger or pressure: damage, pass/decline, response windows, and final decisions.
- Paper yellow means playable cards and readable foreground information.
- Muted jade means currently usable, hand-related, or helpful state.
- Dark ink panels should feel like lacquered metal, smoke, and worn table ornaments rather than flat Godot controls.
- Card buttons should read as miniature cards: suit/rank, large card name, and category band.

## Motion Rules

- Keep motion short and readable.
- Use hover lift for buttons and cards.
- Use popup scale/fade for response windows.
- Use flash feedback for HP changes.
- Prefer smoke, slash, fire, and thunder overlays later; avoid long cinematic effects until the LAN loop remains stable under playtest.

## Current Implementation

- `CyberStyle` remains the shared API name, but its palette and widgets now implement the ink-wuxia style.
- `CyberMatchHudPanel` adds a dark courtyard backdrop, mist bands, central decision stage, phase banner, and pile plaques.
- `HandZonePanel` renders hand cards as aged paper card faces with suit/rank/name/category.
- `HandZonePanel` shows a selected-card floating preview above the hand zone, with card face and effect text.
- `HandZonePanel` adapts the selected-card preview width for smaller 16:9 layouts and uses Chinese table prompts for the main hand-zone states.
- `InkCardButton` procedurally draws paper grain, edge wear, ink wash marks, central suit/type sigils, category bands, and corner brackets on hand-card buttons without changing input behavior.
- `InkCardButton` supports virtual-card previews for UI-only response sources such as Serpent Spear.
- `GeneralCardView` renders darker vertical general cards with bronze frame treatment, HP beads, and compact equipment/judgement tags.
- `GeneralCardView` uses a compact status badge and top state line for current turn, response, dying, defeated, offline, local-player, and waiting states.
- `ResponseWindowPanel` uses a wider central pressure window for Dodge/Peach/Slash/Nullification decisions.
- `ResponseWindowPanel` shows a concrete response-card preview when the local player has a usable Dodge/Peach/Wine/Slash/Nullification card.
- `ResponseWindowPanel` separates event prompt, usable response resources, and recommended action hints, with a gold/vermilion pressure bar for clear response state.
- `TargetSelectionPanel` and `MatchStatusPanel` use Chinese table-language prompts so playtesters can see who acts next and why a control is locked.
- `TargetSelectionPanel` uses two-line target buttons with selectable/selected/locked state labels, localized lock reasons, and a small color state line for targeting readiness.
- `MatchStatusPanel` uses localized phase/waiting/result text, player names when available, and a small color state line for your action, response, waiting, resolving, and ended states.
- `BattleLogPanel` uses BBCode category tags, latest-entry arrow highlighting, and a short panel pulse for new entries.
- `CyberMatchHudPanel` uses short colored damage strokes on physical/fire/thunder damage events, while keeping heal/response feedback lighter.

## Next Visual Targets

- Replace procedural flat backdrops with licensed or self-made courtyard/table textures.
- Continue tuning paper noise and ink edge masks after live visual inspection; the first procedural edge-wear pass is in place.
- Manually tune slash/fire/thunder hit flashes after visual checking in live Godot; the first code-only damage stroke pass is in place.
- Replace or augment the procedural card face with self-made paper/ink textures after the current layout is stable.
- Manually inspect 1366x768 hand-zone overlap after the selected-card preview and equipment tags.
