# Changelog

## 1.11.0
- Real scratching: the scratch card is covered in silver foil that tears away wherever you rub it with your pointer
  (VR laser or desktop cursor). No more sliding coins.
  - The foil leaves torn, scalloped holes, and silver flakes fall from where you scratch.
  - Scratch sound while you rub, plus a light controller buzz in VR.
  - A SCRATCHED % counter; the prize pops out once about 70% is gone (or press REVEAL ALL).
- Rebuild the scene (**LoopLand > Build Game In Scene > Replace**) to get the new card.

## 1.10.0
- New **LoopLand Scratch Cards** machine beside the store, with a marquee, a "FEEL LUCKY?" sign and a buy desk.
  - Four packs: Common 50, Rare 100, Epic 200 and Legendary 400 Loop Coins.
  - Drag the coins across three silver strips to scratch (VR laser or desktop), or press REVEAL ALL.
  - Prizes are Loop Coins (with a 3% jackpot of 3x the pack price) or a dice, token, building style or trail
    you don't own yet. The prize is saved the moment you buy, so leaving mid-scratch never loses it.
  - "YOU WON!" panel with rarity stars, EQUIP, confetti and a win jingle.
  - A collection strip shows which of the 13 winnable items you own.
- Rebuild the scene (**LoopLand > Build Game In Scene > Replace**) to add the machine.

## 1.9.4
- Tokens hop through the middle of every card and land centred on it.
- Players on the same card form a neat group centred on it, and slide back to the centre when someone leaves.

## 1.9.3
- Back to the board game on the floor: the tower world, elevator and world sounds from 2.0/3.0 are removed.
- New **LoopLand > Remove Tower World (back to the floor)** cleans a scene built with them. It:
  - deletes the tower world;
  - puts the game and store back;
  - shows the demo floor and light again;
  - moves the spawn back next to the table;
  - restores the default sky.

## 1.9.2
- VIEW is a true swap: CENTER shows only the middle screens, TOP shows only the screen in front of the console
  you pressed it on (the middle screens hide). Local to each player; starts on CENTER.

## 1.9.1
- Much smoother camera: every shot change is an eased glide (smootherstep), the pan back to the overview takes
  3.2 s with a gentle crane lift, and the chase cam filters out hop bounce and direction snaps.

## 1.9.0
- Cinematic live camera: dice close-up, side chase-cam, upright close-up of the landing card, pan back to an
  angled overview (perspective). UI panels, screens and the logo are hidden from the camera.
- Captions on every live screen: "<player> rolled 4 + 2" (DOUBLES! too) and "<player> landed on <space>".

## 1.8.0
- Live camera director: zooms in on the dice, follows the moving token, then returns to the overview.
- VIEW swaps between the top screen and the center screens (the side live panel is removed).
- Property card prices are aligned rows with stripes, $1,500-style formatting, mortgage value, and the
  current rent row highlighted in gold.

## 1.7.0
- Seat dashboards in the style of a property app: card with picture, name, price, full rent table, owner line
  and the big action button, plus an icon menu (browse, my space, build, sell, mortgage, rounds, store, view).
- VIEW toggle per seat: live board beside the dashboard, on a top screen, or off. Center live screens are back,
  raised to 2.3 m.
- Every board card shows a picture (generated per color group / special space, or your Space Art).

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
