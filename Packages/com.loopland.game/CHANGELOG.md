# Changelog

## 1.6.1
- Console polish: tighter neon outlines (no more overlapping glows), no accent bars over the top buttons,
  bigger STORE button, button gloss, solid back plate (no mirrored UI from across the table).
- Seat screen stands on two posts that stay off the board cards.
- Store shows item pictures in the editor before Play mode.

## 1.6.0
- Neon "Game Controls" console: icon buttons in two columns, LOOPLAND header, big JOIN GAME button, glowing edges.
- STORE button on every console: the store pops up on that seat screen for the local player (X or STORE again closes it).
- New icons: bank, hammer, chart, refresh, people, store bag.

## 1.5.0
- Seat screens: each console gets its own live board + status screen above eye level, replacing the far-away
  center hologram screens (the spinning logo stays).
- Console panels are hidden from the live board camera, so the feed shows only the board.

## 1.4.1
- Sharper Live Board: 2048x1024 feed framed tightly on the board (about 2x the detail).

## 1.4.0
- Infinity board: the 40 cards follow a figure-8 (two round lobes and crossing lanes) on an infinity-shaped
  table, over a glowing gradient ribbon with sparkles. Dice roll in the right lobe; logo in the left lobe.
- Live Board is now a wide 2:1 feed; hologram screens and consoles face the four corners of the table.
- New cosmetic category: Building styles (color of your Loops/Towers) and a Skyline Pack premium product.
- Redesigned store: gradient logo, coin badge, icon tabs, picture cards with status pills, detail panel with
  rarity and preview, info banner, Daily Bonus / World Store buttons. Thumbnails are generated procedurally
  and can be replaced from Assets/LoopLand/Store Art.

## 1.3.0
- Board spaces are now UI cards on a single flat canvas: no overlapping, fewer draw calls.
- Custom card art: drop images into Assets/LoopLand/Space Art (00-39 or space name) and rebuild.
- Ownership strips and Loop/Tower markers are UI on each card.

## 1.2.0
- All controls are now world-space VRChat UI (Canvas + VRC Ui Shape) instead of 3D interact buttons.
- The live board feed is shown on the hologram screens above the table and on a store panel.
- Board spaces are picked with < SPACE / SPACE > / MY SPACE on the console; tiles have no colliders.

## 1.1.0
- Live Board: overhead camera feed of the board on a spectator screen and a store mini screen.
- Fix: table colliders no longer block clicking buttons and board spaces.

## 1.0.0
- First release: networked LoopLand board game (2-6 players), particle tokens, animated 3D dice,
  persistent Loop Coins, cosmetics store, VRChat Creator Economy integration, one-click scene builder.
