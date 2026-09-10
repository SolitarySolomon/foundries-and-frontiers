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

                .BeginSubCommand("buildings")
                    .WithDescription("List the building schematics that loaded, with their two costs")
                    .HandleWith(args => OnBuildings(sapi, args))
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

                .BeginSubCommand("village")
                    .WithDescription("Villages. /ff village create|list|info|remove|join|leave")
                    .BeginSubCommand("create")
                        .WithDescription("Found a village where you stand. /ff village create [culture] [name]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("culture"),
                                  sapi.ChatCommands.Parsers.OptionalAll("name"))
                        .HandleWith(args => OnVillageCreate(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("list")
                        .WithDescription("List every village in the world")
                        .HandleWith(args => OnVillageList(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("info")
                        .WithDescription("Details of a village. /ff village info [id], defaults to the one you are standing in")
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageInfo(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("remove")
                        .WithDescription("Delete a village. /ff village remove [id|all], defaults to the one you are in")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("id"))
                        .HandleWith(args => OnVillageRemove(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("plot")
                        .WithDescription("Village plots. /ff village plot list|add <kind>|site <kind>|remove <id>|show [off]|clear")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("action"),
                                  sapi.ChatCommands.Parsers.OptionalWord("what"))
                        .HandleWith(args => OnVillagePlot(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("build")
                        .WithDescription("Village building. /ff village build list|next|<code>|cancel <id>")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("action"),
                                  sapi.ChatCommands.Parsers.OptionalWord("what"))
                        .HandleWith(args => OnVillageBuild(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("show")
                        .WithDescription("Outline a village claim. /ff village show [id|off]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("id"))
                        .HandleWith(args => OnVillageShow(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("adopt")
                        .WithDescription("Take every unaffiliated villager inside the claim into the village. /ff village adopt [id]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageAdopt(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("ledger")
                        .WithDescription("Show a village's stores and measured daily flow. /ff village ledger [id]")
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageLedger(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("give")
                        .WithDescription("Deposit into the ledger, counted as flow. /ff village give <resource> <amount>")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.Word("resource"),
                                  sapi.ChatCommands.Parsers.OptionalFloat("amount", 10))
                        .HandleWith(args => OnVillageGive(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("take")
                        .WithDescription("Withdraw from the ledger, counted as flow. /ff village take <resource> <amount>")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.Word("resource"),
                                  sapi.ChatCommands.Parsers.OptionalFloat("amount", 10))
                        .HandleWith(args => OnVillageTake(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("set")
                        .WithDescription("Set a pool outright, no flow recorded. /ff village set <resource> <amount>")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.Word("resource"),
                                  sapi.ChatCommands.Parsers.OptionalFloat("amount", 0))
                        .HandleWith(args => OnVillageSet(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("deposit")
                        .WithDescription("Make the villager you are looking at hand in what they carry")
                        .RequiresPlayer()
                        .HandleWith(args => OnVillageDeposit(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("day")
                        .WithDescription("Close the day by hand so the flow figures update. /ff village day [count]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("count", 1))
                        .HandleWith(args => OnVillageDay(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("table")
                        .WithDescription("Show how items are sorted into the six pools")
                        .HandleWith(args => OnVillageTable(sapi))
                    .EndSubCommand()
                    .BeginSubCommand("tier")
                        .WithDescription("Set a village's tier and watch its marker change. /ff village tier <0-5> [id]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.Int("tier"),
                                  sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageTier(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("abandon")
                        .WithDescription("Kill a village and leave its ruins behind. /ff village abandon [id]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageAbandon(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("scan")
                        .WithDescription("Re-scan the claim for beds and workstations. /ff village scan [id]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageScan(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("beds")
                        .WithDescription("Who has a bed and who does not. /ff village beds [id]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageBeds(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("storehouse")
                        .WithDescription("Put the storehouse crate back if it was broken. /ff village storehouse [id]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageStorehouse(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("mark")
                        .WithDescription("Put the centre cairn back if it was broken. /ff village mark [id]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageMark(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("join")
                        .WithDescription("Put the villager you are looking at into a village. /ff village join [id]")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("id", 0))
                        .HandleWith(args => OnVillageJoin(sapi, args))
                    .EndSubCommand()
                    .BeginSubCommand("leave")
                        .WithDescription("Remove the villager you are looking at from their village")
                        .RequiresPlayer()
                        .HandleWith(args => OnVillageLeave(sapi, args))
                    .EndSubCommand()
                .EndSubCommand()

                .BeginSubCommand("time")
                    .WithDescription("Set the hour of day, to watch the schedule change. /ff time <0-24>")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalFloat("hour", 12))
                    .HandleWith(args => OnTime(sapi, args))
                .EndSubCommand()

                .BeginSubCommand("credits")
                    .WithDescription("Who made the parts of this mod that were not made here")
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(args => OnCredits())
                .EndSubCommand()

                .BeginSubCommand("trades")
                    .WithDescription("List every trade the mod knows about")
                    .HandleWith(args => OnTrades(sapi))
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
                "Villages: " + sapi.ModLoader.GetModSystem<VillageRegistry>().Count + "\n" +
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

            // Where the villager is about to appear, which decides whose village they
            // are born into and therefore what culture they are.
            Vec3d spawnAt = player.Entity.Pos.XYZ
                .AheadCopy(2.0, 0, player.Entity.Pos.Yaw)
                .Add(0, 0.5, 0);
            Village bornInto = Registry(sapi).VillageAt(spawnAt.AsBlockPos);

            if (!string.IsNullOrEmpty(cultureArg))
            {
                culture = cultureArg.ToLowerInvariant();
            }
            else if (bornInto != null)
            {
                // People born in a Norse village are Norse. Rolling the dice here would
                // hand a settlement villagers who do not share its names or its clothes.
                culture = bornInto.CultureCode;
            }
            else
            {
                // Outside any claim there is nothing to inherit, so roll across everything
                // loaded and a test crowd comes out mixed.
                var all = new List<string>(cultures.Codes);
                culture = all.Count > 0
                    ? all[sapi.World.Rand.Next(all.Count)]
                    : CultureSystem.DefaultCulture;
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

            Vec3d pos = spawnAt;
            entity.Pos.SetPosWithDimension(pos);
            entity.Pos.Yaw = player.Entity.Pos.Yaw + GameMath.PI;
            entity.PositionBeforeFalling.Set(entity.Pos.XYZ);

            // Culture must be set before SpawnEntity, because Initialize picks the name from it.
            if (entity is FFVillager v) v.CultureCode = culture;

            sapi.World.SpawnEntity(entity);
            DevStats.Bump(DevStats.VillagersSpawned);

            // If they spawned inside a claim, they belong to that village. Saves typing
            // /ff village join after every spawn, and it is what worldgen will do anyway.
            string joined = "";
            if (entity is FFVillager fresh && bornInto != null)
            {
                Registry(sapi).Join(fresh, bornInto);
                joined = " Joined " + bornInto.Name + ".";
            }

            mod.Log("Spawned {0} (id {1}) at {2} for {3}",
                code.Path, entity.EntityId, pos.AsBlockPos, player.PlayerName);

            string name = (entity as FFVillager)?.GivenName ?? "?";
            return TextCommandResult.Success(
                "Spawned " + name + " - " + culture + " " + gender + " " + trade +
                " (entity " + entity.EntityId + ")." + joined);
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
            sb.AppendLine("Manner     " + (v.PersonalityCode != "" ? v.PersonalityCode : "(none)")
                          + ", " + v.Courage.ToString().ToLowerInvariant()
                          + (PersonalitySystem.AlwaysBold(v.Trade) ? " (fixed, this trade always fights)" : "")
                          + ", talks x" + v.TalkFrequency.ToString("0.00"));
            sb.AppendLine("Threat     " + (v.IsThreatened
                              ? (v.Threat?.GetName() ?? "something") + ", " + v.ThreatAgeSec.ToString("0.#") + "s ago"
                              : "(calm)"));
            sb.AppendLine("Village    " + VillageLabel(sapi, v));
            sb.AppendLine("Bed        " + BedLabel(sapi, v));
            sb.AppendLine("Plot       " + PlotLabel(sapi, v));
            sb.AppendLine("Schedule   " + VillageSchedule.Describe(
                              VillageSchedule.PhaseFor(sapi, v.Pos.AsBlockPos))
                          + "  (hour " + sapi.World.Calendar.HourOfDay.ToString("0.0") + ")");
            sb.AppendLine("Position   " + v.Pos.AsBlockPos);
            sb.AppendLine("Health     " + v.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth") + " / "
                                        + v.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("maxhealth"));
            sb.AppendLine("Voice      " + v.WatchedAttributes.GetString("voicetype", "?")
                                        + " @ " + v.WatchedAttributes.GetString("voicepitch", "?"));
            sb.AppendLine("Activity   " + v.DebugState());
            sb.AppendLine("Goto       " + (v.GotoTarget?.ToString() ?? "(none)") + "  last: " + v.LastGotoResult);
            sb.AppendLine("Carrying   " + (v.IsCarrying ? v.CarriedCount + "x " + v.CarriedStack.GetName() : "(nothing)"));
            if (v.IsCarrying)
            {
                var table = sapi.ModLoader.GetModSystem<ResourceTable>();
                EnumVillageResource? pool = table?.Classify(v.CarriedStack);
                sb.AppendLine("  worth    " + (pool == null
                    ? "nothing to a village, no pool matches it"
                    : table.ValueOf(v.CarriedStack).ToString("0.#") + " "
                      + pool.Value.ToString().ToLowerInvariant()));
            }
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
                (e as FFVillager)?.RefreshNameTag();
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

        // --- villages --------------------------------------------------------------

        private static VillageRegistry Registry(ICoreServerAPI sapi)
            => sapi.ModLoader.GetModSystem<VillageRegistry>();

        /// <summary>Village column for /ff dump, so a villager's affiliation is readable.</summary>
        private static string VillageLabel(ICoreServerAPI sapi, FFVillager v)
        {
            if (v.VillageId == 0) return "(none)";
            Village village = Registry(sapi)?.Get(v.VillageId);
            return village == null
                ? "#" + v.VillageId + " (missing, this villager is an orphan)"
                : village.Name + " #" + village.Id;
        }

        /// <summary>Where a villager sleeps, for /ff dump.</summary>
        private static string BedLabel(ICoreServerAPI sapi, FFVillager v)
        {
            if (v.VillageId == 0) return "(no village, so no bed)";

            Village village = Registry(sapi)?.Get(v.VillageId);
            if (village == null) return "(village missing)";

            VillageFacility bed = VillageRegistry.BedOf(village, v.EntityId);
            if (bed != null) return bed.Pos.ToString();

            int free = VillageRegistry.FreeCountOf(village, EnumFacilityKind.Bed);
            return free > 0
                ? "none yet, " + free + " free in the village"
                : "none, and the village has no spare";
        }

        /// <summary>Which plot a villager is assigned to, for /ff dump.</summary>
        private static string PlotLabel(ICoreServerAPI sapi, FFVillager v)
        {
            if (v.VillageId == 0) return "(no village, so no plot)";

            var reg = Registry(sapi);
            Village village = reg?.Get(v.VillageId);
            if (village == null) return "(village missing)";

            VillagePlot plot = reg.PlotOf(village, v.EntityId);
            if (plot != null) return plot.ToString();

            return village.Plots.Count == 0
                ? "none, and the village has sited none at all"
                : "none, though the village has " + village.Plots.Count;
        }

        private static TextCommandResult OnVillageCreate(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("No player entity.");

            var cultures = sapi.ModLoader.GetModSystem<CultureSystem>();
            string culture = args[0] as string;
            if (string.IsNullOrEmpty(culture))
            {
                var all = new List<string>(cultures.Codes);
                culture = all.Count > 0 ? all[sapi.World.Rand.Next(all.Count)] : CultureSystem.DefaultCulture;
            }
            else
            {
                culture = culture.ToLowerInvariant();
                if (!cultures.Has(culture))
                {
                    return TextCommandResult.Error(
                        "Unknown culture '" + culture + "'. Loaded: " + string.Join(", ", cultures.Codes));
                }
            }

            BlockPos centre = player.Entity.Pos.AsBlockPos;
            Village village = Registry(sapi).Create(centre, culture, args[1] as string, out string error);
            if (village == null) return TextCommandResult.Error(error ?? "Could not found a village here.");

            return TextCommandResult.Success(
                "Founded " + village.Name + " (#" + village.Id + "), " + culture +
                ", tier 0, claim " + village.ClaimRadius + " blocks from " + centre + ".");
        }

        private static TextCommandResult OnVillageList(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            var reg = Registry(sapi);
            if (reg.Count == 0) return TextCommandResult.Success("No villages yet. /ff village create founds one.");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(reg.Count + " village(s):");
            foreach (Village v in reg.All)
            {
                sb.AppendLine("  #" + v.Id + "  " + v.Name.PadRight(16)
                    + "t" + v.Tier
                    + "  " + v.CultureCode.PadRight(8)
                    + "pop " + v.MemberIds.Count + " (" + reg.LoadedMembers(v.Id).Count + " loaded)"
                    + "  at " + v.Centre);
            }
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        private static TextCommandResult OnVillageInfo(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            var reg = Registry(sapi);
            int id = (int)args[0];

            Village v = id > 0 ? reg.Get(id) : null;
            if (v == null && id > 0) return TextCommandResult.Error("No village with id " + id + ". " + reg.IdList() + ".");

            if (v == null)
            {
                IServerPlayer player = args.Caller.Player as IServerPlayer;
                if (player?.Entity == null) return TextCommandResult.Error("Give an id, or stand in a village.");
                v = reg.VillageAt(player.Entity.Pos.AsBlockPos) ?? reg.Nearest(player.Entity.Pos.AsBlockPos, 200);
                if (v == null) return TextCommandResult.Error("No village within 200 blocks. /ff village list");
            }

            double age = sapi.World.Calendar.TotalDays - v.FoundedTotalDays;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- " + v.Name + " (#" + v.Id + ") ---");
            sb.AppendLine("Culture    " + v.CultureCode);
            sb.AppendLine("Tier       " + v.Tier);
            sb.AppendLine("Founded    day " + (int)v.FoundedTotalDays + " (" + (int)age + " days ago)");
            sb.AppendLine("Centre     " + v.Centre);
            sb.AppendLine("Claim      " + v.ClaimRadius + " blocks, box " + v.ClaimBox);
            sb.AppendLine("Cairn      " + (v.HasMarker
                ? new BlockPos(v.MarkerX, v.MarkerY, v.MarkerZ, 0).ToString()
                : "(gone, /ff village mark puts it back)"));
            sb.AppendLine("Storehouse " + (v.HasStorehouse
                ? new BlockPos(v.StorehouseX, v.StorehouseY, v.StorehouseZ, 0).ToString()
                : "(gone, /ff village storehouse puts it back)"));
            sb.AppendLine("Roster     " + v.MemberIds.Count + " total, "
                          + reg.LoadedMembers(v.Id).Count + " loaded");

            sb.AppendLine(VillageRegistry.DescribeFacilities(v));
            sb.AppendLine("Day        " + (int)v.LastSimulatedDay + ", " + v.DaysAtCurrentTier + " day(s) at this tier");
            sb.AppendLine();
            sb.AppendLine(v.Ledger.Describe());
            sb.AppendLine();

            var loaded = reg.LoadedMembers(v.Id);
            if (loaded.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Here now:");
                foreach (FFVillager m in loaded)
                {
                    sb.AppendLine("  " + (m.GivenName == "" ? "#" + m.EntityId : m.GivenName).PadRight(22)
                                  + m.Trade.ToString().ToLowerInvariant().PadRight(12)
                                  + (v.Contains(m.Pos.AsBlockPos) ? "inside claim" : "OUTSIDE claim"));
                }
            }

            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        private static TextCommandResult OnVillageRemove(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            var reg = Registry(sapi);
            string arg = (args[0] as string ?? "").Trim();

            if (arg.Length == 0)
            {
                IServerPlayer standing = args.Caller.Player as IServerPlayer;
                if (standing?.Entity == null) return TextCommandResult.Error("Give a village id, or 'all'.");

                BlockPos where = standing.Entity.Pos.AsBlockPos;
                Village near = reg.VillageAt(where) ?? reg.Nearest(where, 400);
                if (near == null)
                {
                    return TextCommandResult.Error("No village within 400 blocks. " + reg.IdList() + ".");
                }
                arg = near.Id.ToString();
            }

            if (arg.ToLowerInvariant() == "all")
            {
                var ids = new List<long>();
                foreach (Village village in reg.All) ids.Add(village.Id);
                int orphans = 0;
                foreach (long vid in ids)
                {
                    orphans += reg.LoadedMembers(vid).Count;
                    reg.Remove(vid);
                }
                return TextCommandResult.Success(
                    "Removed " + ids.Count + " village(s). " + orphans + " loaded villager(s) are now unaffiliated.");
            }

            if (!long.TryParse(arg, out long id))
            {
                return TextCommandResult.Error("Give a village id, or 'all'. " + reg.IdList() + ".");
            }

            Village v = reg.Get(id);
            if (v == null) return TextCommandResult.Error("No village with id " + id + ". " + reg.IdList() + ".");

            int orphaned = reg.LoadedMembers(id).Count;
            reg.Remove(id);
            return TextCommandResult.Success(
                "Removed " + v.Name + " (#" + id + "). " + orphaned + " loaded villager(s) are now unaffiliated.");
        }

        /// <summary>
        /// Everything to do with plots, in one command.
        ///
        /// Kept as one subcommand with a verb rather than six subcommands because plots
        /// are a testing tool at this stage and the whole point is being able to try
        /// something, look at it, and undo it without leaving the spot you are standing in.
        /// </summary>
        private static TextCommandResult OnVillagePlot(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("No player entity.");

            var reg = Registry(sapi);
            BlockPos here = player.Entity.Pos.AsBlockPos;
            Village v = reg.VillageAt(here) ?? reg.Nearest(here, 400);

            string action = (args[0] as string ?? "list").Trim().ToLowerInvariant();
            string what = (args[1] as string ?? "").Trim().ToLowerInvariant();

            if (action == "show" && what == "off")
            {
                reg.HidePlots(player);
                return TextCommandResult.Success("Plot outlines off.");
            }

            if (v == null) return TextCommandResult.Error("No village within 400 blocks. " + reg.IdList() + ".");

            switch (action)
            {
                case "list": return PlotList(v);
                case "show":
                    reg.ShowPlots(player, v);
                    return TextCommandResult.Success(
                        "Outlining " + v.Plots.Count + " plot(s) in " + v.Name
                        + ". /ff village plot show off to clear.");

                case "add":
                {
                    if (!ParseKind(what, out EnumPlotKind kind)) return UnknownKind(what);
                    VillagePlot plot = reg.PlacePlot(v, kind, here, VillageRegistry.HalfSizeFor(kind));
                    if (plot == null)
                    {
                        return TextCommandResult.Error(
                            "That would overlap an existing plot. /ff village plot list to see them.");
                    }
                    reg.ShowPlots(player, v);
                    return TextCommandResult.Success("Added " + plot + ".");
                }

                case "site":
                {
                    if (!ParseKind(what, out EnumPlotKind kind)) return UnknownKind(what);
                    VillagePlot plot = reg.SitePlot(v, kind, out string error);
                    if (plot == null) return TextCommandResult.Error(error ?? "Could not site it.");
                    reg.ShowPlots(player, v);
                    return TextCommandResult.Success(
                        "Sited " + plot + ". The village chose the spot, not you.");
                }

                case "remove":
                {
                    if (!int.TryParse(what, out int id))
                    {
                        return TextCommandResult.Error("Which plot? /ff village plot remove <id>");
                    }
                    if (!reg.RemovePlot(v, id))
                    {
                        return TextCommandResult.Error("No plot #" + id + " in " + v.Name + ".");
                    }
                    reg.ShowPlots(player, v);
                    return TextCommandResult.Success("Removed plot #" + id + ".");
                }

                case "clear":
                {
                    int n = v.Plots.Count;
                    var ids = new List<int>();
                    foreach (VillagePlot p in v.Plots) ids.Add(p.Id);
                    foreach (int id in ids) reg.RemovePlot(v, id);
                    reg.HidePlots(player);
                    return TextCommandResult.Success("Cleared " + n + " plot(s) from " + v.Name + ".");
                }

                default:
                    return TextCommandResult.Error(
                        "Unknown action '" + action + "'. Try list, show, add, site, remove or clear.");
            }
        }

        private static TextCommandResult PlotList(Village v)
        {
            if (v.Plots.Count == 0)
            {
                return TextCommandResult.Success(
                    v.Name + " has no plots. /ff village plot site woodlot to let it choose one, "
                    + "or /ff village plot add woodlot to put one where you are standing.");
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(v.Name + " holds " + VillageRegistry.CountPlots(v) + " working plot(s) of "
                          + VillageRegistry.MaxPlotsFor(v) + " allowed at tier " + v.Tier + ":");

            foreach (VillagePlot p in v.Plots)
            {
                sb.Append("  ").Append(p);
                if (p.LifetimeYield > 0) sb.Append(", ").Append(p.LifetimeYield.ToString("0")).Append(" worked");
                sb.AppendLine();
            }
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        private static bool ParseKind(string name, out EnumPlotKind kind)
        {
            kind = EnumPlotKind.Woodlot;
            if (string.IsNullOrWhiteSpace(name)) return false;
            return Enum.TryParse(name, true, out kind);
        }

        private static TextCommandResult UnknownKind(string given)
        {
            return TextCommandResult.Error(
                "Unknown plot kind '" + given + "'. One of: "
                + string.Join(", ", Enum.GetNames(typeof(EnumPlotKind))).ToLowerInvariant() + ".");
        }

        /// <summary>
        /// What loaded, what it costs, and what is wrong with it.
        ///
        /// The complaints half matters as much as the list. A schematic that failed to
        /// load or has no manifest entry is invisible in play, and the only way to find
        /// out used to be to notice that no village ever built it.
        /// </summary>
        private static TextCommandResult OnBuildings(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            var catalogue = sapi.ModLoader.GetModSystem<BuildingCatalogue>();
            if (catalogue == null) return TextCommandResult.Error("Building catalogue is not loaded.");

            var sb = new System.Text.StringBuilder();

            if (catalogue.Count == 0)
            {
                sb.AppendLine("No building schematics loaded.");
                sb.AppendLine("Export some with WorldEdit into assets/foundriesfrontiers/worldgen/schematics/");
                sb.AppendLine("and give each one an entry in config/buildings.json.");
            }
            else
            {
                sb.AppendLine(catalogue.Count + " building(s):");
                foreach (BuildingPlan plan in catalogue.All)
                {
                    sb.AppendLine("  " + plan.Name + " (" + plan.Code + ")");
                    sb.AppendLine("    " + plan.SizeX + "x" + plan.SizeY + "x" + plan.SizeZ
                                  + ", " + plan.SolidBlockCount + " blocks"
                                  + ", tier " + (plan.Manifest?.Tier ?? 0)
                                  + ", " + (plan.Manifest?.Need.ToString().ToLowerInvariant() ?? "?"));
                    sb.AppendLine("    costs " + plan.CostLine()
                                  + (plan.Unpriced > 0
                                      ? "   (" + plan.Unpriced + " block(s) nothing could price)"
                                      : ""));

                    // The three commonest blocks, which is enough to tell at a glance
                    // whether a building is timber, stone or mud.
                    var top = new List<KeyValuePair<string, int>>(plan.BillOfBlocks);
                    top.Sort((a, b) => b.Value.CompareTo(a.Value));
                    var bits = new List<string>();
                    for (int i = 0; i < top.Count && i < 3; i++)
                    {
                        bits.Add(top[i].Value + "x " + top[i].Key.Replace("game:", ""));
                    }
                    if (bits.Count > 0) sb.AppendLine("    mostly " + string.Join(", ", bits));
                }
            }

            if (catalogue.Complaints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Problems:");
                foreach (string c in catalogue.Complaints) sb.AppendLine("  " + c);
            }

            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        private static TextCommandResult OnVillageBuild(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("No player entity.");

            var reg = Registry(sapi);
            BlockPos here = player.Entity.Pos.AsBlockPos;
            Village v = reg.VillageAt(here) ?? reg.Nearest(here, 400);
            if (v == null) return TextCommandResult.Error("No village within 400 blocks. " + reg.IdList() + ".");

            string action = (args[0] as string ?? "list").Trim().ToLowerInvariant();
            string what = (args[1] as string ?? "").Trim();

            switch (action)
            {
                case "list":
                {
                    if (v.BuildSites.Count == 0)
                    {
                        return TextCommandResult.Success(
                            v.Name + " has built nothing. /ff village build next to let it choose one.");
                    }
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine(v.Name + " has " + v.BuildSites.Count + " building(s):");
                    foreach (VillageBuildSite s in v.BuildSites) sb.AppendLine("  " + s);
                    return TextCommandResult.Success(sb.ToString().TrimEnd());
                }

                case "next":
                {
                    VillageBuildSite site = reg.StartBuild(v, out string error);
                    if (site == null) return TextCommandResult.Error(error ?? "Nothing to build.");
                    return TextCommandResult.Success(
                        v.Name + " has decided on " + site + ". A builder will start when it can pay.");
                }

                case "cancel":
                {
                    if (!int.TryParse(what, out int id)) return TextCommandResult.Error("Which site? /ff village build cancel <id>");
                    VillageBuildSite site = reg.SiteById(v, id);
                    if (site == null) return TextCommandResult.Error("No site #" + id + " in " + v.Name + ".");

                    site.State = EnumBuildState.Abandoned;
                    site.BuilderEntityId = 0;
                    return TextCommandResult.Success("Abandoned site #" + id + ". What was placed stays where it is.");
                }

                default:
                {
                    // Anything else is a schematic code, so the exact building can be
                    // tested without waiting for the village to want it.
                    var catalogue = sapi.ModLoader.GetModSystem<BuildingCatalogue>();
                    BuildingPlan plan = catalogue?.Get(action);
                    if (plan == null)
                    {
                        return TextCommandResult.Error(
                            "No building called '" + action + "'. /ff buildings to see what loaded.");
                    }

                    VillageBuildSite site = reg.StartBuild(v, plan, out string error);
                    if (site == null) return TextCommandResult.Error(error ?? "Could not site it.");
                    return TextCommandResult.Success("Sited " + site + ", costing " + plan.CostLine() + ".");
                }
            }
        }

        private static TextCommandResult OnVillageShow(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("No player entity.");

            string arg = (args[0] as string ?? "").Trim().ToLowerInvariant();
            var reg = Registry(sapi);

            if (arg == "off")
            {
                reg.HideClaim(player);
                return TextCommandResult.Success("Claim outline off.");
            }

            Village v;
            if (arg.Length > 0 && long.TryParse(arg, out long id))
            {
                v = reg.Get(id);
                if (v == null) return TextCommandResult.Error("No village with id " + id + ". " + reg.IdList() + ".");
            }
            else
            {
                BlockPos here = player.Entity.Pos.AsBlockPos;
                v = reg.VillageAt(here) ?? reg.Nearest(here, 400);
                if (v == null) return TextCommandResult.Error("No village within 400 blocks. " + reg.IdList() + ".");
            }

            reg.ShowClaim(player, v);
            return TextCommandResult.Success(
                "Outlining " + v.Name + " (#" + v.Id + "), claim " + v.ClaimRadius
                + " blocks out from " + v.Centre + ". /ff village show off to clear.");
        }

        private static TextCommandResult OnVillageAdopt(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("No player entity.");

            var reg = Registry(sapi);
            int id = (int)args[0];
            BlockPos here = player.Entity.Pos.AsBlockPos;
            Village v = id > 0 ? reg.Get(id) : reg.VillageAt(here) ?? reg.Nearest(here, 400);

            if (v == null)
            {
                return TextCommandResult.Error(id > 0
                    ? "No village with id " + id + "."
                    : "No village within 400 blocks.");
            }

            int adopted = reg.AdoptUnaffiliated(v);
            return TextCommandResult.Success(adopted == 0
                ? "Nobody inside " + v.Name + "'s claim needed adopting."
                : v.Name + " adopted " + adopted + " villager(s). Roster: " + v.MemberIds.Count + ".");
        }

        // --- the ledger --------------------------------------------------------------

        /// <summary>The village a ledger command should act on: an explicit id, else where you stand.</summary>
        private static Village LedgerTarget(ICoreServerAPI sapi, TextCommandCallingArgs args, int id, out string error)
        {
            error = null;
            var reg = Registry(sapi);

            if (id > 0)
            {
                Village byId = reg.Get(id);
                if (byId == null) error = "No village with id " + id + ".";
                return byId;
            }

            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null)
            {
                error = "Give a village id.";
                return null;
            }

            BlockPos here = player.Entity.Pos.AsBlockPos;
            Village v = reg.VillageAt(here) ?? reg.Nearest(here, 400);
            if (v == null) error = "No village within 400 blocks. /ff village list";
            return v;
        }

        private static TextCommandResult OnVillageLedger(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            Village v = LedgerTarget(sapi, args, (int)args[0], out string error);
            if (v == null) return TextCommandResult.Error(error);

            return TextCommandResult.Success(
                "--- " + v.Name + " (#" + v.Id + ") stores ---\n" + v.Ledger.Describe());
        }

        private static TextCommandResult OnVillageGive(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            EnumVillageResource? r = VillageResources.Parse(args[0] as string);
            if (r == null) return TextCommandResult.Error("Unknown resource. Pools: " + VillageResources.Names);

            Village v = LedgerTarget(sapi, args, 0, out string error);
            if (v == null) return TextCommandResult.Error(error);

            float amount = (float)args[1];
            v.Ledger.Deposit(r.Value, amount);
            Registry(sapi).RefreshStorehouse(v);
            return TextCommandResult.Success(
                v.Name + " " + r.Value.ToString().ToLowerInvariant() + " is now "
                + v.Ledger.Get(r.Value).ToString("0.#") + " (counted as today's income).");
        }

        private static TextCommandResult OnVillageTake(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            EnumVillageResource? r = VillageResources.Parse(args[0] as string);
            if (r == null) return TextCommandResult.Error("Unknown resource. Pools: " + VillageResources.Names);

            Village v = LedgerTarget(sapi, args, 0, out string error);
            if (v == null) return TextCommandResult.Error(error);

            float amount = (float)args[1];
            bool ok = v.Ledger.Withdraw(r.Value, amount);
            Registry(sapi).RefreshStorehouse(v);
            return ok
                ? TextCommandResult.Success(
                    v.Name + " " + r.Value.ToString().ToLowerInvariant() + " is now "
                    + v.Ledger.Get(r.Value).ToString("0.#") + " (counted as today's spending).")
                : TextCommandResult.Error(
                    v.Name + " only has " + v.Ledger.Get(r.Value).ToString("0.#") + " "
                    + r.Value.ToString().ToLowerInvariant() + ". Withdrawals refuse rather than go negative.");
        }

        private static TextCommandResult OnVillageSet(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            EnumVillageResource? r = VillageResources.Parse(args[0] as string);
            if (r == null) return TextCommandResult.Error("Unknown resource. Pools: " + VillageResources.Names);

            Village v = LedgerTarget(sapi, args, 0, out string error);
            if (v == null) return TextCommandResult.Error(error);

            float amount = (float)args[1];
            v.Ledger.SetDirectly(r.Value, amount);
            Registry(sapi).RefreshStorehouse(v);
            return TextCommandResult.Success(
                v.Name + " " + r.Value.ToString().ToLowerInvariant() + " set to "
                + v.Ledger.Get(r.Value).ToString("0.#") + ". No flow recorded.");
        }

        private static TextCommandResult OnVillageDeposit(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            FFVillager villager = FindTarget(sapi, args.Caller.Player as IServerPlayer);
            if (villager == null) return TextCommandResult.Error("No villager in sight or within 20 blocks.");

            bool ok = Registry(sapi).DepositCarried(villager, out string outcome);
            return ok ? TextCommandResult.Success(outcome) : TextCommandResult.Error(outcome);
        }

        private static TextCommandResult OnVillageDay(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            Village v = LedgerTarget(sapi, args, 0, out string error);
            if (v == null) return TextCommandResult.Error(error);

            int count = GameMath.Clamp((int)args[0], 1, 30);
            for (int i = 0; i < count; i++) Registry(sapi).ForceDay(v);

            return TextCommandResult.Success(
                "Closed " + count + " day(s) for " + v.Name + ". Now " + v.Ledger.DaysRecorded
                + " day(s) of history.\n" + v.Ledger.Describe());
        }

        private static TextCommandResult OnVillageTable(ICoreServerAPI sapi)
        {
            var table = sapi.ModLoader.GetModSystem<ResourceTable>();
            return table == null
                ? TextCommandResult.Error("Resource table not loaded.")
                : TextCommandResult.Success("--- how items are sorted ---\n" + table.Describe());
        }

        private static TextCommandResult OnVillageTier(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            int tier = (int)args[0];
            if (tier < 0 || tier > VillageRegistry.MaxTier)
                return TextCommandResult.Error("Tier runs 0 to " + VillageRegistry.MaxTier + ". Town is the capstone.");

            Village v = LedgerTarget(sapi, args, (int)args[1], out string error);
            if (v == null) return TextCommandResult.Error(error);

            int was = v.Tier;
            if (!Registry(sapi).SetTier(v, tier))
            {
                return TextCommandResult.Success(v.Name + " is already tier " + tier + ".");
            }

            return TextCommandResult.Success(
                v.Name + " moved from tier " + was + " to " + tier
                + ". Marker is now the " + VillageRegistry.StageForTier(tier)
                + ", claim is " + v.ClaimRadius + " blocks.");
        }

        private static TextCommandResult OnVillageAbandon(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            Village v = LedgerTarget(sapi, args, (int)args[0], out string error);
            if (v == null) return TextCommandResult.Error(error);

            return Registry(sapi).Abandon(v.Id, out string report)
                ? TextCommandResult.Success(report)
                : TextCommandResult.Error("Could not abandon " + v.Name + ".");
        }

        private static TextCommandResult OnVillageScan(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            Village v = LedgerTarget(sapi, args, (int)args[0], out string error);
            if (v == null) return TextCommandResult.Error(error);

            int found = Registry(sapi).ScanFacilities(v);
            int given = Registry(sapi).AssignBeds(v);

            return TextCommandResult.Success(
                "Scanned " + v.Name + ", claim " + v.ClaimRadius + " blocks. Found " + found + " facility(s).\n"
                + VillageRegistry.DescribeFacilities(v)
                + (given > 0 ? "\nHanded out " + given + " bed(s)." : ""));
        }

        private static TextCommandResult OnVillageBeds(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            Village v = LedgerTarget(sapi, args, (int)args[0], out string error);
            if (v == null) return TextCommandResult.Error(error);

            var reg = Registry(sapi);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- " + v.Name + " ---");
            sb.AppendLine(VillageRegistry.DescribeFacilities(v));
            sb.AppendLine();

            var loaded = reg.LoadedMembers(v.Id);
            if (loaded.Count == 0)
            {
                sb.Append("Nobody here to sleep in them.");
            }
            else
            {
                foreach (FFVillager m in loaded)
                {
                    VillageFacility bed = VillageRegistry.BedOf(v, m.EntityId);
                    sb.AppendLine("  " + (m.GivenName == "" ? "#" + m.EntityId : m.GivenName).PadRight(22)
                                  + (bed == null ? "no bed" : "bed at " + bed.Pos));
                }
            }
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        private static TextCommandResult OnVillageStorehouse(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            Village v = LedgerTarget(sapi, args, (int)args[0], out string error);
            if (v == null) return TextCommandResult.Error(error);

            BlockPos placed = Registry(sapi).PlaceStorehouse(v);
            return placed == null
                ? TextCommandResult.Error("No room for a storehouse near " + v.Centre + ".")
                : TextCommandResult.Success(v.Name + "'s storehouse is at " + placed + ".");
        }

        private static TextCommandResult OnVillageMark(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("No player entity.");

            var reg = Registry(sapi);
            int id = (int)args[0];
            BlockPos here = player.Entity.Pos.AsBlockPos;
            Village v = id > 0 ? reg.Get(id) : reg.VillageAt(here) ?? reg.Nearest(here, 400);
            if (v == null) return TextCommandResult.Error("No village found. /ff village list");

            BlockPos placed = reg.PlaceMarker(v);
            return placed == null
                ? TextCommandResult.Error("Could not find room for a cairn near " + v.Centre + ".")
                : TextCommandResult.Success("Cairn for " + v.Name + " placed at " + placed + ".");
        }

        private static TextCommandResult OnVillageJoin(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            FFVillager villager = FindTarget(sapi, player);
            if (villager == null) return TextCommandResult.Error("No villager in sight or within 20 blocks.");

            var reg = Registry(sapi);
            int id = (int)args[0];
            Village v = id > 0
                ? reg.Get(id)
                : reg.VillageAt(villager.Pos.AsBlockPos) ?? reg.Nearest(villager.Pos.AsBlockPos, 200);

            if (v == null)
            {
                return TextCommandResult.Error(id > 0
                    ? "No village with id " + id + "."
                    : "No village within 200 blocks of that villager.");
            }

            reg.Join(villager, v);
            return TextCommandResult.Success(
                (villager.GivenName == "" ? "#" + villager.EntityId : villager.GivenName)
                + " now belongs to " + v.Name + " (#" + v.Id + "). Roster: " + v.MemberIds.Count + ".");
        }

        private static TextCommandResult OnVillageLeave(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            FFVillager villager = FindTarget(sapi, args.Caller.Player as IServerPlayer);
            if (villager == null) return TextCommandResult.Error("No villager in sight or within 20 blocks.");
            if (villager.VillageId == 0) return TextCommandResult.Error("That villager has no village.");

            Village v = Registry(sapi).Get(villager.VillageId);
            Registry(sapi).Leave(villager);
            return TextCommandResult.Success(
                (villager.GivenName == "" ? "#" + villager.EntityId : villager.GivenName)
                + " left " + (v?.Name ?? "their village") + ".");
        }

        private static TextCommandResult OnTime(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            float want = GameMath.Clamp((float)args[0], 0f, 23.99f);
            float now = (float)sapi.World.Calendar.HourOfDay;

            // The calendar only moves forwards, so going "back" means going round.
            float hours = want - now;
            if (hours < 0) hours += 24;

            sapi.World.Calendar.Add(hours);

            return TextCommandResult.Success(
                "Hour is now " + sapi.World.Calendar.HourOfDay.ToString("0.0")
                + ". Villagers should be " + VillageSchedule.Describe(
                    VillageSchedule.PhaseFor(sapi, args.Caller.Player?.Entity?.Pos.AsBlockPos
                                                   ?? new BlockPos(0, 0, 0, 0))) + ".");
        }

        /// <summary>
        /// Every trade, what it carries, and whether it has a job to do yet.
        ///
        /// The tool column matters more than it looks: tools carry real behaviour in this
        /// game rather than a speed bonus, so a trade with the wrong thing in its hands
        /// cannot do its work at all. This is where to check that config/tools.json says
        /// what you think it says.
        /// </summary>
        private static TextCommandResult OnTrades(ICoreServerAPI sapi)
        {
            var catalogue = sapi.ModLoader.GetModSystem<WorldCatalogue>();

            // The trades that actually have an AI task behind them today.
            var working = new HashSet<EnumTrade>
            {
                EnumTrade.Lumberjack, EnumTrade.Farmer, EnumTrade.Herder,
                EnumTrade.Forager, EnumTrade.Builder
            };

            var sb = new System.Text.StringBuilder();
            var all = Enum.GetValues<EnumTrade>();
            sb.AppendLine(all.Length + " trades defined. Tool comes from config/tools.json.");
            sb.AppendLine();

            foreach (EnumTrade trade in all)
            {
                string tool = catalogue?.ToolNameFor(trade) ?? "?";
                sb.AppendLine("  " + trade.ToString().ToLowerInvariant().PadRight(15)
                              + tool.Replace("game:", "").PadRight(22)
                              + (working.Contains(trade) ? "has a job" : "no job yet"));
            }

            sb.AppendLine();
            sb.AppendLine("/ff spawn <trade> to make one. A villager is handed their tool at spawn");
            sb.AppendLine("unless GiveTradeToolsOnSpawn is off, and /ff give <itemcode> replaces it.");
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }
    }
}
