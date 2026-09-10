using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// The shape every producing job has: find something to work on, walk to it, work at
    /// it for a while, carry what comes off it, and take that home.
    ///
    /// Every job in the mod is that loop with three questions answered differently: what
    /// counts as a target, what happens when you get there, and what to do afterwards. So
    /// this class owns the loop and the subclasses answer the questions. The alternative,
    /// which is four jobs each with their own travel and hauling code, is four places for
    /// the same bug to live.
    ///
    /// The loop is a small state machine rather than a chain of nested callbacks because
    /// it has to survive being interrupted at any point. A villager can be attacked, sent
    /// to bed, or dragged home by the tether in the middle of any state, and when the job
    /// starts again it has to pick up somewhere sensible rather than from the beginning.
    ///
    /// Two rules it inherits from the rest of the mod, both learned the hard way:
    ///  - Walking belongs to ffgoto. This task asks for a journey and then leaves it
    ///    alone. It never cancels a walk on being stopped, only when its own job is done.
    ///  - Report outcome, not intent. Every failure path counts something or logs
    ///    something, so a job that quietly does nothing shows up as a number rather than
    ///    as a villager standing in a field.
    /// </summary>
    public abstract class AiTaskVillagerWork : FFTaskBase
    {
        protected enum EnumWorkStep
        {
            /// <summary>Looking for something in the plot worth doing.</summary>
            Choosing = 0,

            /// <summary>Walking to it.</summary>
            Travelling = 1,

            /// <summary>Standing at it, working.</summary>
            Working = 2,

            /// <summary>Walking to the storehouse with full hands.</summary>
            Hauling = 3
        }

        /// <summary>Which trade does this job. Anyone else is not asked.</summary>
        protected abstract EnumTrade Trade { get; }

        /// <summary>Which kind of plot this job needs. The village sites one if it can.</summary>
        protected abstract EnumPlotKind PlotKind { get; }

        /// <summary>
        /// Whether this job works a plot at all.
        ///
        /// Most do: a lumberjack without a woodlot should be idle, not loose in the
        /// forest. The trickle jobs are the exception. A forager picking berries is
        /// working the whole claim by definition, and giving them a rectangle to stand in
        /// would turn the one job that keeps a badly sited village alive into another
        /// thing that needs ground the village has not got.
        /// </summary>
        protected virtual bool NeedsPlot => true;

        /// <summary>
        /// Whether this block is worth working on. Called for candidates inside the plot,
        /// so it does not need to check bounds, only substance.
        /// </summary>
        protected abstract bool IsTarget(Block block, BlockPos pos);

        /// <summary>
        /// Do the work on one block. Return true if something actually happened, which is
        /// what decides whether the plot gets credited with a day's use.
        ///
        /// Whatever comes off it should go into the villager's hands via Harvest, not
        /// straight into the ledger, because a village should only own what somebody
        /// carried home.
        /// </summary>
        protected abstract bool Work(BlockPos pos);

        /// <summary>Optional: put something back, once the block has been worked.</summary>
        protected virtual void AfterWork(BlockPos pos) { }

        /// <summary>
        /// Whether this job has unfinished business at a position whose block no longer
        /// looks like a target.
        ///
        /// Almost every job says no: the block is gone, so the work is done. The
        /// exception is work that takes down more than the block it started on.
        /// </summary>
        protected virtual bool StillBusyAt(BlockPos pos) => false;

        /// <summary>
        /// How far out from the plot's recorded ground level to look. Most jobs work at
        /// the surface; a quarry cuts down into it.
        /// </summary>
        protected virtual int VerticalSearch => 6;

        protected override float ThinkIntervalSec => 1.5f;

        /// <summary>
        /// Villages must keep producing whether or not a player is nearby, or a village
        /// only ever grows while it is being watched, which is the exact failure the whole
        /// design exists to avoid.
        /// </summary>
        protected override float ObservedRangeBlocks => -1f;

        protected EnumWorkStep Step { get; private set; }
        protected BlockPos Target { get; private set; }
        protected VillagePlot Plot { get; private set; }

        private double stepStartedAt;
        private double nextActionAt;
        private double nextPlotAttemptAt;
        private float workedThisVisit;
        private int failuresHere;
        private int idleStrikes;

        /// <summary>How long to leave it before trying to site a plot again after a failure.</summary>
        private const double PlotRetrySeconds = 30;

        /// <summary>
        /// Blocks not to pick again for a while, and when they come back into play.
        ///
        /// Without this the loop livelocks, because the search always returns the nearest
        /// match and nothing remembers that the nearest match was just given up on. A
        /// villager who cannot reach a block, or who reaches it and finds there is
        /// nothing to be done with it after all, would walk to it again immediately and
        /// keep doing so until the world changed.
        ///
        /// A timed skip rather than a permanent one, because the reasons are all
        /// temporary: a path opens up, a nest box empties, another villager moves.
        /// </summary>
        private readonly Dictionary<BlockPos, double> skipUntil = new Dictionary<BlockPos, double>();

        private const double SkipUnreachableSec = 90;
        private const double SkipUnworkableSec = 45;

        /// <summary>
        /// Where the last scan stopped, so the next one carries on rather than starting
        /// from the same corner and running out of budget in the same place.
        /// </summary>
        private int scanCursorX = int.MinValue;

        /// <summary>
        /// Times round the loop finding nothing before the worker lets the plot go.
        ///
        /// Letting go matters: the day clock only retires a plot that has no workers on
        /// it, so a lumberjack who sits on a felled-out woodlot forever keeps it on the
        /// books and stops the village siting a replacement.
        /// </summary>
        private const int IdleStrikesBeforeReleasing = 4;

        /// <summary>How long to stand down for after finding nothing to do.</summary>
        private const double IdleBackoffSec = 20;

        protected ICoreServerAPI Sapi => entity.Api as ICoreServerAPI;

        protected VillageRegistry Registry => Sapi?.ModLoader.GetModSystem<VillageRegistry>();

        protected Village Home => Registry?.Get(Villager?.VillageId ?? 0);

        protected AiTaskVillagerWork(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        // --- deciding whether to work at all ----------------------------------------

        protected override bool ShouldRun()
        {
            if (Villager == null || Villager.Trade != Trade) return false;
            if (Villager.VillageId == 0) return false;

            // Somebody with fuller hands than this is already moving them.
            if (Villager.GotoTarget != null && Step == EnumWorkStep.Choosing) return false;

            if (VillageSchedule.PhaseFor(entity.Api, entity.Pos.AsBlockPos) != EnumDayPhase.Work) return false;

            Village village = Home;
            if (village == null) return false;

            // Not while they are lost.
            //
            // This is the fix for the tether never working. The AI manager starts tasks in
            // the order the entity file lists them and re-reads the slot each time, so a
            // job listed after the tether preempts it on the same tick it starts, before
            // its retry logic has run once. It restarts, gets preempted again, and the
            // villager stands in a field forever with the log filling up.
            //
            // Priority alone cannot settle it, because a job genuinely should outrank
            // going home during working hours. What settles it is that a villager outside
            // their own claim has no business working: whatever they are standing next to
            // is not the village's to take. So the jobs stand aside and the tether gets
            // its slot.
            if (!AiTaskVillagerReturnHome.WithinTether(village, entity.Pos.AsBlockPos)) return false;

            // Carrying a full load is itself a reason to run: the job is not finished
            // until it is in the storehouse.
            if (Villager.CarriedCount >= HaulThreshold) return true;

            if (!NeedsPlot) return true;

            // Already have somewhere to work: cheap, and the common case.
            if (Registry.PlotOf(village, entity.EntityId) is VillagePlot held && held.IsWorkable) return true;

            // Siting a new plot scans a good deal of world, so a village with nowhere
            // left to put a woodlot must not try again every second. Failing to find
            // ground is a slow-changing fact, so it is fine to believe it for a while.
            if (Now < nextPlotAttemptAt) return false;
            nextPlotAttemptAt = Now + PlotRetrySeconds;

            return Registry.ClaimPlot(village, Villager, PlotKind) != null;
        }

        protected override void OnStart()
        {
            Village village = Home;
            Plot = village == null ? null : Registry.PlotOf(village, entity.EntityId);

            stepStartedAt = Now;
            failuresHere = 0;
            workedThisVisit = 0;

            // A villager who came back with full hands finishes the delivery first.
            Step = Villager.CarriedCount >= HaulThreshold ? EnumWorkStep.Hauling : EnumWorkStep.Choosing;
        }

        protected override bool OnTick(float dt)
        {
            if (Villager == null) return false;

            Village village = Home;
            if (village == null) return false;

            switch (Step)
            {
                case EnumWorkStep.Choosing: return TickChoosing(village);
                case EnumWorkStep.Travelling: return TickTravelling();
                case EnumWorkStep.Working: return TickWorking(village);
                case EnumWorkStep.Hauling: return TickHauling(village);
                default: return false;
            }
        }

        protected override void OnStop(bool cancelled)
        {
            // The walk belongs to ffgoto. Cancelling it here would tear up a journey that
            // the next tick of this same job is going to want, which is exactly the bug
            // that kept villagers from ever reaching their beds.
            //
            // The step does get reset, though. It is what ShouldRun reads to decide
            // whether somebody else is already moving this villager, and leaving it on
            // whatever the last run happened to end with meant that guard was answering a
            // question about a run that finished minutes ago.
            Step = Villager != null && Villager.CarriedCount >= HaulThreshold
                ? EnumWorkStep.Hauling
                : EnumWorkStep.Choosing;
        }

        // --- the steps ---------------------------------------------------------------

        private bool TickChoosing(Village village)
        {
            // A short breath between blocks. Without it a villager works at exactly the
            // rate the timer allows and reads as a machine rather than a person.
            if (Now < nextActionAt) return true;

            if (NeedsPlot)
            {
                Plot = Registry.PlotOf(village, entity.EntityId)
                    ?? Registry.ClaimPlot(village, Villager, PlotKind);

                if (Plot == null)
                {
                    Note("has no " + PlotKind.ToString().ToLowerInvariant() + " to work");
                    return false;
                }
            }

            BlockPos found = FindWork(village, Plot);
            if (found == null)
            {
                // Nothing to do here today. Not a failure, and not worth walking to the
                // middle of an empty field to discover again in ten seconds. Back off
                // hard, because searching costs real work and finding nothing twice in a
                // row means the answer is unlikely to change in the next second.
                nextActionAt = Now + IdleBackoffSec;
                idleStrikes++;

                OnNothingToDo(village, Plot);

                if (Plot != null && idleStrikes >= IdleStrikesBeforeReleasing)
                {
                    entity.Api.Logger.Notification(
                        "[F&F] {0} has run out of work in plot #{1} and is giving it up.",
                        Label(), Plot.Id);
                    Registry.ReleasePlot(village, entity.EntityId);
                    Plot = null;
                    idleStrikes = 0;
                    nextPlotAttemptAt = Now + PlotRetrySeconds;
                }

                return false;
            }

            idleStrikes = 0;
            Target = found;
            Step = EnumWorkStep.Travelling;
            stepStartedAt = Now;
            Villager.OrderGoto(Target, MoveSpeeds.Walk);
            return true;
        }

        private bool TickTravelling()
        {
            if (Target == null) { Step = EnumWorkStep.Choosing; return true; }

            if (WithinReach(Target))
            {
                Villager.CancelGoto();
                Step = EnumWorkStep.Working;
                stepStartedAt = Now;
                nextActionAt = Now + WorkTimeSec;
                FaceTarget();
                return true;
            }

            if (Now - stepStartedAt > FFConfig.Current.Work.GiveUpAfterSec)
            {
                Villager.CancelGoto();
                Note("could not reach " + Target);
                DevStats.Bump(DevStats.PathsFailed);
                Skip(Target, SkipUnreachableSec);
                Target = null;
                Step = EnumWorkStep.Choosing;
                return true;
            }

            // The walk ended without arriving. Ask again rather than standing here: a
            // single refused path used to leave a villager idle for the whole timeout.
            if (Villager.GotoTarget == null)
            {
                failuresHere++;
                if (failuresHere > 3)
                {
                    Note("gave up on " + Target + " after " + failuresHere + " tries");
                    Skip(Target, SkipUnreachableSec);
                    Target = null;
                    failuresHere = 0;
                    Step = EnumWorkStep.Choosing;
                    return true;
                }
                Villager.OrderGoto(Target, MoveSpeeds.Walk);
            }

            return true;
        }

        private bool TickWorking(Village village)
        {
            if (Target == null) { Step = EnumWorkStep.Choosing; return true; }

            // The world moved: somebody else took it, or it burned down.
            //
            // Unless the job says it is still busy here. A lumberjack cuts the base of a
            // trunk first and the rest of the tree comes down after it, so the block that
            // was the target is gone while the work very much is not.
            Block block = entity.World.BlockAccessor.GetBlock(Target);
            if (!IsTarget(block, Target) && !StillBusyAt(Target))
            {
                Target = null;
                Step = EnumWorkStep.Choosing;
                return true;
            }

            if (!WithinReach(Target))
            {
                Step = EnumWorkStep.Travelling;
                stepStartedAt = Now;
                Villager.OrderGoto(Target, MoveSpeeds.Walk);
                return true;
            }

            // A villager has one pair of hands, so a stack of logs and a stack of soil
            // cannot both be in them. Rather than break the block and watch the drop fall
            // on the floor, take what is already held home first and come back. The
            // target is kept, so nothing is lost by the detour.
            if (Villager.IsCarrying && !HandsCanTake(block, Target))
            {
                Step = EnumWorkStep.Hauling;
                stepStartedAt = Now;
                return true;
            }

            if (Now < nextActionAt) { SwingAnimation(); return true; }

            BlockPos done = Target;
            bool did = Work(done);
            if (did)
            {
                workedThisVisit++;
                AfterWork(done);
                Registry.NotePlotWorked(village, Plot, 1f);
            }
            else
            {
                // It looked like work and turned out not to be. Leave it alone for a
                // while or it stays the nearest target forever and nothing else in the
                // plot ever gets touched.
                Skip(done, SkipUnworkableSec);
            }

            nextActionAt = Now + FFConfig.Current.Work.BetweenBlocksSec;

            // Unfinished business here means stay here.
            //
            // Clearing the target unconditionally was how a lumberjack halfway through a
            // large tree wandered off to the next one and carried on felling the first
            // one from across the woodlot, dropping its logs at the old stump and
            // planting the sapling there too. StillBusyAt is what the guard at the top of
            // this method reads, and it never got the chance to read it.
            if (did && StillBusyAt(done))
            {
                Target = done;
                return true;
            }

            Target = null;
            Step = Villager.CarriedCount >= HaulThreshold ? EnumWorkStep.Hauling : EnumWorkStep.Choosing;
            if (Step == EnumWorkStep.Hauling) stepStartedAt = Now;
            return true;
        }

        private bool TickHauling(Village village)
        {
            if (!Villager.IsCarrying) { Step = EnumWorkStep.Choosing; return true; }

            BlockPos drop = StorehousePos(village);
            if (drop == null)
            {
                Note("has nowhere to put " + Villager.CarriedCount + " items");
                return false;
            }

            if (WithinReach(drop, 3.0))
            {
                Villager.CancelGoto();
                if (Registry.DepositCarried(Villager, out string outcome))
                {
                    entity.Api.Logger.VerboseDebug("[F&F] {0} delivered {1}", Label(), outcome);
                }
                else
                {
                    // The village cannot use it. Put it down at the storehouse door
                    // rather than keeping hold of it.
                    //
                    // This branch is the one that used to wedge a villager forever: full
                    // hands are themselves a reason to run, so a worker holding something
                    // nothing would accept came back here every second for the rest of
                    // the save. Whatever ends up in their hands, they must always be able
                    // to empty them.
                    ItemStack unwanted = Villager.TakeCarried();
                    if (unwanted != null)
                    {
                        entity.World.SpawnItemEntity(unwanted, drop.ToVec3d().Add(0.5, 1, 0.5));
                    }

                    Note("put down " + (unwanted?.GetName() ?? "something") + ": " + outcome);
                    Step = EnumWorkStep.Choosing;
                    return true;
                }

                Step = EnumWorkStep.Choosing;
                stepStartedAt = Now;
                return true;
            }

            if (Now - stepStartedAt > FFConfig.Current.Work.GiveUpAfterSec)
            {
                Note("could not get home to the storehouse");
                Villager.CancelGoto();
                return false;
            }

            if (Villager.GotoTarget == null) Villager.OrderGoto(drop, MoveSpeeds.Laden);
            return true;
        }

        /// <summary>
        /// What to do when the plot has nothing left worth working.
        ///
        /// The default is to say so and stop. A job with something else to offer, like a
        /// lumberjack who can plant where he has felled, overrides this.
        /// </summary>
        protected virtual void OnNothingToDo(Village village, VillagePlot plot)
        {
            entity.Api.Logger.VerboseDebug(
                "[F&F] {0} found nothing to do in {1}",
                Label(), plot == null ? "the claim" : "plot #" + plot.Id);
        }

        // --- finding something to work on --------------------------------------------

        /// <summary>
        /// Nearest first, so a villager works outward from where they are standing rather
        /// than crossing the plot for every block. Scanning is capped: a big plot is
        /// thousands of columns and this runs on the server tick.
        /// </summary>
        protected virtual BlockPos FindWork(Village village, VillagePlot plot)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;
            int slack = FFConfig.Current.Work.SearchSlackBlocks;

            int minX, minZ, maxX, maxZ;
            if (plot != null)
            {
                minX = plot.MinX - slack; maxX = plot.MaxX + slack;
                minZ = plot.MinZ - slack; maxZ = plot.MaxZ + slack;
            }
            else
            {
                // No plot means the whole claim, searched outward from wherever the
                // villager is standing rather than from a corner, so a forager works the
                // patch in front of them instead of walking to the boundary every time.
                int reach = ForageRadius;
                var here = entity.Pos.AsBlockPos;
                minX = Math.Max(here.X - reach, village.CentreX - village.ClaimRadius);
                maxX = Math.Min(here.X + reach, village.CentreX + village.ClaimRadius);
                minZ = Math.Max(here.Z - reach, village.CentreZ - village.ClaimRadius);
                maxZ = Math.Min(here.Z + reach, village.CentreZ + village.ClaimRadius);
            }

            int width = maxX - minX + 1;
            if (width <= 0) return null;

            // Carry on from where the last scan ran out of budget. Without a cursor the
            // scan restarts at the same corner every time, so anything past the budget in
            // a large plot is never looked at at all: the eastern strips of a woodlot
            // would simply never be felled.
            if (scanCursorX < minX || scanCursorX > maxX) scanCursorX = minX;

            BlockPos best = null;
            double bestDist = double.MaxValue;
            int checkedColumns = 0;
            var probe = new BlockPos(0, 0, 0, 0);

            for (int step = 0; step < width; step++)
            {
                int x = minX + (scanCursorX - minX + step) % width;

                for (int z = minZ; z <= maxZ; z++)
                {
                    if (++checkedColumns > MaxColumnsPerScan)
                    {
                        // Next thought starts on the next column rather than here, so a
                        // big plot gets covered over several passes.
                        scanCursorX = minX + (x - minX + 1) % width;
                        return best;
                    }

                    // Skip most of a distant column before touching the world at all.
                    double dx = x + 0.5 - entity.Pos.X;
                    double dz = z + 0.5 - entity.Pos.Z;
                    double flat = dx * dx + dz * dz;
                    if (flat >= bestDist) continue;

                    probe.Set(x, 0, z);
                    int top = ba.GetTerrainMapheightAt(probe);

                    // Zero means the map chunk is not loaded, not that the ground is at
                    // bedrock. Searching a window around it would quietly scan the
                    // underworld and find nothing, which reads exactly like an empty plot.
                    if (top <= 0) continue;

                    for (int y = top + VerticalSearch; y >= top - VerticalSearch; y--)
                    {
                        var pos = new BlockPos(x, y, z, 0);
                        if (IsSkipped(pos)) continue;

                        Block block = ba.GetBlock(pos);
                        if (block == null || block.Id == 0) continue;
                        if (!IsTarget(block, pos)) continue;

                        double d = entity.Pos.SquareDistanceTo(pos.ToVec3d().Add(0.5, 0, 0.5));
                        if (d >= bestDist) continue;

                        bestDist = d;
                        best = pos;
                        break;
                    }
                }
            }

            // The whole box fitted in the budget, so start fresh next time.
            scanCursorX = minX;
            return best;
        }

        // --- skipping targets that did not work out ----------------------------------

        private void Skip(BlockPos pos, double seconds)
        {
            if (pos == null) return;

            // Keep it small. A worker who has given up on two hundred blocks is a worker
            // whose plot is finished, and the idle path handles that properly.
            if (skipUntil.Count > 64) skipUntil.Clear();

            skipUntil[pos.Copy()] = Now + seconds;
        }

        /// <summary>
        /// Puts a swing's worth of wear on whatever the villager is holding.
        ///
        /// Jobs that break blocks through the block accessor rather than through the
        /// game's own tool code have to do this by hand, or the tool never wears out and
        /// the whole tool economy is decoration: the rack fills up, nobody ever needs
        /// anything off it, and a village that cannot afford pickaxes never finds out.
        ///
        /// The lumberjack does not call this. Its swing goes through the axe itself,
        /// which damages the slot on the way past.
        /// </summary>
        protected void WearTool(int amount = 1)
        {
            ItemSlot slot = entity.RightHandItemSlot;
            if (slot?.Itemstack?.Collectible == null) return;
            if (slot.Itemstack.Collectible.GetDamagedBy(slot) is not EnumItemDamageSource[] by) return;
            if (Array.IndexOf(by, EnumItemDamageSource.BlockBreaking) < 0) return;

            slot.Itemstack.Collectible.DamageItem(entity.World, entity, slot, amount);
            Villager?.RefreshHands();
        }

        /// <summary>
        /// Whether the villager is carrying enough tool to break this block at all.
        ///
        /// Vintage Story gates rock and ore behind a mining tier, and breaking a block
        /// straight through the block accessor walks past that gate without asking. A
        /// bare handed quarrier cutting granite is free stone, which is exactly the kind
        /// of quiet economy hole this mod keeps having to close.
        /// </summary>
        protected bool ToolIsGoodEnough(Block block, BlockPos pos)
        {
            if (block == null) return false;

            int needed = block.GetRequiredMiningTier(entity.World, pos);
            if (needed <= 0) return true;

            return (Villager?.ToolTier ?? 0) >= needed;
        }

        /// <summary>
        /// Whether this block has been given up on recently.
        ///
        /// Protected because a job that finds its own targets has to ask: the base scan
        /// consults this itself, but a task with its own FindWork would otherwise keep
        /// handing back the same unreachable block forever.
        /// </summary>
        protected bool IsSkipped(BlockPos pos)
        {
            if (!skipUntil.TryGetValue(pos, out double until)) return false;
            if (Now < until) return true;

            skipUntil.Remove(pos);
            return false;
        }

        /// <summary>
        /// A hard cap on how much world one scan may look at.
        ///
        /// A 17x17 woodlot with a 13 block vertical window is nearly four thousand block
        /// lookups, and thirty villagers doing that on the same tick is a visible stall.
        /// The cap makes a large plot get scanned in pieces across several thoughts,
        /// which is slower to decide and invisible to play.
        /// </summary>
        private const int MaxColumnsPerScan = 400;

        /// <summary>How far a plotless job looks around itself for something to do.</summary>
        protected virtual int ForageRadius => 14;

        // --- shared helpers ----------------------------------------------------------

        /// <summary>
        /// Puts what came off a block into the villager's hands, if the village has any
        /// use for it.
        ///
        /// The filter is the important half. A villager has one pair of hands, and
        /// anything in them that no pool accepts cannot be put down in a storehouse. A
        /// digger who picked up a flower on the way through a terrace used to end up
        /// unable to carry soil and unable to deliver the flower, standing at the
        /// storehouse door for the rest of the save. So things the village would refuse
        /// never go into a villager's hands in the first place, and are left to fall on
        /// the ground where a player can have them.
        /// </summary>
        protected int Harvest(ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return 0;

            var table = Sapi?.ModLoader.GetModSystem<ResourceTable>();
            if (table != null && table.Classify(stack) == null) return 0;

            return Villager.TryCarry(stack);
        }

        /// <summary>
        /// Whether breaking this block would give the village anything at all.
        ///
        /// For the gathering jobs this is the whole test. A forager should walk past a
        /// deathcap rather than pick it, find it worthless, drop it and walk back to it
        /// again in a minute, and the resource table already knows which mushrooms a
        /// village will eat.
        /// </summary>
        protected bool WorthTaking(Block block, BlockPos pos)
        {
            if (block == null) return false;

            var table = Sapi?.ModLoader.GetModSystem<ResourceTable>();
            if (table == null) return true;

            ItemStack[] drops = block.GetDrops(entity.World, pos, null);
            if (drops == null) return false;

            foreach (ItemStack drop in drops)
            {
                if (drop != null && table.Classify(drop) != null) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether whatever this block would give could join what the villager is already
        /// holding. Asked before breaking anything, so the answer can be "go home first"
        /// rather than "drop it on the floor".
        /// </summary>
        protected bool HandsCanTake(Block block, BlockPos pos)
        {
            if (!Villager.IsCarrying) return true;
            if (block == null) return true;

            ItemStack[] drops = block.GetDrops(entity.World, pos, null);
            if (drops == null || drops.Length == 0) return true;

            var table = Sapi?.ModLoader.GetModSystem<ResourceTable>();
            ItemStack held = Villager.CarriedStack;

            bool anythingUseful = false;
            foreach (ItemStack drop in drops)
            {
                if (drop == null) continue;
                if (table != null && table.Classify(drop) == null) continue;

                anythingUseful = true;
                if (held.Satisfies(drop)) return true;
            }

            // Nothing it gives is worth anything, so full hands are no obstacle.
            return !anythingUseful;
        }

        /// <summary>
        /// Breaks a block properly and carries what it drops.
        ///
        /// Going through the game's own break rather than swapping the block to air means
        /// drop tables, tool tiers and block behaviours all apply, so a village gets
        /// exactly what a player would have got. Anything that will not fit in their hands
        /// is left where it fell rather than deleted.
        /// </summary>
        protected int BreakAndCarry(BlockPos pos)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;
            Block block = ba.GetBlock(pos);
            if (block == null || block.Id == 0) return 0;

            ItemStack[] drops = block.GetDrops(entity.World, pos, null) ?? Array.Empty<ItemStack>();

            // Break it with a drop multiplier of zero, because we have already taken the
            // drops above and are about to put them in the villager's hands. Breaking it
            // normally spawns a second set on the ground, which is a village that doubles
            // its own harvest and litters the plot doing it.
            // BreakBlock already triggers the neighbour update itself, so doing it again
            // here doubles the relight and decay cascade on every block a village breaks.
            entity.World.BlockAccessor.BreakBlock(pos, null, 0f);

            int taken = 0;
            foreach (ItemStack drop in drops)
            {
                if (drop == null) continue;
                int got = Harvest(drop);
                taken += got;

                if (got < drop.StackSize)
                {
                    ItemStack rest = drop.Clone();
                    rest.StackSize = drop.StackSize - got;
                    entity.World.SpawnItemEntity(rest, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            return taken;
        }

        /// <summary>
        /// Where to take a load home to.
        ///
        /// The crate when there is one, and the village centre when there is not.
        ///
        /// The fallback matters more than it looks. The stores live in the ledger, not in
        /// the box, so the box is the player's window onto them rather than a requirement
        /// for them. Without this a village whose storehouse got broken could not deliver,
        /// could not earn, and so could never afford to rebuild the storehouse: one swing
        /// of an axe would kill a settlement permanently. Villagers carry on regardless
        /// and the crate goes back up when the village can pay for it.
        /// </summary>
        protected BlockPos StorehousePos(Village village)
        {
            if (village == null) return null;

            if (village.HasStorehouse)
            {
                return new BlockPos(village.StorehouseX, village.StorehouseY, village.StorehouseZ, 0);
            }

            return village.Centre;
        }

        protected bool WithinReach(BlockPos pos, double reach = -1)
        {
            if (pos == null) return false;
            double r = reach > 0 ? reach : FFConfig.Current.Work.ReachBlocks;
            return entity.Pos.SquareDistanceTo(pos.ToVec3d().Add(0.5, 0.5, 0.5)) <= r * r;
        }

        /// <summary>
        /// How long one block takes. Better tools are faster, with a floor, because a
        /// steel axe should feel like an upgrade and not like a cheat.
        /// </summary>
        protected virtual float WorkTimeSec
        {
            get
            {
                var cfg = FFConfig.Current.Work;
                float factor = 1f - cfg.ToolTierSpeedBonus * Villager.ToolTier;
                factor = Math.Max(cfg.MinWorkTimeFraction, factor);
                return cfg.SecondsPerBlock * factor;
            }
        }

        protected int HaulThreshold
            => Math.Min(FFConfig.Current.Work.HaulAtCarriedCount, VillagerCarry.CarryCapacity);

        protected double Now => entity.World.ElapsedMilliseconds / 1000.0;

        private void FaceTarget()
        {
            if (Target == null) return;
            double dx = Target.X + 0.5 - entity.Pos.X;
            double dz = Target.Z + 0.5 - entity.Pos.Z;
            entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
            if (entity is EntityAgent agent) agent.BodyYaw = entity.Pos.Yaw;
        }

        private void SwingAnimation()
        {
            if (entity.AnimManager?.IsAnimationActive("hit") == true) return;
            entity.AnimManager?.StartAnimation("hit");
        }

        private string Label()
            => (Villager.GivenName == "" ? "#" + entity.EntityId : Villager.GivenName)
             + " the " + Trade.ToString().ToLowerInvariant();

        private void Note(string what)
            => entity.Api.Logger.Notification("[F&F] {0} {1}.", Label(), what);

        public override string DebugLabel()
        {
            string kind = Trade.ToString().ToLowerInvariant();
            switch (Step)
            {
                case EnumWorkStep.Choosing: return kind + ": looking for work";
                case EnumWorkStep.Travelling: return kind + ": walking to " + Target;
                case EnumWorkStep.Working: return kind + ": working (" + (int)workedThisVisit + " done)";
                case EnumWorkStep.Hauling: return kind + ": hauling " + Villager.CarriedCount;
                default: return kind;
            }
        }
    }
}
