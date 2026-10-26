# Importing a sheet of per-animation drawings

The drawings in `sprites/anim-<state>.png` came from one sheet: 8 columns x 12 rows on a white
background, in the order the states appear in `behaviours.json`.

Run from a scratch folder holding `sheet.jpg` (paths in the scripts are relative):

1. `cut.py` – splits the sheet into `cells/<state>.png` by connected pieces.
2. `prep.py` – keys out the white background (with edge un-blending) into `keyed/`.
3. `cut.py visor` – measures each visor into `visor.json`.
4. `pockets.py` – removes white background trapped between limbs (keeps mouth and eye whites).
5. `normalize.py` – same head size and visor position as the original frames, overall size
   partly evened out, fitted to the 677x897 canvas; writes `norm/` and `norm.json` (feet).
6. `fixups.py` – removes the balls drawn in the chase/bat poses (the toy ball is its own window).
7. `stretch.py` – lengthens the body below the neck (more for chibi-like drawings) so her head
   isn't oversized and poses match in height.
8. Copy `norm/*.png` to `sprites/anim-<state>.png`, write `sprites/anim-feet.json`, then run
   `python tools/make_anim_frames.py` for blink, talking-mouth and headphone variants.
