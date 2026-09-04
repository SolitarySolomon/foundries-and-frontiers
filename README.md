# Foundries & Frontiers

A Vintage Story mod that adds villages which grow on their own. Villagers forage, farm,
build and eventually forge, working their way up a tech ladder whether or not a player is
around to watch it happen.

[![Support me on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/coreypiazza)

Built against Vintage Story 1.22.7 (`net10.0`).

## Building

The game's assemblies are not redistributable, so they are not in this repo. Copy these
out of your own install into `refs/`:

    VintagestoryAPI.dll   VintagestoryLib.dll     (game root)
    VSSurvivalMod.dll     VSEssentials.dll        (game Mods/)
    VSCreativeMod.dll
    Newtonsoft.Json.dll   protobuf-net.dll        (game Lib/)
    0Harmony.dll          cairo-sharp.dll  SkiaSharp.dll

Then run:

    ./build.sh

That gives you `dist/foundriesfrontiers_<version>.zip`. Drop it in `VintagestoryData/Mods/`.

## Status

Pre-alpha. Phase A is finished, which means villagers exist and act like people, but there
is no village simulation behind them yet.

The full plan and where it currently stands lives in
[docs/BUILD_ORDER.md](docs/BUILD_ORDER.md).

Working so far:

- Build, packaging and deploy
- Villager entity built on the seraph player model, with randomized appearance and voices
  and hair that match the villager's gender
- Cultures kept as data rather than code. Norman and Norse so far, each with their own
  names, spoken lines and nametags
- Greetings and short conversations between villagers as they go about their day
- Dev tooling: state dump, time skip, live debug labels, counters
- An AI task framework that staggers thinking across villagers and skips work when nobody
  is nearby to see it
- Pathfinding adapted from VS Village, and a goto task built on it
- Carrying tools and loads, with tool tier feeding into how fast work gets done

Phase B is next: the village object itself, its ledger, a storehouse, and the first jobs
that actually produce something.

## Commands

All of these need the `controlserver` privilege.

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

Settings live in `VintagestoryData/ModConfig/foundriesfrontiers.json`, written out with
defaults the first time the mod runs. Edit the file and run `/ff config reload`. Most
values take effect right away with no restart.

If a server is struggling, the first setting to reach for is
`Performance.ThinkIntervalMultiplier`. Anything above 1 makes villagers think less often
and cost less. `Performance.UnobservedThrottle` controls how much work gets skipped when
there is no player around.

If the villagers are too chatty for you, turn down `Chatter.IdleTalkChance` or set
`Chatter.SpeechInChat` to false.

A few things are baked into the entity JSON instead of the config, like wander speed and
the tether distance. Those still need a game restart.

## Support

This mod is free and it stays free. There is no paid version, nothing held back and no
perks for paying. If you want to help anyway, Ko-fi is the place.

What support actually buys is hours. A mod like this is not a weekend project, it is a
long list of systems that each take real time to build and test. Anything that comes in
is time I can put into this instead of something else, which means features land sooner
and bugs get fixed faster. That is the whole pitch.

[![Support me on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/coreypiazza)

## License

MIT, see [LICENSE](LICENSE). Use it, fork it, take pieces out of it. Just keep the
copyright notice. It's the same license the borrowed pathfinding came under.

## A note on AI

Parts of this mod are written with AI assistance. I use Claude to write and refactor code
so development moves faster than I could manage on my own with the time I have. The
design, the direction, the testing and the decision about what actually ships are mine.

I am saying so plainly because you deserve to know what went into something before you
install it, and because I would rather tell you than have you find out later and wonder
what else I left out.

## Credits and attribution

Not everything in here was written from scratch, and anything borrowed is listed below.
If you think something has been used without proper credit, open an issue and it will
get fixed.

### Vintage Story, by Anego Studios

The game itself, plus most of what makes these villagers look and sound like they belong
in it. The mod points at these assets by path at runtime and does not ship copies of any
of them:

| Used | From |
|------|------|
| The seraph model and its animations: walk, sprint, idle, sit, lie, hammer, pickaxe, smithing | `game:entity/humanoid/seraph-faceless` |
| Skin parts: skin tones, eyes, hair, beards, facial expressions, underwear | `game:entity/humanoid/seraphskinparts/*` |
| The instrument voices villagers speak with | `game:sounds/voice/*` |
| Clothing the villagers wear | `survival:clothes-*` |
| The structure of the skin part table | Adapted from the base game's `playerbot` entity |

The game's assemblies are referenced at compile time and are not redistributed either.
See the build steps above.

### VS Village, by G3rste and contributors

The villager pathfinding is adapted from
[VS Village](https://github.com/G3rste/vsvillage) and used under the MIT license. It's a
custom A\* over a waypoint graph with door handling, and it's the hardest single piece of
this mod. Writing it from scratch would have taken weeks and the result would have been
worse.

Those files live in [`src/ThirdParty/Pathfinding/`](src/ThirdParty/Pathfinding/), with the
full license text in
[`src/ThirdParty/LICENSE-vsvillage.txt`](src/ThirdParty/LICENSE-vsvillage.txt). Every
change is marked `CHANGED FROM UPSTREAM` in the source:

- `VillagerAStarNew.cs`, `WaypointAStar.cs`, `VillagerPathNode.cs`, `VillagerPathfind.cs`

What changed: the namespace, swapping VS Village's own village types for an interface this
mod owns, and some added instrumentation. The algorithm is theirs.

VS Village is a good mod in its own right and worth playing.

### Everything else

The rest of the code, design, configuration and content in this repo is original to this
mod.
