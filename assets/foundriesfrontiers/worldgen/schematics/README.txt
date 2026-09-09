Building schematics go in this folder, one .json per building.

How to make one
---------------
1. Build the thing in a creative world.
2. Enable WorldEdit: /worldedit on (or the WE hotkey).
3. Mark the two corners of the box around it. Include the ground it stands
   on only if the building needs a floor; leave it out if it should sit on
   whatever ground it is placed on.
4. /we mex <name>
   The file lands in VintagestoryData/ModData/.../schematics or the game's
   WorldEdit export folder, depending on version. Copy it here.
5. Add an entry for it in config/buildings.json using the same <name>.

What the mod works out for itself
---------------------------------
Size, the bill of blocks, and what it costs the village in pool value are all
read out of the schematic. Never write those by hand. The manifest entry only
carries what the file cannot know about itself: which cultures build it, which
need it answers, which trade it houses, its tier and its quality.

A schematic with no manifest entry still loads and shows up in /ff buildings,
but no village will ever choose it, because nothing knows what it is for.

Keep them small to begin with. A hovel that works beats a longhouse that does
not, and these are meant to be replaced once the tier ladder stops moving.
