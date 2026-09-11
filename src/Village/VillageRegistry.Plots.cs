using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Plots: choosing where they go, who works them, and when they are finished.
    ///
    /// Siting is the interesting part. A village has to pick its own ground, and the
    /// difference between a settlement that looks planned and one that looks scattered
    /// is almost entirely in this file. The rules are deliberately few and readable:
    /// stay inside the claim, stay off the town square, stay off other plots, prefer
    /// flat, and prefer ground that already has what the plot is for. A woodlot wants
    /// trees on it, a quarry wants rock, and a village that sites a clay pit on granite
    /// is a village nobody believes in.
    /// </summary>
    public partial class VillageRegistry
    {
        // --- creating and removing --------------------------------------------------

        /// <summary>
        /// Sites a plot of this kind somewhere sensible in the claim.
        /// Returns null with a reason if there is nowhere to put it, which is a real
        /// answer: a village hemmed in by water genuinely cannot have a field.
        /// </summary>
        public VillagePlot SitePlot(Village village, EnumPlotKind kind, out string error)
        {
            error = null;
            if (village == null) { error = "No village."; return null; }

            var cfg = FFConfig.Current.Plots;

            if (CountPlots(village) >= MaxPlotsFor(village))
            {
                error = "A tier " + village.Tier + " village may hold "
                      + MaxPlotsFor(village) + " plots and already has that many.";
                return null;
            }

            int half = HalfSizeFor(kind);
            BlockPos site = FindSite(village, kind, half);

            if (site == null)
            {
                error = "Found no ground in the claim flat and clear enough for a "
                      + kind.ToString().ToLowerInvariant() + ".";
                return null;
            }

            return PlacePlot(village, kind, site, half);
        }

        /// <summary>
        /// Creates a plot at a spot somebody has already chosen, without asking whether
        /// it is a good one. This is what the test command uses, and what a hand-placed
        /// plot would use later. Still refuses to overlap another plot, because that is
        /// not a matter of taste.
        /// </summary>
        public VillagePlot PlacePlot(Village village, EnumPlotKind kind, BlockPos centre, int half, bool force = false)
        {
            if (village == null || centre == null) return null;

            int minX = centre.X - half, maxX = centre.X + half;
            int minZ = centre.Z - half, maxZ = centre.Z + half;

            if (!force && Overlapping(village, minX, minZ, maxX, maxZ) != null) return null;

            // The height map returns zero for a chunk that is not loaded, which is not
            // the same as ground at bedrock. A plot that records a floor of zero is a
            // terrace whose digger will happily excavate the entire column, so refuse to
            // create one rather than persist a number that means "do not know".
            int ground = GroundAt(centre.X, centre.Z);
            if (ground <= 1)
            {
                sapi.Logger.Warning(
                    "[F&F] Refused to site a {0} at {1}: no ground there, or the chunk is not loaded.",
                    kind, centre);
                return null;
            }

            var plot = new VillagePlot
            {
                Id = village.NextPlotId++,
                Kind = kind,
                State = EnumPlotState.Planned,
                MinX = minX,
                MinZ = minZ,
                MaxX = maxX,
                MaxZ = maxZ,
                Y = ground,
                Tier = 0,
                WorkerCap = WorkerCapFor(kind),
                CreatedTotalDays = sapi.World.Calendar.TotalDays,
                LastWorkedTotalDays = sapi.World.Calendar.TotalDays
            };

            village.Plots.Add(plot);
            RefreshShownPlots(village.Id);
            return plot;
        }

        public bool RemovePlot(Village village, int plotId)
        {
            if (village == null) return false;
            VillagePlot plot = PlotById(village, plotId);
            if (plot == null) return false;

            // Whoever was working it needs telling, or they walk to a field that no
            // longer exists and stand in it.
            foreach (long id in plot.WorkerIds)
            {
                if (sapi.World.GetEntityById(id) is FFVillager v) v.ClearPlot();
            }

            village.Plots.Remove(plot);
            RefreshShownPlots(village.Id);
            return true;
        }

        // --- lookups ----------------------------------------------------------------

        public VillagePlot PlotById(Village village, int plotId)
        {
            if (village?.Plots == null) return null;
            foreach (VillagePlot p in village.Plots) if (p.Id == plotId) return p;
            return null;
        }

        public static VillagePlot PlotAt(Village village, BlockPos pos)
        {
            if (village?.Plots == null || pos == null) return null;
            foreach (VillagePlot p in village.Plots) if (p.Contains(pos)) return p;
            return null;
        }

        public static int CountPlots(Village village, EnumPlotKind kind)
        {
            if (village?.Plots == null) return 0;
            int n = 0;
            foreach (VillagePlot p in village.Plots)
            {
                if (p.Kind == kind && p.IsWorkable) n++;
            }
            return n;
        }

        public static int CountPlots(Village village)
        {
            if (village?.Plots == null) return 0;
            int n = 0;
            foreach (VillagePlot p in village.Plots) if (p.IsWorkable) n++;
            return n;
        }

        public static int MaxPlotsFor(Village village)
        {
            int[] byTier = FFConfig.Current.Plots.MaxPlotsByTier;
            if (byTier == null || byTier.Length == 0) return 4;
            return byTier[GameMath.Clamp(village.Tier, 0, byTier.Length - 1)];
        }

        public static int HalfSizeFor(EnumPlotKind kind)
        {
            int[] sizes = FFConfig.Current.Plots.HalfSizeByKind;
            int i = (int)kind;
            if (sizes == null || i >= sizes.Length) return 6;
            return Math.Max(1, sizes[i]);
        }

        public static int WorkerCapFor(EnumPlotKind kind)
        {
            int[] caps = FFConfig.Current.Plots.WorkerCapByKind;
            int i = (int)kind;
            if (caps == null || i >= caps.Length) return 1;
            return Math.Max(1, caps[i]);
        }

        private static VillagePlot Overlapping(Village village, int minX, int minZ, int maxX, int maxZ)
        {
            int margin = FFConfig.Current.Plots.SeparationBlocks;
            foreach (VillagePlot p in village.Plots)
            {
                if (p.State == EnumPlotState.Abandoned) continue;
                if (p.OverlapsWithMargin(minX, minZ, maxX, maxZ, margin)) return p;
            }
            return null;
        }

        // --- who works where --------------------------------------------------------

        /// <summary>
        /// Finds this villager a plot of the kind their trade works, siting a new one if
        /// the village is allowed another and none has room.
        ///
        /// Returns null when the village genuinely has nowhere for them, which is a
        /// pressure the design wants visible: a lumberjack with no woodlot should be
        /// standing about looking unemployed, not quietly felling somebody's garden.
        /// </summary>
        public VillagePlot ClaimPlot(Village village, FFVillager villager, EnumPlotKind kind)
        {
            if (village == null || villager == null) return null;

            // Already assigned somewhere valid.
            VillagePlot held = PlotOf(village, villager.EntityId);
            if (held != null && held.Kind == kind && held.IsWorkable) return held;
            if (held != null) ReleasePlot(village, villager.EntityId);

            VillagePlot best = null;
            foreach (VillagePlot p in village.Plots)
            {
                if (p.Kind != kind || !p.HasRoom) continue;
                if (best == null || p.WorkerIds.Count < best.WorkerIds.Count) best = p;
            }

            if (best == null)
            {
                best = SitePlot(village, kind, out string _);
                if (best == null) return null;
            }

            best.WorkerIds.Add(villager.EntityId);
            if (best.State == EnumPlotState.Planned) best.State = EnumPlotState.Active;
            villager.SetPlot(best.Id);
            return best;
        }

        public VillagePlot PlotOf(Village village, long entityId)
        {
            if (village?.Plots == null) return null;
            foreach (VillagePlot p in village.Plots)
            {
                if (p.WorkerIds.Contains(entityId)) return p;
            }
            return null;
        }

        public void ReleasePlot(Village village, long entityId)
        {
            if (village?.Plots == null) return;
            foreach (VillagePlot p in village.Plots) p.WorkerIds.Remove(entityId);
            if (sapi.World.GetEntityById(entityId) is FFVillager v) v.ClearPlot();
        }

        /// <summary>
        /// Records that work happened here, both for the idle check and so a plot's worth
        /// to the village is a measured figure rather than a guess.
        /// </summary>
        public void NotePlotWorked(Village village, VillagePlot plot, float yield)
        {
            if (village == null || plot == null) return;
            plot.LastWorkedTotalDays = sapi.World.Calendar.TotalDays;
            plot.LifetimeYield += yield;
            if (plot.State == EnumPlotState.Planned) plot.State = EnumPlotState.Active;
        }

        /// <summary>
        /// Called once a day per village. A plot nobody has touched in a long while is
        /// marked exhausted rather than sitting on the books pretending to be a going
        /// concern, which is what lets the village site a replacement somewhere better.
        /// </summary>
        public void AgePlots(Village village)
        {
            if (village?.Plots == null) return;

            int idle = FFConfig.Current.Plots.IdleDaysBeforeExhausted;
            double now = sapi.World.Calendar.TotalDays;

            foreach (VillagePlot p in village.Plots)
            {
                if (p.State != EnumPlotState.Active) continue;
                if (p.WorkerIds.Count > 0) continue;
                if (now - p.LastWorkedTotalDays < idle) continue;

                p.State = EnumPlotState.Exhausted;
            }

            // Reconcile the roster: an entity that no longer exists is not a worker.
            foreach (VillagePlot p in village.Plots)
            {
                for (int i = p.WorkerIds.Count - 1; i >= 0; i--)
                {
                    if (sapi.World.GetEntityById(p.WorkerIds[i]) is FFVillager) continue;
                    if (village.MemberIds.Contains(p.WorkerIds[i])) continue;   // just unloaded
                    p.WorkerIds.RemoveAt(i);
                }
            }
        }

        // --- siting -----------------------------------------------------------------

        /// <summary>
        /// Picks the best of a handful of random candidates rather than searching the
        /// whole claim. A claim can be a hundred blocks on a side, which is twenty
        /// thousand columns, and a village that takes the best of sixty tries looks
        /// exactly as deliberate as one that took the best of twenty thousand.
        /// </summary>
        private BlockPos FindSite(Village village, EnumPlotKind kind, int half)
        {
            var cfg = FFConfig.Current.Plots;
            int reach = village.ClaimRadius - cfg.ClaimEdgeMarginBlocks - half;
            if (reach < 2) return null;

            BlockPos best = null;
            float bestScore = float.MinValue;

            for (int attempt = 0; attempt < cfg.SiteAttempts; attempt++)
            {
                int x = village.CentreX + sapi.World.Rand.Next(-reach, reach + 1);
                int z = village.CentreZ + sapi.World.Rand.Next(-reach, reach + 1);

                // Off the town square.
                int dx = x - village.CentreX, dz = z - village.CentreZ;
                if (dx * dx + dz * dz < cfg.CentreClearanceBlocks * cfg.CentreClearanceBlocks) continue;

                if (Overlapping(village, x - half, z - half, x + half, z + half) != null) continue;
                if (CoversAFixture(village, x - half, z - half, x + half, z + half)) continue;

                float score = ScoreSite(kind, x, z, half, out bool usable);
                if (!usable) continue;

                // Closer to home is better, all else equal. Nobody wants to walk.
                score -= (float)Math.Sqrt(dx * dx + dz * dz) * 0.02f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new BlockPos(x, GroundAt(x, z), z, 0);
                }
            }

            return best;
        }

        /// <summary>
        /// How good this ground is for this kind of plot, and whether it is usable at all.
        ///
        /// Usable is a hard test: flat enough, dry enough, real ground. The score on top
        /// is a preference, and it is what makes a woodlot land in the trees.
        /// </summary>
        private float ScoreSite(EnumPlotKind kind, int cx, int cz, int half, out bool usable)
        {
            usable = false;
            var cfg = FFConfig.Current.Plots;
            IBlockAccessor ba = sapi.World.BlockAccessor;

            int samples = Math.Max(5, cfg.SampleColumns);
            int step = Math.Max(1, (half * 2 + 1) / (int)Math.Sqrt(samples));

            int minY = int.MaxValue, maxY = int.MinValue;
            int wet = 0, counted = 0, wanted = 0;

            for (int x = cx - half; x <= cx + half; x += step)
            {
                for (int z = cz - half; z <= cz + half; z += step)
                {
                    int y = GroundAt(x, z);
                    if (y <= 0) { usable = false; return 0; }

                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    counted++;

                    // y is the topmost SOLID block, not the first air block above it, so
                    // the ground is at y and whatever stands on it is at y+1. Reading
                    // y-1 and y was off by one the whole time: it asked whether the block
                    // under the soil was soil, and looked for a tree trunk in the dirt.
                    // Woodlots and pastures were being scored on the wrong layer.
                    Block ground = ba.GetBlock(new BlockPos(x, y, z, 0));
                    Block above = ba.GetBlock(new BlockPos(x, y + 1, z, 0));

                    if (ground?.IsLiquid() == true || above?.IsLiquid() == true) wet++;
                    if (WantsThisGround(kind, ground, above)) wanted++;
                }
            }

            if (counted == 0) return 0;
            if (maxY - minY > cfg.FlatnessTolerance) return 0;

            // A quarter of the plot underwater is a lake, whatever the height map says.
            if (wet * 4 > counted) return 0;

            usable = true;

            float flatness = 1f - (maxY - minY) / (float)Math.Max(1, cfg.FlatnessTolerance);
            float suitability = wanted / (float)counted;

            // Suitability outweighs flatness on purpose. A woodlot on gently rolling
            // ground full of trees beats a billiard table with nothing growing on it.
            return suitability * 3f + flatness;
        }

        /// <summary>What each kind of plot hopes to find already on the ground.</summary>
        private static bool WantsThisGround(EnumPlotKind kind, Block ground, Block above)
        {
            string g = ground?.Code?.Path ?? "";
            string a = above?.Code?.Path ?? "";

            switch (kind)
            {
                case EnumPlotKind.Woodlot:
                    // Trees, or the ground trees grow in. Both count: a village plants.
                    return a.Contains("log") || a.Contains("sapling") || a.Contains("leaves")
                        || g.StartsWith("soil") || g.StartsWith("forestfloor");

                case EnumPlotKind.Field:
                case EnumPlotKind.Terrace:
                    return g.StartsWith("soil") || g.StartsWith("farmland") || g.StartsWith("forestfloor");

                case EnumPlotKind.Pasture:
                    return g.StartsWith("soil") && (a == "" || a.Contains("grass") || a.Contains("tallgrass"));

                case EnumPlotKind.Quarry:
                case EnumPlotKind.MineHead:
                    return g.StartsWith("rock") || g.StartsWith("stone") || g.Contains("gravel");

                case EnumPlotKind.ClayPit:
                    return g.Contains("clay") || g.StartsWith("sand") || g.Contains("peat");

                default:
                    return true;
            }
        }

        /// <summary>Keeps plots off the cairn and the storehouse.</summary>
        private static bool CoversAFixture(Village village, int minX, int minZ, int maxX, int maxZ)
        {
            if (village.HasMarker
                && village.MarkerX >= minX && village.MarkerX <= maxX
                && village.MarkerZ >= minZ && village.MarkerZ <= maxZ) return true;

            if (village.HasStorehouse
                && village.StorehouseX >= minX && village.StorehouseX <= maxX
                && village.StorehouseZ >= minZ && village.StorehouseZ <= maxZ) return true;

            return false;
        }

        private int GroundAt(int x, int z)
            => sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));

        // --- outlines ---------------------------------------------------------------

        /// <summary>Its own highlight slot, so a plot outline and a claim outline coexist.</summary>
        private const int PlotHighlightSlot = 1702;

        private readonly Dictionary<string, long> plotsShownTo = new Dictionary<string, long>();

        /// <summary>
        /// Draws every plot in a village at once, each kind in its own colour. One call
        /// rather than one per plot because a highlight slot is replaced wholesale, so
        /// drawing them one at a time would only ever show the last one.
        /// </summary>
        public void ShowPlots(IServerPlayer player, Village village)
        {
            if (player == null || village == null) return;

            var blocks = new List<BlockPos>();
            var colours = new List<int>();

            foreach (VillagePlot plot in village.Plots)
            {
                if (plot.State == EnumPlotState.Abandoned) continue;
                int colour = ColourFor(plot);

                for (int x = plot.MinX; x <= plot.MaxX; x++)
                {
                    AddPlotOutlineBlock(blocks, colours, x, plot.MinZ, colour);
                    AddPlotOutlineBlock(blocks, colours, x, plot.MaxZ, colour);
                }
                for (int z = plot.MinZ + 1; z < plot.MaxZ; z++)
                {
                    AddPlotOutlineBlock(blocks, colours, plot.MinX, z, colour);
                    AddPlotOutlineBlock(blocks, colours, plot.MaxX, z, colour);
                }
            }

            sapi.World.HighlightBlocks(player, PlotHighlightSlot, blocks, colours);
            plotsShownTo[player.PlayerUID] = village.Id;
        }

        public void HidePlots(IServerPlayer player)
        {
            if (player == null) return;
            sapi.World.HighlightBlocks(player, PlotHighlightSlot, new List<BlockPos>());
            plotsShownTo.Remove(player.PlayerUID);
        }

        private void RefreshShownPlots(long villageId)
        {
            Village v = Get(villageId);
            if (v == null) return;

            var watchers = new List<string>();
            foreach (var kv in plotsShownTo) if (kv.Value == villageId) watchers.Add(kv.Key);

            foreach (string uid in watchers)
            {
                if (sapi.World.PlayerByUid(uid) is IServerPlayer p) ShowPlots(p, v);
            }
        }

        private void HidePlotsEverywhere(long villageId)
        {
            var stale = new List<string>();
            foreach (var kv in plotsShownTo) if (kv.Value == villageId) stale.Add(kv.Key);

            foreach (string uid in stale)
            {
                if (sapi.World.PlayerByUid(uid) is IServerPlayer p)
                {
                    sapi.World.HighlightBlocks(p, PlotHighlightSlot, new List<BlockPos>());
                }
                plotsShownTo.Remove(uid);
            }
        }

        private void AddPlotOutlineBlock(List<BlockPos> into, List<int> colours, int x, int z, int colour)
        {
            int y = GroundAt(x, z);
            into.Add(new BlockPos(x, y, z, 0));
            colours.Add(colour);
        }

        /// <summary>
        /// A colour per kind, so a glance at a village says what its ground is for.
        /// Exhausted plots go grey whatever they were.
        /// </summary>
        private static int ColourFor(VillagePlot plot)
        {
            if (plot.State == EnumPlotState.Exhausted) return ColorUtil.ToRgba(110, 130, 130, 130);

            switch (plot.Kind)
            {
                case EnumPlotKind.Woodlot: return ColorUtil.ToRgba(120, 60, 160, 60);
                case EnumPlotKind.Field: return ColorUtil.ToRgba(120, 70, 200, 220);
                case EnumPlotKind.Pasture: return ColorUtil.ToRgba(120, 90, 210, 140);
                case EnumPlotKind.Quarry: return ColorUtil.ToRgba(120, 160, 160, 170);
                case EnumPlotKind.ClayPit: return ColorUtil.ToRgba(120, 70, 120, 200);
                case EnumPlotKind.MineHead: return ColorUtil.ToRgba(120, 40, 40, 60);
                case EnumPlotKind.Terrace: return ColorUtil.ToRgba(120, 60, 140, 190);
                default: return ColorUtil.ToRgba(120, 200, 200, 200);
            }
        }
    }
}
