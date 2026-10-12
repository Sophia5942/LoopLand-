# FBT Mayfair Spirits Machine

A simple bar spirits dispenser built on **your own scripts**: `ManagerBar`, `LiquidParticles`, `LiquidGlass` and
`TransformSync` (unchanged), plus two small helpers:
- `BarTapButton`: the PUSH button. It switches a tap's `LiquidParticles` stream on or off for everyone.
- `GlassLock`: added to each glass. When you put the glass down under a spout, it locks onto the drip tray.

## Install
1. Delete any old `Assets/DrinkSystem` folder, and any old "Drink Machine" object in your scene.
2. Drag the `FBTBar` folder into `Assets`. Your four scripts and your `Glasses2025` prefab must be in the project.
3. Select your bar counter. Then use the top menu: **FBT Mayfair > Build Spirits Machine**.

## How it works in game
1. Pick up a glass from the tray next to the machine.
2. Put it on the drip tray under a spirit. It locks in with a click.
3. Press **PUSH** to pour. The stream fills the glass (your LiquidParticles + LiquidGlass). Press **PUSH** again to stop.
4. Hold **Use** to drink. ManagerBar makes you drunk, depending on the spirit.

The PUSH buttons are synced for everyone. Glass fill and position sync through your LiquidGlass and TransformSync.

## Glass respawn
- **RESET GLASSES** (on the front of the glass tray) puts every glass nobody is holding back on the tray, empty.
- A glass left alone away from its spot goes back by itself after 3 minutes. Change `Respawn After Seconds` on a
  glass's Glass Lock (0 = never).
- Glasses someone is holding are never taken away. Glass Lock syncs a "held" flag for that.

## Sync and performance fixes for your scripts (folder `FBT_YourScripts_Fixed`)
Replace your `ManagerBar.cs`, `LiquidGlass.cs` and `TransformSync.cs` with these. Every change is marked `// FBT FIX`.
- **ManagerBar, filling:** it checked who owns the glass's *liquid child*, which never changes owner, so only the
  world master could fill glasses. It now checks the glass itself.
- **ManagerBar, drunk effect:** it wrote 6 values to the effect material every frame. Now it only writes when they
  change. The drunk post-processing still works exactly the same.
- **LiquidGlass, fill line:** it copied and transformed every vertex of every glass every frame. Now it only redoes
  that when the glass moved or its fill changed (and keeps one copy of the vertices). It also reuses one material
  instead of calling `renderer.material` repeatedly.
- **TransformSync:** it only sent moves over 10 cm (so small moves never reached others). Now 2 cm / 1 degree.
- **Glass Lock:** whoever picks up a glass becomes its owner, so drinking and fill sync from them.
