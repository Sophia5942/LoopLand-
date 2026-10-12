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
