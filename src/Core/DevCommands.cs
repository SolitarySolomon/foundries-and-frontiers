using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Developer commands. These exist to make the test loop fast, which is the single
    /// biggest factor in whether this project ever gets finished. Everything here is
    /// gated behind controlserver privilege and is expected to survive into release
    /// as debug tooling rather than being stripped.
    /// </summary>
    public static class DevCommands
    {
        public static void Register(ICoreServerAPI sapi, FoundriesFrontiersMod mod)
        {
            sapi.ChatCommands
                .Create("ff")
                .WithDescription("Foundries & Frontiers developer commands")
                .RequiresPrivilege(Privilege.controlserver)

                .BeginSubCommand("status")
                    .WithDescription("Report mod status")
                    .HandleWith(args => OnStatus(sapi, mod, args))
                .EndSubCommand()

                .BeginSubCommand("spawn")
                    .WithDescription("Spawn a villager. /ff spawn [trade] [culture], e.g. /ff spawn farmer norse")
                    .RequiresPlayer()
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("trade"),
                              sapi.ChatCommands.Parsers.OptionalWord("culture"))
                    .HandleWith(args => OnSpawn(sapi, mod, args))
                .EndSubCommand()

                .BeginSubCommand("dump")
                    .WithDescription("Dump the full state of the villager you are looking at (or the nearest one)")
                    .RequiresPlayer()
                    .HandleWith(args => OnDump(sapi, args))
                .EndSubCommand()

                .BeginSubCommand("skip")
                    .WithDescription("Advance the calendar. /ff skip <days>")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalFloat("days", 1))
                    .HandleWith(args => OnSkip(sapi, mod, args))
                .EndSubCommand()

                .BeginSubCommand("debug")
                    .WithDescription("Toggle live state labels above every villager")
                    .HandleWith(args => OnDebugToggle(sapi, mod))
                .EndSubCommand()

                .BeginSubCommand("goto")
                    .WithDescription("Send the nearest villager to the block you are looking at. /ff goto [walk|run|laden]")
                    .RequiresPlayer()
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("gait"))
                    .HandleWith(args => OnGoto(sapi, args))
                .EndSubCommand()

                .BeginSubCommand("give")
                    .WithDescription("Give the nearest villager a tool or a load. /ff give <itemcode> [count]")
                    .RequiresPlayer()
                    .WithArgs(sapi.ChatCommands.Parsers.Word("item"),
                              sapi.ChatCommands.Parsers.OptionalInt("count", 1))
                    .HandleWith(args => OnGive(sapi, args))
                .EndSubCommand()

                .BeginSubCommand("gait")
                    .WithDescription("Push the nearest villager forward to check a gait. /ff gait walk|run|stroll|laden")
                    .RequiresPlayer()
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("gait"))
                    .HandleWith(args => OnGait(sapi, args))
                .EndSubCommand()

                .BeginSubCommand("stats")
                    .WithDescription("Show the debug counters. /ff stats reset to clear")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("reset"))
                    .HandleWith(args => OnStats(args))
                .EndSubCommand()

                .BeginSubCommand("cultures")
                    .WithDescription("List loaded cultures")
                    .HandleWith(args => OnCultures(sapi))
                .EndSubCommand()

                .BeginSubCommand("clear")
                    .WithDescription("Remove villagers. /ff clear [radius], default 64, 'all' for everything loaded")
                    .RequiresPlayer()
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("radius"))
                    .HandleWith(args => OnClear(sapi, mod, args))
                .EndSubCommand()

                .BeginSubCommand("config")
                    .WithDescription("Show the live config, or /ff config reload to re-read the file")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("action"))
                    .HandleWith(args => OnConfig(sapi, args))
                .EndSubCommand()

                .BeginSubCommand("credits")
                    .WithDescription("Who made the parts of this mod that were not made here")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(args => OnCredits())
                .EndSubCommand()

                .BeginSubCommand("trades")
                    .WithDescription("List every trade the mod knows about")
                    .HandleWith(args => OnTrades())
                .EndSubCommand();
        }

        private static TextCommandResult OnStatus(ICoreServerAPI sapi, FoundriesFrontiersMod mod, TextCommandCallingArgs args)
        {
            int villagers = 0;
            foreach (var e in sapi.World.LoadedEntities.Values)
            {
                if (e is FFVillager) villagers++;
            }

            string msg =
                "Foundries & Frontiers v" + mod.Mod.Info.Version + "\n" +
                "Game: " + Vintagestory.API.Config.GameVersion.ShortGameVersion + "\n" +
                "World seed: " + sapi.World.Seed + "\n" +
                "Villagers loaded: " + villagers + "\n" +
                "Villages: 0 (not implemented)\n" +
                "Simulation: offline";

            return TextCommandResult.Success(msg);
        }

        private static TextCommandResult OnSpawn(ICoreServerAPI sapi, FoundriesFrontiersMod mod, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("No player entity.");

            string tradeArg = args[0] as string;
            string trade = string.IsNullOrEmpty(tradeArg) ? "forager" : tradeArg.ToLowerInvariant();

            // The entity variants declared in villager.json. Anything else won't resolve.
            string[] known = { "forager", "builder", "lumberjack", "farmer", "herder" };
            if (!known.Contains(trade))
            {
                return TextCommandResult.Error(
                    "Unknown trade '" + trade + "'. Spawnable right now: " + string.Join(", ", known) +
                    ". (/ff trades lists everything the mod plans to support.)");
            }

            CultureSystem cultures = sapi.ModLoader.GetModSystem<CultureSystem>();
            string cultureArg = args[1] as string;
            string culture;
            if (string.IsNullOrEmpty(cultureArg))
            {
                // Roll across everything loaded rather than always defaulting to one,
                // so spawning a crowd for testing produces a mixed crowd.
                var all = new List<string>(cultures.Codes);
                culture = all.Count > 0
                    ? all[sapi.World.Rand.Next(all.Count)]
                    : CultureSystem.DefaultCulture;
            }
            else
            {
                culture = cultureArg.ToLowerInvariant();
            }

            if (!cultures.Has(culture))
            {
                return TextCommandResult.Error(
                    "Unknown culture '" + culture + "'. Loaded: " + string.Join(", ", cultures.Codes));
            }

            string gender = sapi.World.Rand.NextDouble() < 0.5 ? "male" : "female";
            AssetLocation code = new AssetLocation(FoundriesFrontiersMod.ModId, "villager-" + gender + "-" + trade);

            EntityProperties type = sapi.World.GetEntityType(code);
            if (type == null)
            {
                mod.Warn("Entity type {0} did not resolve. Is the entity JSON loading?", code);
                return TextCommandResult.Error("Entity type " + code + " not found. Check the log.");
            }

            Entity entity = sapi.World.ClassRegistry.CreateEntity(type);
            if (entity == null) return TextCommandResult.Error("Class registry returned null for " + code);

            // Two blocks in front of the player, on the ground.
            Vec3d pos = player.Entity.Pos.XYZ
                .AheadCopy(2.0, 0, player.Entity.Pos.Yaw)
                .Add(0, 0.5, 0);

            entity.Pos.SetPosWithDimension(pos);
            entity.Pos.Yaw = player.Entity.Pos.Yaw + GameMath.PI;
            entity.PositionBeforeFalling.Set(entity.Pos.XYZ);

            // Culture must be set before SpawnEntity, because Initialize picks the name from it.
            if (entity is FFVillager v) v.CultureCode = culture;

            sapi.World.SpawnEntity(entity);
            DevStats.Bump(DevStats.VillagersSpawned);

            mod.Log("Spawned {0} (id {1}) at {2} for {3}",
                code.Path, entity.EntityId, pos.AsBlockPos, player.PlayerName);

            string name = (entity as FFVillager)?.GivenName ?? "?";
            return TextCommandResult.Success(
                "Spawned " + name + " - " + culture + " " + gender + " " + trade +
                " (entity " + entity.EntityId + ").");
        }

        /// <summary>Finds the villager the player is looking at, else the nearest within 20 blocks.</summary>
        private static FFVillager FindTarget(ICoreServerAPI sapi, IServerPlayer player)
        {
            if (player?.Entity == null) return null;

            if (player.CurrentEntitySelection?.Entity is FFVillager looked) return looked;

            Entity nearest = sapi.World.GetNearestEntity(player.Entity.Pos.XYZ, 20, 20,
                e => e is FFVillager && e.Alive);
            return nearest as FFVillager;
        }

        private static TextCommandResult OnDump(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            FFVillager v = FindTarget(sapi, args.Caller.Player as IServerPlayer);
            if (v == null) return TextCommandResult.Error("No villager in sight or within 20 blocks.");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- " + (v.GivenName != "" ? v.GivenName : "unnamed") + " (entity " + v.EntityId + ") ---");
            sb.AppendLine("Trade      " + v.Trade);
            sb.AppendLine("Culture    " + v.CultureCode);
            sb.AppendLine("Gender     " + (v.IsFemale ? "female" : "male"));
            sb.AppendLine("Village    " + (v.VillageId == "" ? "(none)" : v.VillageId));
            sb.AppendLine("Position   " + v.Pos.AsBlockPos);
            sb.AppendLine("Health     " + v.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth") + " / "
                                        + v.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("maxhealth"));
            sb.AppendLine("Voice      " + v.WatchedAttributes.GetString("voicetype", "?")
                                        + " @ " + v.WatchedAttributes.GetString("voicepitch", "?"));
            sb.AppendLine("Activity   " + v.DebugState());
            sb.AppendLine("Goto       " + (v.GotoTarget?.ToString() ?? "(none)") + "  last: " + v.LastGotoResult);
            sb.AppendLine("Carrying   " + (v.IsCarrying ? v.CarriedCount + "x " + v.CarriedStack.GetName() : "(nothing)"));
            sb.AppendLine("Tool       " + (v.ToolStack?.GetName() ?? "(none)")
                          + "  tier " + v.ToolTier + "  work rate x" + v.WorkRate.ToString("0.00"));

            // Print what the skin system actually applied, rather than what we asked for.
            // "Norse villagers look wrong" is a hypothesis; this turns it into data.
            var skin = v.GetBehavior<Vintagestory.GameContent.EntityBehaviorExtraSkinnable>();
            if (skin != null)
            {
                sb.AppendLine();
                sb.AppendLine("Applied skin parts (" + skin.AppliedSkinParts.Count + "):");
                foreach (var part in skin.AppliedSkinParts)
                {
                    sb.AppendLine("  " + part.PartCode.PadRight(18) + part.Code);
                }
            }
            else
            {
                sb.AppendLine("Applied skin parts: NONE - extraskinnable behaviour missing!");
            }

            sb.AppendLine();
            sb.AppendLine("World day  " + (int)sapi.World.Calendar.TotalDays
                          + "  hour " + sapi.World.Calendar.HourOfDay.ToString("0.0")
                          + "  season " + sapi.World.Calendar.GetSeason(v.Pos.AsBlockPos));
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        private static TextCommandResult OnSkip(ICoreServerAPI sapi, FoundriesFrontiersMod mod, TextCommandCallingArgs args)
        {
            float days = (float)(args[0] is float f ? f : 1f);
            if (days <= 0 || days > 365) return TextCommandResult.Error("Give a value between 0 and 365 days.");

            double before = sapi.World.Calendar.TotalDays;
            sapi.World.Calendar.Add(days * sapi.World.Calendar.HoursPerDay);
            double after = sapi.World.Calendar.TotalDays;

            mod.Log("Calendar advanced {0} days ({1:0} -> {2:0})", days, before, after);

            return TextCommandResult.Success(
                "Advanced " + days + " day(s). Now day " + (int)after + ".\n" +
                "Note: this moves the world clock, so weather, crops and hunger jump too.");
        }

        private static TextCommandResult OnDebugToggle(ICoreServerAPI sapi, FoundriesFrontiersMod mod)
        {
            mod.DebugLabels = !mod.DebugLabels;

            // Push every loaded villager back to its normal tag when switching off.
            foreach (var e in sapi.World.LoadedEntities.Values)
            {
                (e as FFVillager)?.RefreshNameTagPublic();
            }

            return TextCommandResult.Success(
                "Debug labels " + (mod.DebugLabels ? "ON, villagers now show live state" : "OFF"));
        }

        /// <summary>
        /// Drives a villager forward at a chosen speed for a few seconds. Exists purely to
        /// check that a gait and its animation agree - the walk animation is
        /// mulWithWalkSpeed, so a speed that is wrong looks like sliding rather than an
        /// obviously wrong number.
        /// </summary>
        private static TextCommandResult OnGait(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            FFVillager v = FindTarget(sapi, args.Caller.Player as IServerPlayer);
            if (v == null) return TextCommandResult.Error("No villager in sight or within 20 blocks.");

            string gait = (args[0] as string ?? "walk").ToLowerInvariant();
            float speed;
            switch (gait)
            {
                case "stroll": speed = MoveSpeeds.Stroll; break;
                case "walk":   speed = MoveSpeeds.Walk;   break;
                case "laden":  speed = MoveSpeeds.Laden;  break;
                case "run":    speed = MoveSpeeds.Run;    break;
                default: return TextCommandResult.Error("Gait must be stroll, walk, laden or run.");
            }

            v.DebugDrive(speed, 4f);

            return TextCommandResult.Success(
                "Driving " + (v.GivenName == "" ? "villager" : v.GivenName) + " at " + gait +
                " (" + speed + ", animation '" + MoveSpeeds.AnimationFor(speed) + "') for 4s.");
        }

        /// <summary>
        /// The acceptance test for pathfinding: pick a villager, look at a block, and see
        /// whether they get there. Put an obstacle in the way and see whether they go round.
        /// </summary>
        private static TextCommandResult OnGoto(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            FFVillager v = FindTarget(sapi, player);
            if (v == null) return TextCommandResult.Error("No villager in sight or within 20 blocks.");

            BlockSelection sel = player?.CurrentBlockSelection;
            if (sel?.Position == null)
            {
                return TextCommandResult.Error("Look at a block to set the destination.");
            }

            string gait = (args[0] as string ?? "walk").ToLowerInvariant();
            float speed;
            switch (gait)
            {
                case "stroll": speed = MoveSpeeds.Stroll; break;
                case "laden":  speed = MoveSpeeds.Laden;  break;
                case "run":    speed = MoveSpeeds.Run;    break;
                default:       speed = MoveSpeeds.Walk;   break;
            }

            BlockPos target = sel.Position.UpCopy();
            v.OrderGoto(target, speed);

            double dist = System.Math.Sqrt(v.Pos.SquareDistanceTo(target.ToVec3d()));
            return TextCommandResult.Success(
                (v.GivenName == "" ? "Villager" : v.GivenName) + " ordered to " + target +
                " (" + dist.ToString("0") + " blocks, " + gait + "). Watch /ff debug for progress.");
        }

        /// <summary>
        /// Hands a villager an item so tool tiers and carrying can be checked before any
        /// job exists to produce them. A tool goes to the working hand, anything else
        /// becomes a carried load.
        /// </summary>
        private static TextCommandResult OnGive(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            FFVillager v = FindTarget(sapi, args.Caller.Player as IServerPlayer);
            if (v == null) return TextCommandResult.Error("No villager in sight or within 20 blocks.");

            string code = args[0] as string;
            int count = args[1] is int c ? c : 1;

            var loc = new AssetLocation(code.Contains(":") ? code : "game:" + code);
            Item item = sapi.World.GetItem(loc);
            if (item == null)
            {
                Block block = sapi.World.GetBlock(loc);
                if (block == null)
                {
                    // Vintage Story codes are usually "thing-material", so "copperpickaxe"
                    // never resolves when the real code is "pickaxe-copper". Rather than
                    // just failing, suggest what does exist.
                    return TextCommandResult.Error(
                        "No item or block called '" + loc + "'.\n" + SuggestCodes(sapi, code));
                }
                var bstack = new ItemStack(block, count);
                int tookB = v.TryCarry(bstack);
                return TextCommandResult.Success("Carrying " + tookB + "x " + bstack.GetName() + ".");
            }

            var stack = new ItemStack(item, count);

            if (VillagerCarry.ToolTierOf(stack) > 0 || item.Tool != null)
            {
                v.GiveTool(stack);
                return TextCommandResult.Success(
                    "Gave " + stack.GetName() + " - tool tier " + v.ToolTier +
                    ", work rate x" + v.WorkRate.ToString("0.00") + ".");
            }

            int took = v.TryCarry(stack);
            if (took == 0) return TextCommandResult.Error("Hands full, or holding something else.");
            return TextCommandResult.Success(
                "Carrying " + v.CarriedCount + "x " + stack.GetName() +
                " (capacity " + VillagerCarry.CarryCapacity + ").");
        }

        /// <summary>
        /// Finds real item codes containing any word-ish fragment of what was typed.
        /// Turns a dead end into a usable answer.
        /// </summary>
        private static string SuggestCodes(ICoreServerAPI sapi, string typed)
        {
            string needle = typed.ToLowerInvariant().Replace(":", "").Replace("-", "");
            var hits = new List<string>();

            foreach (Item it in sapi.World.Items)
            {
                string path = it?.Code?.Path;
                if (path == null) continue;

                string flat = path.Replace("-", "");
                // Match either direction: "pickaxecopper" contains "pickaxe", and a typed
                // "copperpickaxe" shares its parts with "pickaxe-copper".
                bool match = flat.Contains(needle) || needle.Contains(flat);
                if (!match && needle.Length >= 4)
                {
                    foreach (string part in path.Split('-'))
                    {
                        if (part.Length >= 4 && needle.Contains(part)) { match = true; break; }
                    }
                }
                if (match && !hits.Contains(path)) hits.Add(path);
                if (hits.Count >= 12) break;
            }

            if (hits.Count == 0) return "No similar codes found.";
            hits.Sort(StringComparer.Ordinal);
            return "Did you mean: " + string.Join(", ", hits);
        }

        /// <summary>
        /// Removes test villagers. They accumulate across a testing session and there was
        /// no way to get rid of them short of hunting each one down.
        /// </summary>
        private static TextCommandResult OnClear(ICoreServerAPI sapi, FoundriesFrontiersMod mod, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("No player entity.");

            string arg = (args[0] as string ?? "64").ToLowerInvariant();
            bool all = arg == "all";
            float radius = 64;
            if (!all && !float.TryParse(arg, out radius))
            {
                return TextCommandResult.Error("Give a radius in blocks, or 'all'.");
            }

            var doomed = new List<Entity>();
            foreach (var e in sapi.World.LoadedEntities.Values)
            {
                if (!(e is FFVillager)) continue;
                if (!all && e.Pos.SquareDistanceTo(player.Entity.Pos.XYZ) > radius * radius) continue;
                doomed.Add(e);
            }

            foreach (Entity e in doomed) e.Die(EnumDespawnReason.Removed);

            mod.Log("Cleared {0} villagers ({1})", doomed.Count, all ? "all loaded" : radius + " blocks");
            return TextCommandResult.Success(
                "Removed " + doomed.Count + " villager(s) " + (all ? "(all loaded)." : "within " + radius + " blocks."));
        }

        private static TextCommandResult OnConfig(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            if ((args[0] as string) == "reload")
            {
                FFConfig.Load(sapi);
                return TextCommandResult.Success(
                    "Config reloaded from ModConfig/" + FFConfig.FileName + ".\n" +
                    "Note: values baked into entity JSON (wander speed, tether distance) still need a restart.");
            }

            FFConfig c = FFConfig.Current;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ModConfig/" + FFConfig.FileName);
            sb.AppendLine("Movement    stroll " + c.Movement.Stroll + "  walk " + c.Movement.Walk
                          + "  laden " + c.Movement.Laden + "  run " + c.Movement.Run);
            sb.AppendLine("Chatter     idle " + c.Chatter.IdleTalkChance + "  volume " + c.Chatter.VoiceVolume
                          + "  chat cd " + c.Chatter.ChatCooldownSec + "s  in chat: " + c.Chatter.SpeechInChat);
            sb.AppendLine("Villager    carry " + c.Villager.CarryCapacity
                          + "  tool rates [" + string.Join(", ", c.Villager.WorkRateByToolTier) + "]");
            sb.AppendLine("Performance think x" + c.Performance.ThinkIntervalMultiplier
                          + "  unobserved x" + c.Performance.UnobservedThrottle
                          + "  range " + c.Performance.ObservedRangeBlocks);
            sb.Append("Edit the file and run /ff config reload.");
            return TextCommandResult.Success(sb.ToString());
        }

        private static TextCommandResult OnStats(TextCommandCallingArgs args)
        {
            if ((args[0] as string) == "reset")
            {
                DevStats.Reset();
                return TextCommandResult.Success("Counters reset.");
            }
            return TextCommandResult.Success(DevStats.Report());
        }

        private static TextCommandResult OnCultures(ICoreServerAPI sapi)
        {
            CultureSystem cultures = sapi.ModLoader.GetModSystem<CultureSystem>();
            if (cultures.Codes.Count == 0) return TextCommandResult.Error("No cultures loaded - check the log.");

            var sb = new System.Text.StringBuilder("Loaded cultures:\n");
            foreach (string code in cultures.Codes)
            {
                Culture c = cultures.Get(code);
                sb.AppendLine("  " + code + " (" + c.DisplayName + ") - "
                    + (c.MaleNames.Length + c.FemaleNames.Length) + " names, "
                    + c.Lines.Count + " line sets");
            }
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        /// <summary>
        /// Attribution, in game, where players actually are. The README is the full
        /// version; this is the part nobody should have to go looking for.
        /// </summary>
        private static TextCommandResult OnCredits()
        {
            return TextCommandResult.Success(
                "Foundries & Frontiers - credits\n" +
                "\n" +
                "Villager pathfinding is adapted from VS Village by G3rste and\n" +
                "contributors, used under the MIT license. The A* and waypoint graph\n" +
                "are their work. github.com/G3rste/vsvillage\n" +
                "\n" +
                "Villager models, animations, skin parts, voices and clothing are\n" +
                "Vintage Story's own assets by Anego Studios, referenced rather than\n" +
                "redistributed.\n" +
                "\n" +
                "Everything else is original to this mod. Full details in the README.\n" +
                "\n" +
                "Parts of this mod are written with AI assistance. Design, testing\n" +
                "and what ships are the author's own.\n" +
                "\n" +
                "This mod is free. If you want to support the work:\n" +
                "ko-fi.com/coreypiazza");
        }

        private static TextCommandResult OnTrades()
        {
            var names = Enum.GetNames<EnumTrade>();
            return TextCommandResult.Success(
                names.Length + " trades defined:\n" + string.Join(", ", names) +
                "\n\nSpawnable so far: forager, builder, lumberjack, farmer, herder.");
        }
    }
}
