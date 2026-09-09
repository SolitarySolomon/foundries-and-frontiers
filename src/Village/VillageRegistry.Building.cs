using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Deciding what to build, where to put it, paying for it, and putting it up a few
    /// blocks at a time.
    ///
    /// The important rule, which is the one the whole economy rests on: **the village
    /// pays the ledger cost once, before a single block goes down, and refuses to start
    /// otherwise.** A village that paid per block and ran out halfway would leave a shell
    /// it could neither finish nor recover the materials from, and the design says a
    /// stalled build should stall visibly at the start rather than rot in the middle.
    /// </summary>
    public partial class VillageRegistry
    {
        private BuildingCatalogue Buildings
            => sapi?.ModLoader.GetModSystem<BuildingCatalogue>();

        // --- choosing and siting -------------------------------------------------------

        /// <summary>
        /// Decides on the next building for this village and finds it a spot.
        /// Returns null with a reason, which is a real answer: a village may have nothing
        /// it wants, nothing it can afford, or nowhere flat enough left inside its claim.
        /// </summary>
        public VillageBuildSite StartBuild(Village village, out string error)
        {
            error = null;
            if (village == null) { error = "No village."; return null; }

            BuildingCatalogue catalogue = Buildings;
            if (catalogue == null || catalogue.Count == 0)
            {
                error = "No building schematics are loaded, so there is nothing to build.";
                return null;
            }

            if (OpenSites(village) >= FFConfig.Current.Build.MaxSitesAtOnce)
            {
                error = village.Name + " already has "
                      + FFConfig.Current.Build.MaxSitesAtOnce + " building(s) going up.";
                return null;
            }

            BuildingPlan plan = catalogue.ChooseFor(village, p => CountBuilt(village, p));
            if (plan == null)
            {
                error = village.Name + " wants nothing it knows how to build at tier " + village.Tier + ".";
                return null;
            }

            return StartBuild(village, plan, out error);
        }

        /// <summary>Sites a specific building, for the test command and for the brain later.</summary>
        public VillageBuildSite StartBuild(Village village, BuildingPlan plan, out string error)
        {
            error = null;
            if (village == null || plan == null) { error = "Nothing to build."; return null; }

            BlockPos where = FindBuildSite(village, plan);
            if (where == null)
            {
                error = "Found nowhere in the claim flat and clear enough for a "
                      + plan.Name + " (" + plan.SizeX + "x" + plan.SizeZ + ").";
                return null;
            }

            var site = new VillageBuildSite
            {
                Id = village.NextBuildSiteId++,
                PlanCode = plan.Code,
                State = EnumBuildState.Waiting,
                X = where.X,
                Y = where.Y,
                Z = where.Z,
                Total = Math.Max(1, plan.Layout.Count),
                LayoutCount = plan.Layout.Count,
                StartedTotalDays = sapi.World.Calendar.TotalDays,
                Holdup = "waiting on materials"
            };

            village.BuildSites.Add(site);
            return site;
        }

        /// <summary>
        /// Called once a day. If the village has a builder and nothing on the go, it
        /// decides on something.
        ///
        /// This is the smallest piece of autonomy that makes the phase testable: a
        /// village left alone with a builder in it will start putting something up
        /// tomorrow rather than waiting for a command. The real decision, which weighs a
        /// village's needs against what it is short of, is the brain's job in Phase D.
        /// </summary>
        public void ConsiderBuilding(Village village)
        {
            if (village == null) return;
            if (OpenSites(village) > 0) return;
            if (Buildings == null || Buildings.Count == 0) return;

            bool hasBuilder = false;
            foreach (FFVillager v in LoadedMembers(village.Id))
            {
                if (v.Trade == EnumTrade.Builder) { hasBuilder = true; break; }
            }
            if (!hasBuilder) return;

            VillageBuildSite site = StartBuild(village, out string error);
            if (site != null)
            {
                sapi.Logger.Notification("[F&F] {0} has decided to build {1}.", village.Name, site.PlanCode);
            }
            else if (error != null)
            {
                sapi.Logger.VerboseDebug("[F&F] {0} is not building: {1}", village.Name, error);
            }
        }

        public static int OpenSites(Village village)
        {
            if (village?.BuildSites == null) return 0;
            int n = 0;
            foreach (VillageBuildSite s in village.BuildSites) if (s.IsOpen) n++;
            return n;
        }

        public static int CountBuilt(Village village, BuildingPlan plan)
        {
            if (village?.BuildSites == null || plan == null) return 0;
            int n = 0;
            foreach (VillageBuildSite s in village.BuildSites)
            {
                if (s.PlanCode == plan.Code && s.State != EnumBuildState.Abandoned) n++;
            }
            return n;
        }

        public VillageBuildSite SiteById(Village village, int id)
        {
            if (village?.BuildSites == null) return null;
            foreach (VillageBuildSite s in village.BuildSites) if (s.Id == id) return s;
            return null;
        }

        /// <summary>The site a builder should be working on, or null if there is none.</summary>
        public VillageBuildSite OpenSiteFor(Village village, long builderId)
        {
            if (village?.BuildSites == null) return null;

            VillageBuildSite free = null;
            foreach (VillageBuildSite s in village.BuildSites)
            {
                if (!s.IsOpen) continue;
                if (s.BuilderEntityId == builderId) return s;
                if (free == null && s.BuilderEntityId == 0) free = s;
            }
            return free;
        }

        // --- paying --------------------------------------------------------------------

        /// <summary>
        /// Takes the materials out of the ledger, all at once, and lets the site start.
        ///
        /// Returns false and writes the shortfall onto the site when the village cannot
        /// cover it. That message is the whole point of stalling visibly: a player who
        /// walks past a site that has not moved in three days should be able to look at it
        /// and see that the village is forty stone short.
        /// </summary>
        public bool TryPayFor(Village village, VillageBuildSite site)
        {
            if (village == null || site == null) return false;
            if (site.Paid) return true;

            BuildingPlan plan = Buildings?.Get(site.PlanCode);
            if (plan == null)
            {
                site.Holdup = "its plan no longer exists";
                return false;
            }

            if (!BuildingCatalogue.CanAfford(village, plan))
            {
                site.Holdup = "short " + BuildingCatalogue.Shortfall(village, plan);
                return false;
            }

            // Everything or nothing. Withdrawing pool by pool and failing partway would
            // charge a village for a building it never gets.
            foreach (EnumVillageResource r in VillageResources.All)
            {
                float cost = plan.LedgerCost[(int)r];
                if (cost <= 0) continue;

                if (!village.Ledger.Withdraw(r, cost))
                {
                    // Something moved between the check and here. Put back whatever was
                    // taken so far and try again next time.
                    foreach (EnumVillageResource back in VillageResources.All)
                    {
                        if (back == r) break;
                        village.Ledger.Refund(back, plan.LedgerCost[(int)back]);
                    }
                    site.Holdup = "short " + BuildingCatalogue.Shortfall(village, plan);
                    return false;
                }
            }

            site.Paid = true;
            site.State = EnumBuildState.Building;
            site.Holdup = "";

            RefreshStorehouse(village);
            sapi.Logger.Notification(
                "[F&F] {0} paid {1} for a {2} and started building.",
                village.Name, plan.CostLine(), plan.Name);

            return true;
        }

        // --- putting it up ---------------------------------------------------------------

        /// <summary>
        /// Places the next few blocks of a site.
        ///
        /// A slice at a time rather than the whole schematic in one call, because the
        /// point of Phase C is buildings that go up over days while you watch, and
        /// because placing four hundred blocks on one tick is a stall.
        ///
        /// The schematic is placed in full the first time and then revealed: that is not
        /// what happens here. Blocks are placed individually from the packed data in the
        /// schematic's own order, so a half built house is genuinely half built and
        /// breaking it does what breaking it should.
        /// </summary>
        public int PlaceSlice(Village village, VillageBuildSite site, int blocks)
        {
            if (village == null || site == null || site.State != EnumBuildState.Building) return 0;

            BuildingPlan plan = Buildings?.Get(site.PlanCode);
            if (plan?.Schematic == null)
            {
                Abandon(village, site, "its plan no longer exists");
                return 0;
            }

            if (plan.Layout.Count == 0)
            {
                Abandon(village, site, "its schematic has no blocks in it");
                return 0;
            }

            // The layout is rebuilt from the world's block registry every startup, so its
            // length can change under a saved site: install a mod, or re-export the
            // schematic, and the cursor now points somewhere else entirely. Left alone
            // that silently marks a third built house finished. Starting over is cheap,
            // because placing a block that is already there costs nothing.
            if (site.LayoutCount != plan.Layout.Count)
            {
                sapi.Logger.Notification(
                    "[F&F] {0} at {1} was built against a {2} block layout and this world has {3}. "
                    + "Starting it again from the beginning.",
                    plan.Name, site.Origin, site.LayoutCount, plan.Layout.Count);

                site.Placed = 0;
                site.LayoutCount = plan.Layout.Count;
                site.Total = Math.Max(1, plan.Layout.Count);
            }

            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos origin = site.Origin;
            int placed = 0;

            while (site.Placed < plan.Layout.Count && placed < blocks)
            {
                (BlockPos offset, Block block) = plan.Layout[site.Placed++];
                if (block == null || block.Id == 0) continue;

                var at = new BlockPos(origin.X + offset.X, origin.Y + offset.Y, origin.Z + offset.Z, 0);
                if (!ba.IsValidPos(at)) continue;

                // Water and lava live on their own layer. Writing a fluid into the solid
                // layer erases the block placed underneath it a moment earlier, which is
                // how a well ends up as a hole.
                ba.SetBlock(block.BlockId, at, block.ForFluidsLayer ? 2 : 1);
                placed++;
            }

            if (site.Placed >= plan.Layout.Count)
            {
                Finish(village, site, plan);
            }

            return placed;
        }

        private void Finish(Village village, VillageBuildSite site, BuildingPlan plan)
        {
            site.State = EnumBuildState.Done;
            site.Holdup = "";
            site.BuilderEntityId = 0;

            // Block entities and decor come last, in one go. They are not blocks in the
            // packed grid and there is no sensible way to reveal a chest one plank at a
            // time, so a finished building gets its furniture when it is finished.
            try
            {
                plan.Schematic.PlaceDecors(sapi.World.BlockAccessor, site.Origin);
                plan.Schematic.PlaceEntitiesAndBlockEntities(
                    sapi.World.BlockAccessor, sapi.World, site.Origin,
                    plan.Schematic.BlockCodes, plan.Schematic.ItemCodes,
                    false, null, 0, null, true);
            }
            catch (Exception e)
            {
                sapi.Logger.Warning(
                    "[F&F] Finishing {0} at {1} threw while placing its fittings: {2}",
                    plan.Name, site.Origin, e.Message);
            }

            sapi.Logger.Notification("[F&F] {0} finished a {1} at {2}.", village.Name, plan.Name, site.Origin);

            // A new building almost certainly contains beds or a workstation, and the
            // facility scan is the only thing that knows about them.
            ScanFacilities(village);
        }

        /// <summary>
        /// Gives up on a site, and gives the materials back if they were ever taken.
        ///
        /// The refund is the part that matters. A village that pays sixty wood for a
        /// house and then abandons the site because its schematic vanished has lost sixty
        /// wood to a bug, and the ledger's whole reason for existing is that nothing in
        /// it moves without a reason anyone can point at.
        /// </summary>
        public void Abandon(Village village, VillageBuildSite site, string why)
        {
            if (village == null || site == null) return;

            if (site.Paid)
            {
                BuildingPlan plan = Buildings?.Get(site.PlanCode);
                if (plan != null)
                {
                    foreach (EnumVillageResource r in VillageResources.All)
                    {
                        village.Ledger.Refund(r, plan.LedgerCost[(int)r]);
                    }
                    RefreshStorehouse(village);
                }
                site.Paid = false;
            }

            site.State = EnumBuildState.Abandoned;
            site.BuilderEntityId = 0;
            site.Holdup = why ?? "";

            sapi.Logger.Notification("[F&F] {0} gave up on {1}: {2}", village.Name, site.PlanCode, site.Holdup);
        }

        // --- where it goes ---------------------------------------------------------------

        /// <summary>
        /// Somewhere in the claim a building of this size will stand.
        ///
        /// The same shape of search the plots use, and for the same reasons, but stricter:
        /// a field can be a bit lumpy and a house cannot. It also stays off the plots, so
        /// a village does not put a cottage in the middle of its own wheat.
        /// </summary>
        private BlockPos FindBuildSite(Village village, BuildingPlan plan)
        {
            var cfg = FFConfig.Current.Build;
            int w = Math.Max(1, plan.SizeX);
            int d = Math.Max(1, plan.SizeZ);

            int reach = village.ClaimRadius - FFConfig.Current.Plots.ClaimEdgeMarginBlocks - Math.Max(w, d);
            if (reach < 2) return null;

            BlockPos best = null;
            float bestScore = float.MinValue;

            for (int attempt = 0; attempt < cfg.SiteAttempts; attempt++)
            {
                int x = village.CentreX + sapi.World.Rand.Next(-reach, reach + 1);
                int z = village.CentreZ + sapi.World.Rand.Next(-reach, reach + 1);

                if (Overlapping(village, x, z, x + w - 1, z + d - 1) != null) continue;
                if (OverlapsASite(village, x, z, w, d)) continue;

                // The plots code has always guarded these and this did not, so a village
                // could put a cottage on top of its own storehouse.
                if (CoversAFixture(village, x, z, x + w - 1, z + d - 1)) continue;

                int minY = int.MaxValue, maxY = int.MinValue;
                bool usable = true;

                // The corners and the middle. A house only has to sit on its own footprint,
                // so there is no point sampling it like a field.
                foreach ((int sx, int sz) in Corners(x, z, w, d))
                {
                    int y = GroundAt(sx, sz);
                    if (y <= 1) { usable = false; break; }

                    Block ground = sapi.World.BlockAccessor.GetBlock(new BlockPos(sx, y - 1, sz, 0));
                    Block at = sapi.World.BlockAccessor.GetBlock(new BlockPos(sx, y, sz, 0));
                    if (ground?.IsLiquid() == true || at?.IsLiquid() == true) { usable = false; break; }

                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

                if (!usable) continue;
                if (maxY - minY > cfg.FlatnessTolerance) continue;

                // Near the centre and flat. A village grows outward from its square, so
                // wanting to be close to it is most of what makes a settlement read as one
                // place rather than scattered huts.
                double dx = x - village.CentreX, dz = z - village.CentreZ;
                float score = -(float)Math.Sqrt(dx * dx + dz * dz) - (maxY - minY) * 2f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new BlockPos(x, minY, z, 0);
                }
            }

            return best;
        }

        private static IEnumerable<(int, int)> Corners(int x, int z, int w, int d)
        {
            yield return (x, z);
            yield return (x + w - 1, z);
            yield return (x, z + d - 1);
            yield return (x + w - 1, z + d - 1);
            yield return (x + w / 2, z + d / 2);
        }

        private bool OverlapsASite(Village village, int x, int z, int w, int d)
        {
            foreach (VillageBuildSite s in village.BuildSites)
            {
                if (s.State == EnumBuildState.Abandoned) continue;

                BuildingPlan other = Buildings?.Get(s.PlanCode);
                int ow = Math.Max(1, other?.SizeX ?? 1);
                int od = Math.Max(1, other?.SizeZ ?? 1);

                int margin = FFConfig.Current.Build.SeparationBlocks;
                bool clear = x + w - 1 + margin < s.X
                          || s.X + ow - 1 + margin < x
                          || z + d - 1 + margin < s.Z
                          || s.Z + od - 1 + margin < z;

                if (!clear) return true;
            }
            return false;
        }
    }
}
