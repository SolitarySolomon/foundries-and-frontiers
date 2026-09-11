# Working on Foundries & Frontiers

Read this first. It is the working agreement, the conventions, and the state of play,
written down so a new session picks up where the last one left off rather than relearning
it from a conversation that is no longer there.

`docs/BUILD_ORDER.md` is the map and the reasoning. This file is how to work.

---

## What this is

A Vintage Story 1.22.7 mod (modid `foundriesfrontiers`, net10.0) adding autonomous NPC
villages that grow without the player doing anything, in the spirit of Millénaire. Repo
lives at `Vintage Story Foundries & Frontiers` on Corey's desktop. GitHub: SolitarySolomon.

## Division of labour

**Claude writes all the code. Corey tests it in game.** That is the whole arrangement, and
it shapes everything else: nothing can be verified by running it here, so verification has
to be by reading, by grepping the built artefact, and by adversarial review. A thing that
"should work" is a thing nobody has checked.

## How Corey wants to be talked to

- Written deliverables read like a person wrote them. **No em dashes.** Not AI-shaped.
- **Do not sugarcoat.** If an idea is bad, say it is bad and say why.
- Simplify when explaining something new.
- Credit third-party work in the README. Corey cares about this.

---

## The rules that took an argument to arrive at

These are not style preferences. Each one came out of shipping the wrong thing first.

**The ground belongs to the player.** A village may shape its own plot and nothing beyond
it. Inside a plot, abstraction is fine: a quarry face that produces stone without breaking
a new block every time is good design. Outside one, a village must not consume what a
player might want. The first mine dug real shafts and broke real ore blocks, which was
"honest" and also meant villages quietly stripping seams out of a shared world.

**Magic in a ledger is cheap. Magic that moves a villager is not.** A tool must be walked
to and picked up, a log must be carried home. What a swing produces can be abstract; how a
person gets from A to B cannot.

**Numbers come from the game, not from us.** Block prices derive from the storehouse
resource table, then the game's own grid recipes, then a short hand-written fallback, then
material. Yields come from the block's own `GetDrops`. Ore odds come from a real survey of
the ground under the plot. Whenever a figure could be read from the world instead of typed,
read it.

**Never quote a number you did not measure.** Illustrative estimates presented as output
is a mistake that has been made here once already and should not be made again.

**Tool gates are load-bearing.** Breaking a block through `IBlockAccessor` walks straight
past the mining tier the game puts on rock and ore. Any job that produces material must ask
`ToolIsGoodEnough` on the block it is taking the material from, and must wear the tool down
afterwards, or the whole tool economy is decoration.

---

## Bug classes this codebase keeps producing

Check for these before shipping anything. Every one has been shipped at least once.

1. **Reporting intent rather than outcome.** Returning true when nothing happened; counting
   a swing that produced nothing; `TryCarry` returns what is held, not what it took.
2. **A task ending without handing the slot to anything else.** The AI manager re-offers it
   next tick and the villager loops.
3. **Free-resource loops.** Doubled harvests, culling that duplicates loot, a face that
   produces without breaking or wearing anything.
4. **Silent no-ops from unchecked assumptions.** An asset that never resolves, a config
   array indexed out of range, `Math.Clamp(0, 0, -1)` throwing.
5. **"Nothing left I can do" mistaken for "the job is finished."** An empty scan result is
   not proof of completion.
6. **A job that never moves never pays for its work.** The base charges working time on
   arrival and a short pause between blocks. A job that stays in one place must override
   `PauseAfter`.
7. **An AI task is dead if nothing can carry the trade it asks for.** A villager's trade
   comes from the end of its entity code. A trade missing from `villager.json`'s variant
   group cannot be spawned, and the task refuses on its first line forever, silently.
8. **Reading the wrong layer.** `GetTerrainMapheightAt` returns the topmost **solid** block
   and is a **worldgen** height map: breaking blocks never changes it. Any job that moves
   the ground must find its own targets.
9. **Off-by-one on negative coordinates.** Use `>> 5` and `& 31` for chunk coords.

---

## How to verify

1. Build with `bash build.sh`. Zero warnings is the standard.
2. **Grep the built zip**, never the source, for every new symbol and every wiring change.
   Unpack `dist/foundriesfrontiers_<version>.zip` and check the DLL strings and the JSON.
3. **Run an adversarial review before shipping anything non-trivial.** Spawn a subagent
   with the changed files, the base classes they touch, and the bug-class list above. Every
   review pass so far has found real defects, several of them critical.
4. Decompile rather than guess at engine behaviour:
   `DOTNET_ROOT=/home/claude/.dotnet ilspycmd -t <Type> refs/<Assembly>.dll`
   Game assets are readable on Corey's machine under `Vintagestory/assets/survival`.

## Shipping a version

1. Bump `modinfo.json`.
2. `bash build.sh`, unpack and grep the zip.
3. `SendUserFile` the zip, move the previous one to `VintagestoryData/ModsOld/`, commit the
   new one into `VintagestoryData/Mods/`.
4. `SendUserFile` the changed sources, commit them into the repo folder.
5. Commit with `bash ~/ffgit.sh commit -F -` (see below). Write what changed and, more
   importantly, what was wrong.
6. Update `docs/BUILD_ORDER.md` and the field manual artifact.

**`~/ffgit.sh` exists for a reason.** The repo folder is mounted in a way that forbids
`unlink()`, so git can create lock files but never remove them. The wrapper sweeps stale
locks before and after each command. Use it for every git call; plain `git` will wedge.

---

## Engine notes worth keeping

- `AiTaskManager.StartNewTasks` iterates tasks in **JSON list order** and re-reads the slot
  each iteration, so a task listed later can preempt one started earlier on the same tick.
  Priority alone does not settle ordering problems.
- Tree felling lives on **`ItemAxe.OnBlockBrokenWith`**, not on the block. It works with a
  non-player entity. Chopping the bottom log fells the tree; do not reimplement it.
- `BlockSchematic.BlockIds` are keys into that schematic's own `BlockCodes`, **not** world
  block ids. `TransformWhilePacked` repacks to its own corner, so do not re-normalise
  offsets afterwards: the fittings come off the untouched schematic and will not follow.
- `BlockEntityFarmland.OnCreatedFromSoil` is essential. Without it new farmland has zero
  fertility and grows at a tenth speed while looking like the best soil in the game.
- `Assets.GetMany(path, domain, loadAsset: false)` leaves assets unhydrated and `ToText()`
  returns empty.
- `BreakBlock(pos, null, 0f)` suppresses drops and already triggers the neighbour update.
- WorldEdit export: `/we on`, `/we ms` at the low corner, `/we me` at the high corner,
  `/we export <name>`. Not `/we mex`.

---

## State of play

Version **0.12.0**. Phases A and B are done. Phase C's machinery is done.

**Corey's outstanding jobs, not Claude's:**

- `git push`. There are unpushed commits.
- **C1: the eight placeholder schematics.** Hand-built in game and exported with WorldEdit,
  all facing north. Nothing in Phase C or D can be tested without them, and this is the one
  part Claude cannot do. Requirements and the export procedure are in the field manual.
- Run the test list in the field manual.

**Next real work:** Phase D, the brain. Needs model, daily decision tick, the street grid
and site selection, the build planner, the labour allocator, population growth. `FacingFrom`
in `VillageRegistry.Building.cs` becomes "face the street" instead of "face the square" once
D3 lays streets. Roads are a route finder, not schematics.

**The field manual** is a published artifact holding the full test list with every command,
the schematic build requirements with facings, and the WorldEdit export steps. Ask Corey for
the link, or find it with the Artifact tool's `list` action; it is titled
"Foundries & Frontiers Field Manual".
