# Foundries & Frontiers

A Vintage Story mod: villages that grow on their own — foraging, farming, forging and
building their way up a tech ladder, whether or not anyone is watching.

Targets **Vintage Story 1.22.7** (`net10.0`).

## Building

The project references Vintage Story's game assemblies, which are not redistributable and
are therefore not committed. Drop these into `refs/` from your own install:

    VintagestoryAPI.dll   VintagestoryLib.dll     (game root)
    VSSurvivalMod.dll     VSEssentials.dll        (game Mods/)
    VSCreativeMod.dll
    Newtonsoft.Json.dll   protobuf-net.dll        (game Lib/)
    0Harmony.dll          cairo-sharp.dll  SkiaSharp.dll

Then:

    ./build.sh

Produces `dist/foundriesfrontiers_<version>.zip`. Drop that in `VintagestoryData/Mods/`.

## Status

Pre-alpha — Phase A. Villagers exist and have personalities; nothing is simulated yet.

Full sequence and current state: **[docs/BUILD_ORDER.md](docs/BUILD_ORDER.md)**

Done so far:

- Toolchain — cloud build, zip packaging, deploy
- Villager entity on the seraph model, randomized appearance, gendered voices and hair
- Cultures as data — Norman and Norse names, spoken lines, nametags
- Ambient social behaviour — greetings and villager conversations
- Dev tooling — state dump, time skip, live debug labels, counters
- AI task framework with staggered thinking and distance culling
- Pathfinding (adapted from VS Village, MIT) and a goto task
- Carrying loads and tools, with tool tier driving work rate

**Phase A is complete.** Next: **Phase B** — the village object, ledger, storehouse and the first producing jobs.

## Commands

All require `controlserver` privilege.

| Command | Does |
|---------|------|
| `/ff status` | Mod and world state |
| `/ff spawn [trade] [culture]` | Spawn a villager in front of you |
| `/ff trades` | List every trade the mod defines |
| `/ff cultures` | List loaded cultures |
| `/ff dump` | Full state of the villager you're looking at |
| `/ff skip <days>` | Advance the world calendar |
| `/ff debug` | Toggle live state labels above every villager |
| `/ff stats [reset]` | Debug counters |
| `/ff goto [gait]` | Send the nearest villager to the block you're looking at |
| `/ff gait <name>` | Drive a villager forward to check a gait |
| `/ff give <item> [n]` | Give a villager a tool or a load to carry |
| `/ff credits` | Attribution, in game |
| `/ff clear [radius\|all]` | Remove test villagers |
| `/ff config [reload]` | Show or re-read the config |

## Configuration

Settings live in `VintagestoryData/ModConfig/foundriesfrontiers.json`, written with
defaults on first run. Edit it and run `/ff config reload` — no restart needed for most
values.

The dial to reach for first on a struggling server is
`Performance.ThinkIntervalMultiplier`: raise it above 1 and villagers think less often and
cost less. `Performance.UnobservedThrottle` controls how much work is skipped when no
player is nearby.

If villagers are too chatty for your taste, lower `Chatter.IdleTalkChance` or set
`Chatter.SpeechInChat` to false. Values baked into the entity JSON — wander speed and the
tether distance — still need a game restart to change.

## Licence

MIT — see [LICENSE](LICENSE). Use it, fork it, borrow from it; just keep the copyright
notice. The same terms this mod's own borrowed pathfinding came under.

## Credits and attribution

This mod does not build everything from scratch, and everything it borrows is listed here.
If you think something is used without proper credit, please open an issue — it will be
fixed.

### Vintage Story — Anego Studios

The game itself, and a great deal of what makes this mod look and sound like it belongs in
it. Foundries & Frontiers **references** these assets by path at runtime and does not
redistribute any of them:

| Used | From |
|------|------|
| The seraph model and its animation set — walk, sprint, idle, sit, lie, hammer, pickaxe, smithing | `game:entity/humanoid/seraph-faceless` |
| Skin parts — skin tones, eyes, hair, beards, facial expressions, underwear | `game:entity/humanoid/seraphskinparts/*` |
| Instrument voices used for villager speech | `game:sounds/voice/*` |
| Clothing worn by villagers | `survival:clothes-*` |
| The skin-part table structure | Adapted from the base game's `playerbot` entity |

The game's assemblies are referenced to compile against and are likewise not redistributed —
see the build instructions above.

### VS Village — G3rste and contributors

The villager pathfinding stack — a custom A\* over a waypoint graph, with door handling —
is **adapted from [VS Village](https://github.com/G3rste/vsvillage)** and used under the
MIT licence.

This is the hardest single piece of the mod and it would have taken weeks to write badly.
The files live in [`src/ThirdParty/Pathfinding/`](src/ThirdParty/Pathfinding/) with the
full licence text in [`src/ThirdParty/LICENSE-vsvillage.txt`](src/ThirdParty/LICENSE-vsvillage.txt),
every modification marked `CHANGED FROM UPSTREAM` in the source:

- `VillagerAStarNew.cs` · `WaypointAStar.cs` · `VillagerPathNode.cs` · `VillagerPathfind.cs`

Changes made: the namespace, replacing VS Village's own village types with an interface
this mod owns, and adding instrumentation. The algorithm is theirs.

VS Village is an excellent mod in its own right and is worth playing.

### Everything else

All other code, design, configuration and content in this repository is original to this
mod.
