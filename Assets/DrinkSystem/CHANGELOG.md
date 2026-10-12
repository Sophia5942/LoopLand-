# Changelog

## 2.1.6
- Fix NullReferenceException in Profile: glass meshes are looked up again by name after the model is re-imported (Read/Write), and a glass that cannot be loaded is skipped instead of stopping the build.

## 2.1.5
- Fix "Could not load the FBT Mayfair logo": the logo is always re-imported as a sprite, with a fallback that creates the sprite in code (same for the generated icons).

## 2.1.4
- Build errors now show as a pop-up, and a finished build moves the Scene view to the machine and says so.
- The logo is found wherever the DrinkSystem folder is.

## 2.1.1
- Now a regular `.unitypackage` that imports into `Assets/DrinkSystem`. There are no assembly definitions or UdonSharp
  assembly files, so it compiles together with your own UdonSharp scripts. Fresh file IDs, so nothing clashes with
  older installs.
- Glass tint reset no longer uses `MaterialPropertyBlock.Clear`.

## 2.1.0
- Clear step-by-step line, left to right: 1 glass rack, 2 place, 3 strength + froth + pour, 4 finishing station
  (straw, fruit, glass tint, logo, applied instantly to the glass on the pad), 5 drink. Numbered gold badges, a sign
  with the whole line, and cards that always say what to do next.
- STRENGTH now drives the drunk post-processing (how drunk you get). Spirit was merged into it.
- Uses your Glasses2025 glasses (models and materials) as networked glasses, with an FBT Mayfair logo decal.
- FBT Mayfair branding replaces Golden Hour and the G logo. Neon sign removed.
- Faster drunk effect: off when sober, throttled material updates, only changed values are written.

## 2.0.0
- New look: a fully generated black and gold bar station (no model file needed) with drink cards and painted drink
  icons, gold nozzles with warm light cones, a garnish drawer, a glowing shelf of logo glasses and a neon sign.
- Options tower: FROTH / STRENGTH / SPIRIT dials, FRUIT and STRAW pickers, synced for everyone. A poured drink keeps
  them (foam, color strength, effects, rim fruit wheel, straw). Custom Drink pours the picked fruit.
- Customize Cup kiosk: glass tint and logo for the glass on its pad.
- Builder places the station on top of the selected object (your bar counter).

## 1.0.0
- First release: networked drink machine with cup locking, push-button UI (laser, mouse, VR finger presses),
  pour / fizz / slurp / lock sounds, mixable flavors, drinking, cup respawn, Quest-friendly shaders and a one-click builder.
