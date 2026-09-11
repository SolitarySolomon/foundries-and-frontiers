Building schematics go in this folder, one .json per building.

Two rules matter more than everything else here. Get either wrong and the
building loads without complaint and then sits wrong in every village forever.

  1. Build it facing NORTH.
  2. Export with /we export, not /we mex.

Both are explained below.


Which way is north
------------------
North is negative Z. Your coordinate readout is already switched on, so walk
and watch the third number: Z going DOWN means you are walking north.

The front of the building, the wall the door is in, must be the wall with the
SMALLEST Z. Stand outside the front door and look at it, and you should be
facing south, with Z rising as you walk toward the door.

Every schematic is authored facing north and the mod turns it from there. A
village decides which way a house should look and rotates it a quarter, a half
or three quarters of a turn, and the game's own transform moves the door and
every other orientable block round with it. Build one facing east and it still
loads, still places, and is wrong by a quarter turn in every village, with no
error anywhere to tell you.


How to export one
-----------------
1. Build the thing in a creative world, facing north.

2. /we on

3. Stand in the bottom north west corner of the box you want, at the lowest
   block of it, and type:   /we start
   The mark is taken at YOUR position, not the block you are looking at, so
   stand in the corner rather than next to it.

4. Fly to the opposite corner, top south east, and type:   /we end

5. /we export hovel
   Any name. The .json is added for you.

6. The file lands in:   VintagestoryData\WorldEdit\hovel.json
   Copy it into this folder.

7. Add an entry for it in config/buildings.json using the same name.

/we info shows the current selection if you lose track of it, and /we start
and /we end can be re-typed as many times as you like before exporting.

The short aliases ms, me and mex only exist when a world has the
legacywecommands setting turned on. On a normal 1.22.7 world they are not
commands at all, which is why the instructions here changed.


What to include in the box
--------------------------
Include the walls, roof, floor if it has one, door, windows, and every bed and
workstation that belongs to it.

Leave the ground out unless the building genuinely needs a floor. The box is
placed with its bottom layer sitting on the ground, so a bottom layer of soil
means every one of these gets a course of your creative world's dirt under it
wherever it is built.

Leave a margin of air around it only if you want that margin cleared. The mod
cuts back grass, bushes, saplings, leaves and tree trunks inside the footprint
before building, so the box being a little generous is harmless. It does NOT
cut soil, sand, gravel or rock: levelling ground is the digger's job and is
not built yet, so site anything you test on reasonably flat ground.

Do not put the mod's own storehouse block in a schematic. A village places its
own crate near its centre and keeps a record of where it is. A storehouse
building is the shed around it.


Minimums, by what the building is for
-------------------------------------
housing    At least one bed. Beds are the only thing population growth is
           gated on, so a house with no bed grows nothing. Use any vanilla
           bed. A bed is two blocks and only the head half is counted, so
           four beds read as four, not eight.
           Also wants a door and a roof that actually closes: warmth is a
           standing need later and an open shell will fail it.

workshop   At least one workstation block for the trade it houses, and the
           manifest entry must name that same trade. The block codes that
           count are listed in config/facilities.json. A workshop with no
           recognised station in it houses nobody.

storage    A shell. No special block.

amenity    Whatever the amenity is. A well needs its water on the fluid
           layer, which the game handles for you if you build it with real
           water.

defence    A wall segment or a tower. Keep segments short and straight.

civic      Anything. The square and the monument live here.


Sizes
-----
Keep them small to begin with. Odd numbers on both horizontal sides make the
middle unambiguous when a building is turned. Around 5x5 to 9x9 is a sensible
range for tiers 0 to 2, and nothing should exceed about 15x15 yet: siting looks
for ground flat to within two blocks across the whole footprint, and the bigger
the box, the rarer that is.

A hovel that works beats a longhouse that does not. These are placeholders and
are meant to be thrown away once the tier ladder stops moving.


What the mod works out for itself
---------------------------------
Size, the bill of blocks, and what the building costs the village in pool value
are all read out of the schematic. Never write those by hand. The manifest
entry in config/buildings.json only carries what the file cannot know about
itself: which cultures build it, which need it answers, which trade it houses,
its tier and its quality.

A schematic with no manifest entry still loads and shows up in /ff buildings,
but no village will ever choose it, because nothing knows what it is for.
