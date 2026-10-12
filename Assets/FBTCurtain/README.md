# FBT Mayfair Curtain Opener

Opens your curtain when someone walks up to it. It animates the blend shape "Key 1" from 0 (closed) to 50 (open),
with a curtain sound, and closes it again when everyone has walked away. Your curtain model is not changed.

## Setup
1. Drag the `FBTCurtain` folder into `Assets`. Wait for Unity to finish loading.
2. Select your curtain in the Hierarchy (the object with the Skinned Mesh Renderer).
3. Top menu: **FBT Mayfair > Add Curtain Opener To Selected Curtain**.
4. Save the scene (Ctrl+S) and press Play.

It adds a separate "Curtain Opener" object next to your curtain. Settings on it:
- `Open Distance` (3 m): how close someone must be.
- `Open Seconds` (2.5): how long it takes to open or close.
- `Open Weight` (50) and `Closed Weight` (0).
- `Any Player Opens It`: on = anyone near opens it for everyone; off = it only opens for you when you're near.
- `Open Clip` / `Close Clip`: swap in your own sounds any time.

There's no networking: every player's game checks where the players are, so everyone sees the same thing. It checks
4 times a second and only animates while the curtain is moving.
