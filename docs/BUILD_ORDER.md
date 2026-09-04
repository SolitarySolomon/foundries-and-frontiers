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
      rather than a dangling reference.
- [ ] **B2 · Ledger:** six pools, plus **measured daily flow recorded from real deposits**
      (fast-forward depends on this being honest). *Test:* `/ff dump`
- [ ] **B3 · Blocks, block entities and dialogs:** the shared infrastructure for every
      placeable this mod adds. First customer is the **storehouse**: a block entity whose
      inventory is the ledger's visible, lootable face, with a dialog to open it.
- [ ] **B4 · Beds and workstations:** **cheaper than planned.** The game already has a
      point-of-interest registry (`POIRegistry`, used by beehives and farmland). Our block
      entities implement `IPointOfInterest`; lookup is `GetNearestPoi` / `WalkPois`. We add
      ownership and free-slot logic on top rather than building a registry.
      *Test:* place beds, `/ff dump` shows occupancy.
- [ ] **B4b · Village tether:** villagers stay within their village's claim unless a task
      takes them out, and return when it ends. Currently anchored to spawn point via the
      wander task's `maxDistanceToSpawn`; this step re-anchors it to the claim centre and
      makes leaving require a reason: a job site, a caravan, a raid, fleeing.
      *Test:* a villager wanders a village and never drifts off across the map.
- [ ] **B5 · Plots:** first-class `VillagePlot`: type, bounds, tier, state. Woodlot, field,
      pasture, quarry, clay pit and mine head all hang off this.
- [ ] **B6 · Daily schedule:** sleep at night in an owned bed, work by day, shelter during
      temporal storms. The frame every job slots into. *Test:* villagers go to bed at dusk.
- [ ] **B7 · Work loop base:** shared task: travel → act over time → carry → deposit.
      Every producing job below is a subclass.
- [ ] **B8 · Lumberjack:** fell wild trunks in a radius, replant, haul. `BreakBlock` gives
      us drops, but **the game exposes no "these logs are one tree" helper:** we write a
      flood-fill over connected log and leaf blocks ourselves. *Test:* tree falls, ledger wood rises.
- [ ] **B9 · Farmer:** till, sow from retained seed, water, reap, replant, rotate against
      the game's real N/P/K.
- [ ] **B10 · Herder:** troughs from stored grain, cull to cap, eggs and wool.
- [ ] **B11 · Trickle jobs:** forager and deadfall gatherer. The low-yield renewable
      bootstrap that stops a badly-sited village being dead on arrival.

---

## Phase C: Construction  *(M1)*

Nothing in Phase D can place a building until this exists.

- [ ] **C1 · Placeholder schematics:** *Hand-built in game.* Around eight crude
      structures (hovel, log house, farmhouse, storehouse, shed, forge, well, wall segment)
      exported via WorldEdit. Deliberately ugly; real ones come at E2 once the ladder stops
      moving. **Nothing in Phase C or D can be tested without them.**
- [ ] **C2 · Schematic catalogue:** load, validate every block code, classify by size,
      **derive the bill of materials and build duration from the blocks themselves**, plus a
      per-culture manifest carrying what a schematic can't know about itself: which need it
      satisfies, which trade it houses, its tier, its footprint in cells.
      *Test:* `/ff buildings` lists what loaded with its costs.
- [ ] **C3 · Build site:** marker block, scaffold, progressive construction over in-game days.
- [ ] **C4 · Builder draws materials:** consumes from the ledger and **stalls visibly** when
      empty. *Test:* start a build with no wood; nothing happens until wood arrives.
- [ ] **C5 · Terracing:** cut uphill, fill downhill, retain in the tier's material, before
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
- [ ] **E2 · Per-tier dwellings and walls:** one dwelling and one perimeter per tier, 0–4,
      two cultures. Tier-1 wall is stakes with gaps; tier-4 is mortared stone.
- [ ] **E3 · Infrastructure buildings:** woodshed, well, compost yard, smokehouse, granary,
      cellar, lamp posts, market, workshops. Roughly two thirds of the ~40-building catalogue.
- [ ] **E4 · Woodlot:** planted plot, forester replaces lumberjack at tier 2, sapling and
      seed recoup tuned so renewables trend stable rather than draining.
- [ ] **E5 · Extraction points:** quarry, clay pit, mine head. **Confirmed readable:**
      `GenDeposits.Deposits` in `Vintagestory.ServerMods` exposes the deposit variants the
      prospecting pick uses, so mine-head yields really can be seeded from local geology.
      8–12% floor so nowhere is bricked; scales with tier.
- [ ] **E6 · The craft chain:** charcoal burner → smith → tools, with **tool tier feeding
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

---

## Phase H: Decline, and shipping  *(M6 → v1.0)*

- [ ] **H1 · Decline:** the four-stage slide: strain, regression, attrition, ruin. Recovery
      rolls throughout; failure weighted to tiers 0–1.
- [ ] **H2 · Ruins:** decay as an *absence of maintenance*, structures left standing.
- [ ] **H3 · Orphans:** when a village dies, the survivors don't simply vanish. They set
      out for the nearest living village within range and are adopted into it, bringing
      their trade and their name with them. A settlement that takes in refugees gains
      population it didn't grow, and and the arrivals carry the memory of what happened,
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
