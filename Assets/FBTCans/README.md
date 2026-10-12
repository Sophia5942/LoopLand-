# FBT Mayfair – Drinks Fridge + Bin

Uses **your own fridge model**. Six drink rows: click a row and that drink appears in front of it. Grab it, open it, and bin it.

## Setup
1. Delete any older **FBTCans** folder, then drag this **FBTCans** folder into your project's **Assets** folder.
   - It needs your **TransformSync.cs** in the project. It's already there if your bar works.
2. Wait for Unity to finish compiling.
3. In the Hierarchy, **click your fridge** to select it.
4. Click **FBT Mayfair > Build Can Fridge + Bin**. If an older one exists, choose **Replace**.
5. Inside "FBT Can Fridge + Bin" there are **6 drink rows**, laid out 3 x 2 to start with:
   - Get Mayfair Energy
   - Get Velvet Rouge
   - Get Emerald Gin & Tonic
   - Get Blanc Spritz
   - Get After Hours Espresso Martini
   - Get Passion Martini
6. **Move each row onto a shelf in your fridge**, with its **blue arrow pointing out of the fridge**, towards the glass.
   - Each row is a click box with 4 display cans of that drink.
   - The **see-through can** shows where the new drink appears, in front of the row.
   - The helpers are editor-only and aren't uploaded.
7. Move **Trash Bin** where you want it, then save the scene (Ctrl+S).

## The drinks
All six use **your can model** with a label painted to match the club: dark, warm gold, burgundy and green.

| Drink | Look |
|---|---|
| **Mayfair Energy** | Espresso-black, brushed-gold serif M, gold light lines |
| **Velvet Rouge**, Rum & Black Cherry | Burgundy velvet, gold cherries |
| **Emerald**, Gin & Tonic | Deep green, gold botanical sprigs |
| **Blanc**, Elderflower Spritz | Champagne cream, rising gold bubbles |
| **After Hours**, Espresso Martini | Espresso with a crema glow, gold coffee beans |
| **Passion**, Passionfruit Martini | Amber-to-burgundy sunset, gold light waves |

- The labels are in `Textures/`, one PNG per drink.
- On the lid there's a champagne-gold ring-pull and rivet. The drinking hole is at the front and appears when you open it.

## In game
- **Click a row** in the fridge ("Get Velvet Rouge" and so on). That drink appears in front of the row.
  - It stays put until someone grabs it.
  - Each player can get one drink every 10 seconds per row.
- **Grab it** and press **Use** to open it. Everyone sees the ring-pull animation and hears the can-opening sound.
- Press **Use** again to **swish** it round in your hand. This has no sound.
- **Drop it on the floor** and you hear the can landing.
- **Throw it in the bin** to clean it up.
- A can left lying around cleans itself up after 5 minutes.

## Admin panel
"Drinks Admin Panel" is a dark plaque with gold trim. It has two buttons:
- **LOCK / UNLOCK**: while locked, nobody can get new drinks. The rows can't be clicked, and the status shows **DRINKS LOCKED**. Drinks people already have still work.
- **RESET ALL**: clears every drink out of the world, including ones people are holding.

**Who can use it:** the **instance owner**. To add more admins, select the panel and type their VRChat display names into **Admin Names**. Untick **Instance Owner Is Admin** if you only want the named people.
- Everyone else can't click the buttons at all.
- In public or group instances there's no instance owner, so add names if you want the panel to work there.

**Safe from hacked clients:**
- Only an admin can take control of the panel. Whoever has it refuses everyone else.
- Every player only accepts a lock or reset that came from an admin.
- While locked, the owners of the cans refuse to hand a free can to non-admins.

## Settings
- **Each drink row** (CanFridge on "Get ..."):
  - **Cooldown Seconds**: 10 per player.
  - To make the click box bigger or smaller, change the Box Collider's Size. The see-through box is only a guide.
- **Cans** (FBTCan on each can):
  - **Cleanup After Seconds**: the auto clean-up time. 0 turns it off.
  - **Drop Sound Speed**: how hard a can has to hit something to make the floor sound.
- Each drink has **4 cans** in its pool. When all 4 are out, the oldest one nobody is holding is reused.

## Anti-abuse (hacked / modded clients)
- **There can never be more than 24 cans (4 per drink).** Spamming can't create new objects. Worst case, it moves the same cans around.
- **The cooldown is checked by other players too.** Every player sees who spawned a can and when. To take a free can, a player has to get it from its current owner, and that owner's game refuses if the player spawned one in the last ~7 seconds. So a hacked client that skips its own cooldown still gets refused.
- **Nobody can snatch a can out of your hand.** The owner refuses, and the pickup has Disallow Theft on.
- **Spamming the floor sound does nothing.** Each player's game plays it at most 4 times a second per can.
- **Spamming swish does nothing either.** A swish only plays once the last one has finished.
- Internal script functions can't be called over the network (they start with `_`).

## Networking / performance
- Everyone sees the same cans: appearing, opened, held, moved and binned.
- Each can syncs 3 bools and two counters (spawn, swish) (Manual sync), and only when something changes. Movement uses your TransformSync.
- The floor sound is one small network event, at most about 3 per second per can.
- The spawner and bin don't sync anything.
- There's no Update loop. The opening animation only runs for under a second when a can opens.
- It uses mobile shaders and small meshes, so it's fine on Quest.
- Your scripts are not changed.
