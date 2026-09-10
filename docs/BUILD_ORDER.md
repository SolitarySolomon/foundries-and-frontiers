# Build order

The design document is the *why*. This is the *what next*, every step needed to get from
here to the mod the design describes.

**This is a map, not a contract.** Steps will subdivide as they get built. "B8 lumberjack"
is one line here and is realistically several days with its own surprises. A step that
turns out to be three steps gets split, and that is the plan working correctly.

**Rules this order follows:**

1. **Every step is testable in-game by a single command.** If it can't be verified in
   under a minute, it's too big and gets split.
2. **Nothing depends on a later step.** Audited twice; see *Audit notes* at the bottom for
   what those passes found.
3. **Risky work happens early inside its phase**, so a surprise doesn't invalidate work
   stacked on top of it.
4. **Tooling before content.** Test-loop speed decides whether this ships.

Status: `[x]` done · `[~]` in progress · `[ ]` not started · **RISK** = expect several attempts.

---

## Cross-cutting constraints

Rules that apply to every step, because retrofitting any of them is painful.

- **Multiplayer-safe from the start.** Anything player-specific is a map keyed by player,
  never a single value. Audited at H7, written correctly throughout.
- **Every threshold is config.** No magic numbers compiled in, because the balance pass depends
  on changing them without a rebuild.
- **Persisted data is versioned.** Enum values stable and appended, never inserted.
- **Server decides, client renders.** Anything seen or heard comes from a server decision.
- **Performance is a feature, not a phase.** A village is dozens of entities running several
  tasks each, so the cost of *deciding not to act* dominates. Every task staggers its
  thinking across entities, declares how often it actually needs to be asked, and throttles
  hard when no player is nearby. Built into `FFTaskBase` so it cannot be forgotten. E17
  measures; it does not retrofit.

---

## Phase A: Foundations

- [x] **A1 · Toolchain:** cloud build, packaging, deploy, log reading. *Test:* `/ff status`
- [x] **A2 · Villager entity:** seraph model, randomized appearance, gendered voices and hair.
- [x] **A3 · Cultures as data:** names, spoken lines, nametags. *Test:* `/ff cultures`
- [x] **A4 · Dev tooling:** `/ff dump`, `/ff skip <days>`, debug overlay showing each
      villager's current decision, and counters for what we intend to drive to zero
      (recovery teleports, failed paths, stalled builds). **Before anything else.**
      *Done:* `/ff dump`, `/ff skip <days>`, `/ff debug` (live state labels), `/ff stats`.
- [x] **A5 · AI task framework:** **decided: `taskai`.** Attach `EntityBehaviorTaskAI`,
      register our task types with `AiTaskRegistry.Register<T>("code")`, wire priorities and
      cooldowns. *Test:* a villager runs a trivial registered task.
      *Why not `activitydriven`:* it is a JSON-scripted sequence engine (goto, play
      animation, wait, talk) built for **authored** routines, which is exactly what the base
      game's Nadiya villagers need. Ours choose targets algorithmically (which tree, which
      need, which site), so they want code-first tasks. Its action table
      (`ActivityModSystem.ActionTypes`) is public, so we can still borrow it later for
      scripted flavour.
      *Done:* `FFTaskBase` with staggered thinking, think intervals and distance culling;
      `AiTaskRegistry.Register<T>()` wiring; `ffloiter` as the proof task; villagers now
      wander and look around using the base game's own tasks.
- [x] **A6 · Pathfinding:** **RISK.** Vendor VS Village's A* and waypoint graph under
      `src/ThirdParty/` with its MIT notice. Recovery teleports exist but are counted from
      day one. *Test:* `/ff goto`, villager walks around an obstacle without cheating.
- [x] **A7 · Villager inventory, clothing and equipment:** the gear inventory, carry
      capacity, held items, tool slot. *Done:* villagers dress on spawn from a per-culture
      wardrobe held in `cultures.json`; carried loads in the off hand with a capacity;
      a tool in the working hand whose tier drives a work-rate multiplier read from the
      game's own `ToolTier`. *Test:* `/ff give <item>`, then `/ff dump`.

---

## Phase B: Village and economy  *(M1)*

- [x] **B1 · Village object and persistence:** record, claim box, registry, membership,
      saved to world data and restored on load. *Test:* `/ff village create`, reload, still there.
      *Done:* `Village` record (id, name, culture, tier, centre, roster, per-player standing),
      `VillageRegistry` owning creation, removal, lookup by position and membership,
      JSON into world save data with a format version, and `/ff village create|list|info|remove|join|leave`.
      Claim is a square box sized by tier from config. Villagers spawned inside a claim
      join it automatically. A villager whose village no longer exists becomes an orphan
      rather than a dangling reference; walking to a new home is H3.
      *Also done, pulled forward from B3:* the centre cairn, a block with a block entity
      that names the village when you look at it. Breaking it leaves the village intact.
      `/ff village show` outlines a claim on the ground for testing, `/ff village adopt`
      takes in unaffiliated villagers standing inside it, `/ff village mark` replaces a
      broken cairn, `/ff village remove all` clears the lot.
- [x] **B2 · Ledger:** six pools, plus **measured daily flow recorded from real deposits**
      (fast-forward depends on this being honest). *Test:* `/ff dump`
      *Done:* `VillageLedger` on the village record with stock, today's income and
      spending, a seven day history of net movement and a lifetime total. Flow is only
      ever the sum of real movements: nothing can write a rate. Withdrawals refuse rather
      than go negative, which is what makes a builder stall at a half finished wall.
      A day clock on the registry closes the day once per in game day and raises
      `OnNewDay`, which is the hook the brain uses in D2. `ResourceTable` sorts item
      stacks into pools from `config/resources.json`, with per item weights so a log is
      worth more than a stick. Commands: `/ff village ledger|give|take|set|deposit|day|table`.
      Unit tested offline for arithmetic, history capping and save round trip, including
      loading a save written before a pool existed.
      *Corrected after review:* a day only enters the measured history if the village was
      loaded for enough of it. An unwatched day is not evidence of zero production, and
      filing it as one would have left every unvisited village reading as dead and then
      being simulated forward at a rate it never had a chance to earn. The ledger also
      carries a confidence figure now, which F1 blends against a per-tier default so a
      village seen once is not projected forward on one day of luck.
- [x] **B3 · Blocks, block entities and dialogs:** the shared infrastructure for every
      placeable this mod adds. First customer is the **storehouse**: a block entity whose
      inventory is the ledger's visible, lootable face, with a dialog to open it.
      *Done:* `BlockStorehouse` and `BlockEntityStorehouse` on the game's own container
      base, with a chest style dialog. One row per pool, eight columns, and a slot only
      accepts stacks that belong in its row. The grid is rebuilt from the ledger whenever
      it is opened or the stores change, and moving anything writes the difference back
      as a real deposit or withdrawal, so a player helping themselves shows up as
      spending on the day it happened. That is the signal G4 turns into a standing hit.
      Each pool shows whatever villagers last carried in, falling back to a stand-in item
      from `config/resources.json`. Breaking the crate does not spill or destroy anything,
      because the stores live in the ledger; `/ff village storehouse` puts it back.
      The cairn came earlier as the deliberately small first customer for this plumbing.
      *Also:* a living village will not let you break its storehouse at all, and the
      marker grows with the tier: three dropped stones, a stacked cairn, a dressed
      column, a monument. `/ff village tier` drives it until E1 does.
      *Pulled forward from H2:* `Abandon` leaves a ruin rather than deleting anything.
      The cairn stays standing with the dead village's name on it and the storehouse
      becomes an ordinary lootable box holding a fraction of what was left, because a
      settlement does not fail with a full granary.
- [x] **B4 · Beds and workstations:** **cheaper than planned.** The game already has a
      point-of-interest registry (`POIRegistry`, used by beehives and farmland). Our block
      entities implement `IPointOfInterest`; lookup is `GetNearestPoi` / `WalkPois`. We add
      ownership and free-slot logic on top rather than building a registry.
      *Test:* place beds, `/ff dump` shows occupancy.
      *Correction:* `POIRegistry` lives in **VSEssentials**, not VSSurvivalMod, and
      **vanilla beds do not implement `IPointOfInterest`**. So a facility is our own
      record pointing at a position, registered as a point of interest, rather than the
      bed itself being one. Same benefit, one more indirection.
      *Done:* `VillageFacility` for beds and workstations, persisted on the village with
      ownership. Block codes come from `config/facilities.json`, so recognising a forge is
      data. A claim is scanned on founding, on a tier change and on demand, never on a
      timer, because a tier-5 claim is most of a million blocks; between scans the list is
      kept current from the game's own place and break events. Beds are handed out on the
      day tick, nearest free one first, and a bed whose owner is gone frees itself.
      `/ff village scan`, `/ff village beds`, and `/ff dump` names a villager's bed.
- [x] **B4b · Village tether:** villagers stay within their village's claim unless a task
      takes them out, and return when it ends. Currently anchored to spawn point via the
      wander task's `maxDistanceToSpawn`; this step re-anchors it to the claim centre and
      makes leaving require a reason: a job site, a caravan, a raid, fleeing.
      *Test:* a villager wanders a village and never drifts off across the map.
      *Finished properly later:* the first pass only added the "come back" half and left
      the game's own wander task anchored to the spawn point, so villagers stood where
      they were put and never roamed the village at all. `ffwander` replaces it and
      anchors to the claim.
      *Done:* `ffhome`, priority 1.6, above wandering and below an explicit order, because
      being told to go somewhere is itself a reason to leave. Triggers outside the claim
      plus a slack margin and walks them to the centre rather than to the boundary, since
      stopping on the line leaves them one step from doing it again. Runs unobserved: a
      villager quietly walking off the edge of the world while nobody is looking is the
      exact failure it exists to prevent.
- [x] **B5a · The earth pool:** a seventh resource, `Earth`, holding soil, sand, dry grass
      and peat.
      **The case for it is fields, not construction.** Wood and stone are what a village
      actually builds out of at every tier; earth is daub infill at tier 1 and cob at
      tier 2 and then stops mattering for building at all. What does not stop is farmland:
      soil grade is a five step progression that runs forever and is the one material a
      village on poor ground cannot dig its way out of.
      Folding it into clay is the thing to avoid. Pool value is fungible, so a village
      that dug 200 units of dirt into the clay pool could afford a kiln's ledger cost
      without being able to produce a single fired brick. Dirt has to be its own currency
      or it buys pottery. Folding it into stone is nonsense for the same reason.
      **If fields alone do not justify a seventh storehouse row in play, drop it.**
      Nothing else depends on it and the ledger already handles pools coming and going
      across saves.
      Cheap because the ledger stores pools as arrays sized from `VillageResources.Count`
      and `Grow()` already widens a save written before a pool existed, unit tested.
      **Soil grade is a forms ladder on this pool**, the same shape as firewood to planks.
      The farmer stockpiles whatever earth gets dug; what the village can hand back out
      depends on its tier. First pass, against the game's real farmland fertility of
      verylow 5, low 25, medium 50, compost 65, high 80:

      | form | tier | value |
      |---|---|---|
      | `soil-verylow` | 0 | 0.5 |
      | `soil-low` | 1 | 1 |
      | `soil-medium` | 2 | 1.5 |
      | `soil-compost` | 3 | 2 |
      | `soil-high` | 5 | 3 |
      | `sand`, `drygrass`, `peat` | 0 | 0.5 |

      The weights do the trading up on their own: at those values it is six units of dug
      dirt for one of the best soil in the game, so improving ground costs real labour
      without needing a system of its own. There is no terra preta in Vintage Story;
      `soil-high` at 80 is the best there is.
      *Done:* `Earth` is pool 6, and the ledger widened itself on load exactly as
      designed, so old saves gained a row without a migration. The forms ladder above is
      what shipped, minus sand and peat as things a village hands back out: they can be
      deposited, but nobody wants sand returned to them.
      **It did not have to wait for C5 after all.** The digger arrived with it, so the
      pool has a source on day one: a Terrace plot is ground being cut level, and the
      spoil is the material. That was the whole argument for the pool existing and it is
      now a loop you can watch rather than a paragraph.
      *Also done, and worth more than the pool:* every form code in `resources.json` is
      now checked against the running game at load, and anything that does not resolve to
      a real item or block says so in the log. Two hours of this project have been spent
      on a block code typed from memory that quietly did not exist.

- [x] **B5 · Plots:** first-class `VillagePlot`: type, bounds, tier, state. Woodlot, field,
      pasture, quarry, clay pit and mine head all hang off this.
      For a field, the plot's tier records **which grade of soil was laid down**, which
      comes straight out of the storehouse. The grading itself is not a plot mechanic and
      not a separate system: it is the earth pool's forms table, exactly as wood already
      works. See B5a.
      *Done:* a plot is a record on the village, not a block and not a set of marker
      posts, for the same reason a facility is: the ground is ordinary world and the plot
      is the village's opinion about it. So siting one costs nothing but a decision, and a
      plot survives a player rearranging the terrain inside it.
      **Siting is the part that decides whether a village looks planned or scattered**,
      and the rules are deliberately few enough to read: inside the claim, off the town
      square, off other plots, prefer flat, and prefer ground that already has what the
      plot is for. That last one is what puts a woodlot in the trees and keeps a clay pit
      off granite. It takes the best of sixty random candidates rather than searching
      twenty thousand columns, because a village that took the best of sixty looks exactly
      as deliberate and costs a thousandth as much.
      Workers are a roster with a cap rather than a single owner, so a field big enough to
      feed a town is not a one person job and a village does not have to site six
      overlapping fields to employ six farmers.
      No point of interest registration, unlike beds: a village has a handful of plots and
      walking the list is free, where a town has hundreds of beds and needed the index.
      `/ff village plot list|show|add <kind>|site <kind>|remove <id>|clear`, each kind in
      its own colour on the ground. `site` lets the village choose; `add` puts one where
      you are standing, which is the difference between testing the siting rules and
      testing everything downstream of them.
      *Corrected before shipping:* the height map returns zero for an unloaded chunk
      rather than an error, and a plot that recorded a floor of zero would have had its
      digger excavate the whole column to bedrock. Siting now refuses rather than persist
      a number that means "do not know".
- [x] **B6 · Daily schedule:** sleep at night in an owned bed, work by day, shelter during
      temporal storms. The frame every job slots into. *Test:* villagers go to bed at dusk.
      *Done:* `VillageSchedule` is the one place that decides what an hour means, so no job
      ever reasons about the time itself; it asks whether these are working hours.
      Sheltering outranks the clock, read from the game's own temporal stability rather
      than a timer of ours, so a village hides during exactly the storms a player would.
      `ffsleep` at priority 1.8, above the tether, because a villager caught out at dusk
      should be heading for their own bed rather than the village centre. **Beds are
      mountable seats, so villagers genuinely lie in them** rather than standing beside
      one looking tired. A villager with no bed still stands down where they are, which
      makes an overcrowded village visibly overcrowded, and that is the pressure housing
      is meant to apply. `/ff time <hour>` to watch it change.
- [x] **B6b · Personality and reaction:** villagers that respond to the world instead of
      walking through it. Two things, deliberately kept separate.
      *Personality* is a data file, `config/personalities.json`, not a class hierarchy.
      Each entry carries a `boldChance`, a `talkFrequency` and a `tone`, and is rolled
      once at spawn and then persisted, so a villager keeps their manner across reloads.
      The roll turns into one of two courages, timid or bold, and that single value is
      what the reaction task reads. Adding a personality later is an edit to a JSON file,
      which is the whole point: the seven that ship are a starting set, not the design.
      *Reaction* is one task, `ffreact` at priority 1.95, that handles both fighting and
      fleeing rather than two tasks fighting over the same villager. A bold villager
      turns and swings; a timid one runs. Because it is one task, a villager can change
      their mind halfway through, which is what actually happens to people.
      Guards are not rolled. Spearmen, swordsmen and archers are always bold, because a
      guard who runs is not a guard. The one exception is the retreat rule: a guard who
      drops below 40% health **and has another bold villager nearby** will fall back.
      Alone, they hold. The condition is deliberate; retreating only when someone else is
      there to take over is the difference between a rout and a line.
      Speech got quieter and wider at the same time. Idle chance came down, and every
      culture gained situational lines: weather, time of day, being hurt, tired or
      hungry, each with tone variants that fall back to a neutral line when a culture has
      not written one. Fewer words, more of them worth hearing.
      *Test:* `/ff dump` shows manner and courage; hit a villager and watch what they do.

- [x] **B7 · Work loop base:** shared task: travel → act over time → carry → deposit.
      Every producing job below is a subclass.
      *Done:* `AiTaskVillagerWork` owns the loop as a four step state machine, and the
      subclasses answer three questions: what counts as a target, what happens when you
      reach it, and what to do afterwards. A state machine rather than a chain of
      callbacks because it has to survive being interrupted anywhere. A villager can be
      attacked, sent to bed or dragged home by the tether mid swing, and has to pick up
      somewhere sensible rather than from the beginning.
      It inherits the rule the sleep bug taught us: **walking belongs to `ffgoto`**, and a
      deciding task never cancels a journey it handed over, only when its own job is done.
      Jobs sit at priority 1.7, above the tether and below sleep. A villager who has
      drifted out of the claim during working hours is more usefully felling a tree than
      being walked back to the square, but dusk still beats a day's work.
      *Four things came out of the review before this shipped, all of them real:*
      breaking a block was spawning its drops on the ground **and** putting them in the
      villager's hands, so every harvest was doubled; a villager holding something no pool
      accepts could never empty their hands and stood at the storehouse door forever;
      nothing remembered a target that had just been given up on, so the loop walked back
      to the same unreachable block every second; and the scan budget restarted at the
      same corner every pass, so the far side of any plot larger than the budget was never
      looked at at all. All four were the same shape of bug, which is code that reports
      what it meant to do rather than what happened.
      *Maintenance now costs something.* The day clock used to put a broken cairn or
      storehouse back for free, which was a hole: a player could break a village's
      storehouse every morning and it would reappear by lunchtime out of nothing, while
      every other part of the mod obeyed the rule that nothing enters or leaves the ledger
      without a reason. It is stone and wood out of the village's own stores now, and a
      village that cannot pay stays broken and says so.
      **The question that fixed the deadlock:** if the storehouse is what a village stores
      things in, how does it pay for a new one after losing it? It does not need to: the
      stores live in the ledger and the crate is the player's window onto them, so
      villagers now deliver to the village centre when there is no crate standing. Without
      that, one swing of an axe at the wrong moment killed a settlement permanently.
- [x] **B8 · Lumberjack:** fell wild trunks in a radius, replant, haul. `BreakBlock` gives
      us drops, but **the game exposes no "these logs are one tree" helper:** we write a
      flood-fill over connected log and leaf blocks ourselves. *Test:* tree falls, ledger wood rises.
      *Done, and the flood fill is the whole job.* Blocks know they are logs and leaves;
      nothing anywhere says which four hundred of them are one oak. The fill walks all
      twenty six directions, because canopies in this game are not face connected and a
      six way fill leaves half a crown hanging. Leaves are an edge rather than a bridge,
      or two touching oaks become one very large oak. It never walks below where it
      started, so a trunk touching a neighbour's roots does not take the neighbour down.
      Only grown trunks count: `log-placed` is what a player builds a cabin out of, and a
      lumberjack who cannot tell the difference eventually dismantles somebody's house and
      files it as timber.
      A redwood is over a thousand blocks, so felling happens sixty blocks at a time
      across several ticks, top down. All at once was a visible server stall and looked
      like a tree blinking out of existence rather than coming down.
      **Corrected: the flood fill was a reimplementation of something the game already
      does, and the wrong thing was doing it.** Vintage Story fells a tree when the base
      of the trunk is cut, and it is the *axe* that does it, not the block:
      `ItemAxe.OnBlockBrokenWith` finds the whole tree and takes it down with the game's
      own felling groups, its reduced drops from leaves and branchy wood, and its
      durability cost. Villagers were breaking blocks with no tool at all, so none of that
      ever fired, and the fill written here would have drifted out of step with the real
      rules the first time the game changed them.
      So the lumberjack swings an axe the way a player does, and a villager with no axe
      gets one log for exactly the reason a player with no axe gets one log. That makes
      the tool matter for a real reason rather than as a speed multiplier, which is why
      villagers are now handed the simplest tool their trade needs at spawn
      (`GiveTradeToolsOnSpawn`, found by asking the item registry rather than by writing a
      code down).
      The timber lands on the ground, because that is what the game's felling does, and
      the lumberjack gathers it. That reads better than logs teleporting into somebody's
      arms, and a player walking past a fresh stump finds whatever has not been picked up.
      **Tools wear out, and coming back is a walk.** A village makes tools ahead of the
      need and racks them at the storehouse; a villager with nothing in their hands walks
      over and collects one. The first version had the tool appear in their hand wherever
      they happened to be standing, which is the same poof-magic the storehouse and the
      build sites both refuse, and Corey caught it in the same breath as asking for a rack.
      The rack is what makes a smith worth having. A village without one knaps something
      in the evening and turns out a single tool a day; each smith makes four. After a bad
      week that is the difference between a workforce back on its feet tomorrow and one
      bare handed for most of a season.
      What gets made is what is actually wanted, which is the trades of whoever is
      currently empty handed. No point knapping hoes for a village of lumberjacks.
      **Knapping itself is not simulated**, and that is worth saying plainly rather than
      letting it read as a feature: nobody kneels at a knapping surface, the village
      simply spends the stone. Making it visible is a toolmaker job and belongs with the
      craft chain in Phase E.

      Before this a lumberjack whose axe broke
      quietly went back to taking one log at a time and nothing anywhere said why, which
      is a village that stops growing for a reason nobody can see. Each morning the
      village re-equips anyone bare handed, paying metal or stone for the head depending
      on what it has learned to work and wood for the handle either way, and a village
      that cannot pay leaves them bare handed and says so. A day rather than the instant
      it snaps, because a tool reappearing the moment it breaks reads as magic and a
      worker finishing the afternoon bare handed does not.
      Which tier of tool a village can make is its own ladder,
      `BestToolTierByVillageTier`, so a founding hamlet knaps stone and replaces a broken
      axe with another stone one rather than a steel one. Every trade's tool type lives in
      `config/tools.json` rather than in code, because whether a herder carries shears or
      a knife is a balance decision worth arguing with without a rebuild.
      **Replanting does not run on vanilla luck, but only for lumberjacks.** Leaf drops
      are rare enough that a woodlot living off them thins out and never recovers, which
      makes the plot pointless, so a lumberjack keeps two saplings from each tree they
      fell. That is a lumberjack's skill and nothing else: the saplings are created in
      their hands rather than by changing what leaves drop, so a player breaking the same
      leaves gets exactly the rare chance they always did. Which sapling comes from the
      world catalogue, matched to the log, so an oak woodlot stays an oak woodlot. Set
      `SaplingsPerTree` to zero for vanilla rates only.
- [x] **B9 · Farmer:** till, sow from retained seed, water, reap, replant, rotate against
      the game's real N/P/K.
      *Plus soil improvement:* relaying a field with the best grade the storehouse can
      currently produce, so a village's fields visibly improve as it climbs. The grading
      is the earth pool's job (B5a); the farmer's job is digging the raw material and
      laying the result.
      *Done:* reap, relay, till, sow, in that order. Reaping first because a ripe crop
      left standing is the only one of the four that can be lost.
      **Seed comes out of the village's own food**, which is what makes sowing a decision
      rather than free growth. A village down to its last meal cannot plant its way out.
      **The bug worth recording:** farmland takes its nutrient levels from the soil block
      it was made from, and laying a farmland block without telling it which soil it came
      from produces ground with zero fertility that recovers toward zero forever. It looks
      like the best soil in the game and grows crops at a tenth speed. So `Till` calls
      `OnCreatedFromSoil` exactly as the game's own hoe does, and `Relay` carries the old
      field's moisture and nutrients across before raising its ceiling, rather than paying
      two earth to make an established field worse.
      **Rotation is in, and it is a rung on the ladder rather than a switch.** From tier 2
      a farmer reads the ground's own nitrogen, phosphorus and potassium off the farmland
      and sows whichever crop eats the nutrient the soil still has most of, so a field
      that has been growing cabbage until its nitrogen is gone gets something that wants
      phosphorus next and the nitrogen recovers underneath it. Below that tier they
      scatter whatever seed they have. A village should be seen to get better at farming
      rather than being born knowing how, and `RotateCropsFromTier` is where to argue with
      the gate.
      *Still to come:* watering.
- [x] **B10 · Herder:** troughs from stored grain, cull to cap, eggs and wool.
      *Done:* what goes in a trough comes from **the trough's own content list** rather
      than a guess, since each one declares what it takes and how much makes a fill level.
      Feeding is paid for out of the food pool, so animals are a way of turning grain into
      meat and eggs rather than a source of free food.
      Culling reads the animal's own drop table, so it gives what a player butchering the
      same animal would get and works unchanged for modded animals. Adults only, never
      below a floor: a herd should be trimmed, not eaten out of existence.
      Fertilised eggs are left alone, because a fertile egg is a chicken the village has
      not got yet. That made "how many eggs are here" and "how many can I have" different
      questions, and asking the wrong one had the herder walking back to a full nest he
      could never empty.
- [x] **B11 · Trickle jobs:** forager and deadfall gatherer. The low-yield renewable
      bootstrap that stops a badly-sited village being dead on arrival.
      *Done as one job.* Everything else in the mod needs something the ground has to
      already have: a village on bare rock has no woodlot to fell and no field worth
      tilling, and would starve while its lumberjack waited for a forest. A forager finds
      something almost anywhere, slowly, and slowly is the design. A village that could
      live off berries forever would never need to farm.
      It works the claim rather than a plot, because fencing off a berry patch is absurd.
      Berry bushes are **picked, not pulled up**: the fruit comes off and the bush stays,
      or a renewable bootstrap gives one harvest and then nothing forever.
      What is worth picking is decided by asking the resource table, not by a second list
      of mushroom names kept in sync by hand, so the forager walks past the deathcaps.
      **Deliberately one job, not two.** The entry above asked for a forager and a deadfall
      gatherer; a separate gatherer would have been the same loop with a different list of
      block codes, and sticks are already on the forager's list. Merged on purpose rather
      than forgotten.

- [x] **B12 · Digger, which is C5 arriving early:** the builder cuts a Terrace plot level
      and keeps the spoil.
      This was going to wait for Phase C, and it should not have. The earth pool needed a
      source or it was a storehouse row full of nothing, and terracing is that source.
      The point, which took two wrong answers to arrive at: **earth is not what a village
      builds its walls out of.** Walls are wood and stone. Earth is cob and daub, which
      the game's own recipes make from soil and dry grass, and which is what tier 1 and
      tier 2 houses are made of. So the spoil from levelling a site is not waste to cart
      off, it is the material the building is made from, and levelling the ground and
      gathering the material are one job rather than two.
      It cuts down to the plot's recorded floor and no further, and refuses to work at all
      on a plot whose floor was never properly read. A digger with no floor to stop at
      excavates to bedrock.
      Rock is deliberately not diggable: cutting a terrace through a hillside of soil is a
      day with a shovel, and cutting one through granite is a quarry, which is a different
      plot and a different worker.

---

## Phase C: Construction  *(M1)*

Nothing in Phase D can place a building until this exists.

- [ ] **C0 · The village monument:** at tier 4 and 5 the marker should stop being a
      single block and become a **built structure on a 2x2 or larger footprint**, standing
      in a square rather than in the dirt, with variants: a carved figure, a beast, a
      villager holding the tool their village is known for. That is not something to
      write as shape JSON by hand and have it look like anything. It is a schematic, so
      it waits for this phase and goes on the list of things to ask builders for.
      Until then the single-block monument stands in.

- [ ] **C1 · Placeholder schematics:** *Hand-built in game.* Around eight crude
      structures (hovel, log house, farmhouse, storehouse, shed, forge, well, wall segment)
      exported via WorldEdit. Deliberately ugly; real ones come at E2 once the ladder stops
      moving. **Nothing in Phase C or D can be tested without them.**
- [x] **C2 · Schematic catalogue:** **every building carries two costs, and both are
      read out of the schematic, never written by hand.** The *bill of blocks* is what
      physically gets placed, 15 oak logs and 6 planks. The *ledger cost* is what those
      blocks are worth in pool value, so 15 logs at 4 each plus 6 planks at 1 is 66 wood.
      The village pays the pool cost out of its stores and the builder places the blocks.
      That is what lets a village that only has firewood still afford a log cabin: it has
      the wood, and turning wood into the shape the building needs is the craft chain's
      job, gated by tier. A settlement with no saw cannot spend its wood on anything that
      needs planks.
      Then: load, validate every block code, classify by size,
      **derive the bill of materials and build duration from the blocks themselves**, plus a
      per-culture manifest carrying what a schematic can't know about itself: which need it
      satisfies, which trade it houses, its tier, its footprint in cells.
      *Test:* `/ff buildings` lists what loaded with its costs.
      *Done, and it ships loading nothing,* because C1 has not happened yet: the folder is
      there with instructions in it, `config/buildings.json` is an empty list with the
      field meanings written out, and `/ff buildings` says so rather than looking broken.
      Both costs come out of the file. The bill of blocks is a count per block code; the
      ledger cost is each of those blocks valued through the same resource table the
      storehouse uses, so the two numbers cannot drift apart the way two hand written
      numbers would.
      **Two bugs here would each have sunk the whole phase, and both were silent.** The
      numbers in a schematic's `BlockIds` are keys into *its own* code table, not block
      ids in this world, and they came from whichever machine exported the file: treating
      one as a live id builds a house out of whatever happens to sit at that slot in the
      player's registry, which changes the moment they install another mod. And
      `Assets.GetMany` takes a `loadAsset` flag that defaults to true; passing false left
      every schematic unhydrated, so each one read as an empty string and failed to parse
      with no error text at all, which looked exactly like an empty folder.
      Block positions are unpacked by the schematic's own `GetJustPositions` rather than
      by picking the packed integer apart here. That bit layout is the engine's file
      format and reimplementing it would work right up until the day it silently did not.
- [x] **C3 · Build site:** marker block, scaffold, progressive construction over in-game days.
      *Done, without the marker block.* A build site is a record on the village, the same
      shape as a plot and for the same reason, and the thing you see is the building
      itself going up eight blocks at a time. A scaffold block would be a second thing to
      keep in sync with the record, and the record is what is true.
      Progress is a block count rather than a percentage, because what actually happens is
      that a builder places blocks one at a time and the count is where they got to. A
      percentage would be a number derived from that and then trusted instead of it, which
      is the shape of most of this project's bugs.
      *The save hazard worth recording:* that count is an index into a list rebuilt from
      the world's block registry at every startup. Install another mod and the list is a
      different length, so the cursor now points somewhere else, and left alone that
      quietly marks a third built house finished. The site records the length it was
      created against and starts over when it does not match, which is cheap: placing a
      block that is already there costs nothing.
      Fluids go on their own block layer. Writing one into the solid layer erases the
      block placed under it a moment earlier, which is how a well becomes a hole.
- [x] **C4 · Builder draws materials:** spends the *ledger cost* and places the *bill of
      blocks*, converting one into the other only for forms the tier has unlocked.
      Consumes from the ledger and **stalls visibly** when
      empty. *Test:* start a build with no wood; nothing happens until wood arrives.
      *Done.* Materials come out **once, before a single block goes down**, and the site
      refuses to start otherwise. Paying per block would leave a village that ran dry
      halfway with a shell it could neither finish nor recover the stone from; the rule is
      that a stalled build stalls at the start.
      A stalled site carries the shortfall in words, so a player walking past a site that
      has not moved in three days can look at it and read that the village is forty stone
      short.
      Abandoning a site that was paid for refunds it. A village that loses sixty wood to a
      bug is a village whose ledger stopped meaning anything.
      *And the builder stands down rather than standing about:* waiting at a site the
      village cannot afford used to hold the highest priority slot for the whole working
      day, which meant the one villager who might have closed the shortfall was the one
      prevented from working. They now go and do something else for two minutes and come
      back.
- [ ] **C3 · Build site:** marker block, scaffold, progressive construction over in-game days.
- [ ] **C4 · Builder draws materials:** spends the *ledger cost* and places the *bill of
      blocks*, converting one into the other only for forms the tier has unlocked.
      Consumes from the ledger and **stalls visibly** when
      empty. *Test:* start a build with no wood; nothing happens until wood arrives.
- [ ] **C5 · Terracing:** **the cut is where an early village gets its building material.**
      Cob and daub, which is what tiers 1 and 2 put up houses in, not the palisade or the
      curtain wall: those are wood and stone and have nothing to do with earth. The game's
      own recipes settle this: cob is 5 soil and 4 dry grass, daub is soil, sand, clay and
      grass. So the spoil from levelling a site is not waste to be moved, it is the
      material the building is made of, and a settlement that works its ground can put up
      cob houses before it has a clay pit. Demand drops away at tier 3 when construction
      moves to fired brick, so this is an early-game loop rather than a standing one.
      Blocks cut are broken properly and their drops go through the resource table into
      the ledger, so this mostly falls out of machinery that already exists.
      **Hard limit: only a queued building's footprint, never speculative.** A tier-5
      claim is 193 blocks across and a village that levels it leaves a dirt pancake where
      the player's terrain used to be. Terracing works a site toward buildable, not flat.
      Then: cut uphill, fill downhill, retain in the tier's material, before
      raising anything. *Test:* a house on a slope that doesn't look pasted on.

---

## Phase D: The brain  *(M2)*

- [ ] **D1 · Needs model:** scored, ranked need list. Starvation 1000, exposure 800,
      homelessness 400, profession gap 200, upgrade 60, expansion 20.
- [ ] **D2 · Daily tick:** one server pass per in-game day per loaded village, hung off the
      calendar's day boundary. *Test:* `/ff skip 5`, log narrates five days of decisions.
- [ ] **D3 · Grid and streets:** 7-block cells, 2-block streets, 8-cell modules, street
      laying, waypoints dropped as it paves (so pathfinding improves as a village develops).
      Culture changes the rule: Norman lays streets first, Norse clusters round the longhouse.
- [ ] **D4 · Site selection:** **RISK.** Interior cells for buildings, peripheral for plots,
      and off-grid for what must follow terrain, like a quarry against rock or a well at the water table.
- [ ] **D5 · Build planner:** need → culture catalogue → affordable → sited → enqueued.
- [ ] **D6 · Labour allocator:** target headcount per trade, the authored aptitude matrix,
      crisis tasking. *Test:* starve a village, watch the smith farm badly.
- [ ] **D7 · Population:** immigration on surplus plus a free bed, and the founding kit
      (seed, saplings, breeding pairs).
- [ ] **D8 · Cell churn:** a tier-1 field beside the houses relocates outward when that
      ground becomes valuable. What makes a village densify rather than merely spread.

---

## Phase E: The ladder  *(M3)*

- [ ] **E1 · Tiers and gates:** definitions, conditions, sustained-hold check, all config.
      Every gate reads three kinds of condition, and all three matter:
      **stock** (clay ≥ 200), **flow** (food surplus ≥ 20/day for 10 days, which is why
      B2's measured rate had to be honest), and **coverage** (every bed heated, claim
      enclosed, every trade staffed).
      *Added:* a **housing standard** on every gate, at least 70% of dwellings built to
      the previous tier's material or better, config as `HousingStandardFraction`.
      Without it nothing ever forces a village to replace what it already has, and a
      tier-4 settlement could be a field of tier-0 hovels with a bloomery beside them.
      The gates as written only ask whether *new* things exist. This is what makes the
      whole-village rebuild mandatory, and the rebuild is the single most legible thing
      the ladder can show a player who has been away. Deliberately not 100%: one stubborn
      old hovel in the corner of a stone town is character.
- [ ] **E2 · Per-tier dwellings and walls:** one dwelling and one perimeter per tier, 0–4,
      two cultures. Tier-1 wall is stakes with gaps; tier-4 is mortared stone.
- [ ] **E3 · Infrastructure buildings:** woodshed, well, compost yard, smokehouse, granary,
      cellar, lamp posts, market, workshops. Roughly two thirds of the ~40-building catalogue.
- [ ] **E4 · Woodlot:** planted plot, forester replaces lumberjack at tier 2, sapling and
      seed recoup tuned so renewables trend stable rather than draining.
      *The woodlot lays its own ground.* The blueprint includes a soil base under the
      planting grid, so a village founded on bare rock is not permanently wood-locked: it
      digs earth, lays a bed, and grows its own timber. Deliberately **not** touching how
      trees and fertility work, because vanilla tree growth does not read fertility and
      there is no reason to invent that. Soil here is a requirement for planting at all,
      not a yield modifier.
      A stone village still has to get its first saplings from somewhere, which means
      trade, and that is a fine thing for it to need.
- [ ] **E5 · Extraction points:** quarry, clay pit, mine head. **Confirmed readable:**
      `GenDeposits.Deposits` in `Vintagestory.ServerMods` exposes the deposit variants the
      prospecting pick uses, so mine-head yields really can be seeded from local geology.
      8–12% floor so nowhere is bricked; scales with tier.
- [ ] **E6 · The craft chain:** *the tier gate on item forms already exists in
      `config/resources.json`: each pool lists the shapes its value can take and the tier
      that unlocks each. Planks at tier 2, fired brick at 3, iron at 4. This step is what
      makes those unlocks cost a workshop and a worker rather than just a number.*
      Then: charcoal burner → smith → tools, with **tool tier feeding
      back into everyone's work rate**. The loop that makes progression accelerate.
- [ ] **E7 · Remaining trades:** potter, mason, miller, baker, weaver, clothier, tanner,
      cook, preserver, angler. Each a subclass of B7 plus a workstation.
- [ ] **E8 · Preservation and winter:** smokehouse, village-only recipes, food buffer.
      *Test:* a village that survives its first winter.
- [ ] **E9 · Fire tender:** hearths, torches and lamps as *maintained* state that burns down
      and needs refuelling. A failing village goes dark before anything else breaks.
- [ ] **E10 · Heat and light coverage:** detect whether a bed has a heat source in range and
      how much of a claim is lit. Both are tier gates and both need real queries.
- [ ] **E11 · Claim spawn suppression:** lit, enclosed ground suppresses hostile spawns, so
      completing the defensive work visibly changes what nights are like.
- [ ] **E12 · Guards:** posts, day and night watch, gear made by the smith from real metal.
      Temporal storms are the recurring test the base game already schedules for us.
- [ ] **E13 · Death and attrition:** a killed villager is removed from the roster, their bed
      and workstation freed, their trade knowledge potentially lost. Feeds decline.
- [ ] **E14 · Wounds and the healer:** persistent wounds cut work rate; an infirmary turns a
      raid that costs eight people into one that costs three.
- [ ] **E15 · Worldgen village placement:** villages generate, gated on temperature, rainfall
      and forest density per culture, semi-rare with configurable density. **Until this exists
      every village is one you typed a command to create.**
- [ ] **E16 · Claim growth:** a village annexes adjacent land when it needs a plot and has
      none, up to a cap, so expansion is visible rather than fixed at worldgen.
- [ ] **E17 · Performance budget:** profile a loaded village of 25–30 with jobs running.
      Measure ms/tick for pathfinding, the brain, and entity count. **If a single village
      can't run at a sane cost, several systems get cheaper before Phase F, not after.**
- [ ] **E18 · Pacing pass:** first real balance work, including the rule that **metal gates
      advancement but never survival**. *Test:* `/ff skip 120` on rich and poor geology.

---

## Phase F: Persistence  *(M4)*

- [ ] **F1a · Projected yield:** what a village *should* produce, computed from its own
      workers, plots, tier and tools rather than from measurement. This is what an
      unvisited village grows on, and what measured flow is blended against by confidence.
      Without it a village nobody has walked past can never advance. Depends on B5 plots
      and D6 labour, so it cannot move earlier than this.
- [ ] **F1 · Fast-forward:** **RISK.** Self-calibrated rates from B2's measured flow, event
      stepping, season boundaries. Fairness is the hard part.
- [ ] **F2 · Event log:** one line per applied event. Debugging tool *and* what the headman
      tells you about later.
- [ ] **F3 · Deferred placement:** buildings finish in the ledger, blocks appear on chunk load.
- [ ] **F4 · Home village:** `KeepLoaded` chunks, server cap, clean release.
- [ ] **F5 · Divergence harness:** 200 days live vs 200 fast-forwarded from one seed, diff the
      end state, fail past tolerance. **Write alongside F1, not after.**

---

## Phase G: People and standing  *(M5)*

- [ ] **G1 · Families:** ages, parentage, inherited trades. Losing the last smith loses the knowledge.
- [ ] **G2 · Births:** children, growth, succession into a parent's trade.
- [ ] **G3 · Graves:** a named marker per death. Reads a village's history at a glance.
- [ ] **G4 · Reputation:** per-player standing; gifts valued against the ledger's deficit;
      theft from the storehouse tracked against you.
- [ ] **G5 · Standing changes behaviour:** guards stop shadowing you, doors stop being barred,
      the headman greets you by name. The number is invisible; this is how you feel it.
- [ ] **G6 · Notice board:** block, notices generated from need scores, three urgency
      registers, culture voice.
- [ ] **G7 · Headman dialogue:** village state and event log, detail gated by standing.
- [ ] **G8 · Trading:** scarcity pricing, barter beating coin, signature goods.
- [ ] **G9 · Joining a village:** citizenship at Trusted, a dwelling assigned **or built for
      you**, storehouse access both ways, guards defend you, counted in the population.
- [ ] **G10 · Founding a village:** founding stone, settlers arrive, then the brain takes over.
      **It runs itself; you don't command it.**

- [ ] **G11 · Town life:** the density and activity that make a settlement read as alive,
      deliberately **not** a tier. Every tier in this design is a material, and "more life"
      has none, so it would be the one stage with no visual identity and an arbitrary gate.
      It also should not switch on at the top: a tier-2 village wants a woodpile by the
      door too, just fewer of them. So it scales continuously with population and
      prosperity instead.
      Market stalls around the square, count scaling with trade volume, staffed on market
      days. Street furniture the builder places in leftover cells when there is nothing
      left to construct: woodpiles, drying racks, benches, barrels, handcarts, washing
      lines. Shop signs on job buildings. A market day that pulls the schedule to the
      square instead of to work. Lighting density rising with prosperity rather than tier.
      And **sprawl without a tier**: a tier-5 town keeps appending grid modules as its
      population grows, so a town two hundred days old is visibly bigger than one that
      just arrived. This is what the sixth tier was asking for, and none of it needs one.

---

## Phase H: Decline, and shipping  *(M6 → v1.0)*

- [ ] **H1 · Decline:** the four-stage slide: strain, regression, attrition, ruin. Recovery
      rolls throughout; failure weighted to tiers 0–1.
- [ ] **H2 · Ruins:** decay as an *absence of maintenance*, structures left standing.
- [ ] **H3 · Orphans:** when a village dies, the survivors don't simply vanish. They set
      out for the nearest living village within range and are adopted into it, bringing
      their trade and their name with them. A settlement that takes in refugees gains
      population it did not grow, and the arrivals carry the memory of what happened,
      which is the headman's account of a neighbour's fall coming from someone who was
      there. Villagers with nowhere reachable to go die on the road.
- [ ] **H4 · Reclamation:** thriving villages take over dead ones and inherit the walls.
- [ ] **H5 · Terrain smoothing:** **RISK.** Worldgen pass at TerrainFeatures 0.15, target
      surface per region to avoid chunk seams, capped displacement, never below water.
- [~] **H6 · Config surface:** every contentious number exposed and documented.
      *Brought forward:* built at the end of Phase A rather than the end of the project,
      because 35 hardcoded values had already accumulated and retrofitting fifty is worse
      than starting from a config object. Movement, chatter, carrying, tool rates and the
      performance throttles are live and reloadable. Remaining: everything Phases B-G add.
- [ ] **H7 · Save migration:** version the persisted schema and prove an old save loads.
- [ ] **H8 · Multiplayer audit:** per-player standing, home village cap, shared damage vs
      individual blame, two players in one village.
- [ ] **H9 · Balance pass:** long playthroughs, tuning.
- [ ] **H10 · Release engineering:** semantic versioning, changelog, the mod's ModDB entry,
      install and config documentation for players, and a compatibility statement naming
      the Vintage Story versions supported. Then **v1.0**.

---

## After v1.0

Designed in full, deliberately unstarted.

| Release | Contents |
|---------|----------|
| 1.1 | Relations, caravans, inter-village trade, rival pairing at worldgen, daughter villages |
| 1.2 | Brigands, garrison rosters, levies, war states, sieges, wealth-scaled raiding, unit composition |
| 1.3 | Inter-village roads, bridges, caravan speed |
| 1.4 | Delvers, Woodfolk, Emberkin, tier 5 Town, water as a resource, culture-specific hair and layout |

---

## Audit notes

Two passes were run over this list against the design document.

**Pass 1, dependency audit.** For each step, what does it silently assume exists? This
found more than the content pass did:

| Assumed by | Was missing | Now |
|---|---|---|
| Pathfinding, every job, guards | An AI task framework at all | A5 |
| Carrying, tool tier, guard gear | Villager inventory and equipment | A7 |
| Storehouse, markers, notice board | Block, block-entity and dialog infrastructure | B3 |
| "beds ≥ population", exposure need | Bed and workstation ownership | B4 |
| Every job | A day/night schedule to hang work on | B6 |
| Schematic catalogue | Someone actually making schematics | C1 |
| Village registry | World save data persistence | B1 |
| Lumberjack | Tree topology and entity block-breaking | B8 |
| Daily tick | A calendar day-boundary hook | D2 |
| Aptitude penalties | The authored aptitude matrix | D6 |
| Warmth and light gates | Heat-source and light-level queries | E10 |
| Decline, guards | Death removing a villager from the roster | E13 |
| Mine head yields | Whether ore density is even readable | E5 |

**Pass 3, found while building.** Three more, all from playing with what exists:
villagers wearing clothes (A7 covered inventory but never dressing), a tether keeping them
near their village (nothing covered it at all), and orphaned villagers migrating to a
neighbour when their village dies (H2 left them simply gone). Playing the thing finds what
reading the plan cannot.

**Pass 2, content audit.** Every system in the design document, checked against a step.
This found four: infrastructure buildings (only dwellings and walls were covered),
worldgen village placement, claim spawn suppression, and standing visibly changing NPC
behaviour rather than just being a number.

**Still deliberately absent:** save migration existed only as a cross-cutting rule, so it
is now H6 with its own test.

---

## Known unknowns

Things this plan assumes about the *engine* rather than about the design. Each is written
as an assumption on purpose, so it gets checked rather than discovered.

### Resolved

Checked by compiling a probe file against the real 1.22.7 assemblies. All of these exist
and are reachable from a mod:

| Assumption | Verdict |
|---|---|
| A mod can register its own AI tasks | ✅ `AiTaskRegistry.Register<T>("code")` |
| A point-of-interest registry exists for beds and workstations | ✅ `POIRegistry.GetNearestPoi` / `WalkPois`. **Saves building one** |
| Day boundaries and seasons are queryable | ✅ `Calendar.TotalDays`, `HourOfDay`, `GetSeason(pos)` |
| **Buildings can be placed as chunks load** | ✅ `Event.ChunkColumnLoaded` and `ChunkLoadOptions.OnLoaded`. **The whole fast-forward design rested on this** |
| Village data can persist in the world save | ✅ `SaveGame.StoreData` / `GetData` |
| Villagers can carry and hold things | ✅ `InventoryGeneric`, `LeftHandItemSlot`, `RightHandItemSlot` |
| Schematics load and place from a mod | ✅ `BlockSchematic.LoadFromFile` / `Place` |
| Entities can break blocks and get drops | ✅ `BlockAccessor.BreakBlock` |
| Regional ore density is readable | ✅ `GenDeposits.Deposits` (`Vintagestory.ServerMods`) |
| Chunks can be force-loaded and released | ✅ `LoadChunkColumnPriority` + `KeepLoaded`, `UnloadChunkColumn` |

### Still open

| Assumption | Step at risk | How we find out |
|---|---|---|
| A tree can be identified as one object | B8 | No engine helper, we write a flood-fill |
| `/ff skip <days>` works without wrecking weather and crops | A4 | Try it; fall back to advancing only the village tick |
| A 30-villager village fits a sane tick budget | E17 | Profile it, and don't wait until Phase H |
| Nametags render a second line | A2 | Waiting on an in-game check |

**This list is expected to grow.** Adding to it is the plan working, not failing.

---

## The four things most likely to go wrong

1. **A6 pathfinding:** hardest inherited piece; everything after it slips if it fights us.
2. **D4 site selection:** will look bad for a long time before it looks good.
3. **F1 fast-forward fairness:** silent drift only the harness will catch.
4. **E17 performance:** a village of thirty running jobs and pathfinding may simply cost
   too much, and the fix is architectural rather than local.

**None of them can be allowed to kill the mod.** Pathfinding falls back to the engine's own,
site selection to flat-ground-only placement, fast-forward to loaded-villages-only, and
terrain smoothing can simply not exist. Each has a defined retreat.

---

## Bug hunt, 0.7.0

Three reviewers over the whole mod rather than only the new code, then a fourth over the
fixes themselves. Twenty three real defects. Recorded here because most of them are the
same handful of mistakes wearing different hats, and the patterns are worth keeping.

**The tether, finally explained.** Not a guess this time. The AI manager starts tasks in
the order the entity file lists them and re-reads the slot on each one, so a task listed
after another can take the slot on the same tick the first one started. Every producing
job is listed after the tether and outranks it, so the tether started, ordered a walk, and
was preempted before its own retry logic had run once. It restarted and lost again,
forever. Priority could not settle it, because a job genuinely should outrank going home
during working hours. What settles it is that **a villager outside their own claim has no
business working**: whatever they are standing next to is not the village's to take. The
jobs stand aside and the tether gets its slot.

Three things fell out of that one:

- Ending a task does not hand the slot to anything else. The engine offers a freed slot
  again on the very next tick, and a task whose `ShouldRun` is still true simply retakes
  it. Sleep, the builder and the tether all did this after giving up, so "give up" meant a
  fresh pathfind every few seconds forever. They stand down for a while now.
- `FFTaskBase` measured its think interval by counting calls, and the engine only asks a
  task whether it wants to run when it could actually win the slot. So a low priority task
  froze while something else held the slot and then had to wait its full interval again
  from the moment the slot freed, by which point a shorter-interval task had taken it. Real
  elapsed time now.
- The tether stopped the instant a villager crossed the line, leaving them standing on it,
  one step from being dragged back and in exactly the band where the fight happened. It
  walks them properly inside now.

**Free resources, three ways.** Every harvest was doubled, because a block was broken with
its drops still enabled after they had already been put in the villager's hands. Cooking
printed food, because a serving was worth two and a carrot was worth one. Culling gave an
animal's loot twice, once into the herder's hands and once onto the ground. And anything
that spent from the ledger without telling the storehouse left the shelves showing the old
figure, so emptying a row handed over a full row of goods for a partial debit. Every spend
now goes through one method that keeps the crate honest.

**Silent no-ops.** Two would each have sunk Phase C on their own: schematic block ids are
keys into the file's own table rather than ids in this world, and an asset loader flag left
every schematic reading as an empty string. Neither produced an error. The lesson has been
learned enough times now to be a rule: **anything read from a file gets checked against the
running game at load, and says so in the log when it does not resolve.**

**Integer division.** `-10 / 32` is 0, not -1, so every village west or north of the origin
asked about the wrong chunk when deciding whether it had been running, and got a granite
cairn whatever its bedrock was. Shifts and masks now.

**And the one that was my own fix.** Widening the ledger's filed history so a new pool did
not read as untrusted turned "no data" into a measured zero, which was worse: fast-forward
would then blend toward nothing and the starvation check could never fire for that pool.
Short rows stay short and confidence is counted per pool instead. Fixing a confident lie
by making it a different confident lie is a mistake worth naming.
