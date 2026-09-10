using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Every village in the world, and the only thing allowed to create or destroy one.
    ///
    /// Server side only. Villages are server state; the client is told what it needs to
    /// see and nothing more.
    ///
    /// Saved into the world file rather than a mod config, because a village belongs to
    /// a world the way a chunk does. Stored as JSON: there are dozens of villages at
    /// most, JSON survives a field being added without a migration step, and when
    /// something goes wrong the save data can be read by a human.
    /// </summary>
    public partial class VillageRegistry : ModSystem
    {
        /// <summary>Bump only if the shape changes in a way old data cannot survive.</summary>
        public const int SaveFormatVersion = 1;

        private const string SaveKey = "foundriesfrontiers:villages";

        private ICoreServerAPI sapi;

        private readonly Dictionary<long, Village> byId = new Dictionary<long, Village>();
        private long nextId = 1;

        /// <summary>
        /// Loaded members per village. Rebuilt from entity load events rather than
        /// persisted, because it only ever describes what is in memory right now.
        /// </summary>
        private readonly Dictionary<long, List<FFVillager>> loadedMembers =
            new Dictionary<long, List<FFVillager>>();

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        /// <summary>Runs after the culture table is loaded, so village names can use it.</summary>
        public override double ExecuteOrder() => 0.3;

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.GameWorldSave += OnGameWorldSave;

            // The day clock. Checked on a real-time timer rather than every tick because
            // a day is the smallest unit anything here cares about, and a village that
            // notices the date five seconds late is indistinguishable from one that does not.
            api.Event.RegisterGameTickListener(OnDayCheck, 5000);

            LoadFacilityConfig(api);

            // Keeping the facility list current as the world changes, so a scan is only
            // ever needed when something happened that these did not see.
            // Refusing a break has to happen here, where the engine is still asking.
            // Doing it in Block.OnBlockBroken is too late: the client has already
            // predicted the break and taken its own block entity apart, which leaves a
            // crate standing that nothing can open.
            api.Event.BreakBlock += OnBreakBlock;

            api.Event.DidPlaceBlock += (player, id, sel, stack) => NoticeBlockChanged(sel?.Position);
            api.Event.DidBreakBlock += (player, id, sel) => NoticeBlockChanged(sel?.Position);

            api.Event.OnEntityLoaded += OnEntityAppeared;
            api.Event.OnEntitySpawn += OnEntityAppeared;
            api.Event.OnEntityDespawn += OnEntityDespawn;
        }

        // --- the collection --------------------------------------------------------

        public IReadOnlyCollection<Village> All => byId.Values;

        public int Count => byId.Count;

        public Village Get(long id) => byId.TryGetValue(id, out Village v) ? v : null;

        /// <summary>
        /// The ids that do exist, for error messages. Ids are never reused, so after a
        /// few rounds of testing they are nowhere near 1 and "no village with id 1" is
        /// only useful if it also says what the ids actually are.
        /// </summary>
        public string IdList()
        {
            if (byId.Count == 0) return "there are no villages at all";
            var parts = new List<string>();
            foreach (Village v in byId.Values) parts.Add("#" + v.Id + " " + v.Name);
            return "existing: " + string.Join(", ", parts);
        }

        /// <summary>
        /// The village whose claim contains this position. If claims overlap, which they
        /// should not but might after a tier bump, the nearer centre wins.
        /// </summary>
        public Village VillageAt(BlockPos pos)
        {
            Village best = null;
            double bestDist = double.MaxValue;

            foreach (Village v in byId.Values)
            {
                if (!v.Contains(pos)) continue;
                double d = v.HorizontalDistanceTo(pos);
                if (d < bestDist) { bestDist = d; best = v; }
            }
            return best;
        }

        /// <summary>Nearest village centre within maxBlocks, claim or no claim.</summary>
        public Village Nearest(BlockPos pos, double maxBlocks = double.MaxValue)
        {
            Village best = null;
            double bestDist = maxBlocks;

            foreach (Village v in byId.Values)
            {
                double d = v.HorizontalDistanceTo(pos);
                if (d <= bestDist) { bestDist = d; best = v; }
            }
            return best;
        }

        /// <summary>
        /// Founds a village. Returns null with a reason if the site is not usable, which
        /// is deliberately the only way to fail: callers should never have to pre-check.
        /// </summary>
        public Village Create(BlockPos centre, string cultureCode, string name, out string error)
        {
            error = null;

            if (centre == null) { error = "No position given."; return null; }

            var cultures = sapi.ModLoader.GetModSystem<CultureSystem>();
            if (!cultures.Has(cultureCode)) cultureCode = CultureSystem.DefaultCulture;

            double minGap = FFConfig.Current.Village.MinBlocksBetweenCentres;
            foreach (Village other in byId.Values)
            {
                double d = other.HorizontalDistanceTo(centre);
                if (d < minGap)
                {
                    error = "Too close to " + other.Name + " (" + (int)d + " blocks, minimum is " + (int)minGap + ").";
                    sapi.Logger.Notification("[F&F] Refused a village at {0}: {1}", centre, error);
                    return null;
                }
            }

            var village = new Village
            {
                Id = nextId++,
                CultureCode = cultureCode,
                Name = string.IsNullOrWhiteSpace(name) ? PickName(cultureCode) : name.Trim(),
                Tier = 0,
                FoundedTotalDays = sapi.World.Calendar.TotalDays,
                CentreX = centre.X,
                CentreY = centre.Y,
                CentreZ = centre.Z,
                LastSimulatedDay = Math.Floor(sapi.World.Calendar.TotalDays)
            };

            byId[village.Id] = village;
            loadedMembers[village.Id] = new List<FFVillager>();

            PlaceMarker(village);
            PlaceStorehouse(village);
            ScanFacilities(village);

            sapi.Logger.Notification("[F&F] Founded {0} at {1}", village, centre);
            return village;
        }

        /// <summary>
        /// The village dies but its remains stay in the world.
        ///
        /// This is the path H1 and H2 will use when a settlement fails: the cairn is left
        /// standing as a grave marker with the name still on it, and the storehouse turns
        /// into an ordinary lootable box holding what was not carried away. Removing the
        /// record afterwards is what makes the ground free for a reclamation later.
        ///
        /// Distinct from Remove, which is a clean delete for testing and leaves nothing.
        /// </summary>
        public bool Abandon(long id, out string report)
        {
            report = null;
            if (!byId.TryGetValue(id, out Village v)) return false;

            float fraction = GameMath.Clamp(FFConfig.Current.Village.RuinLootFraction, 0f, 1f);
            var left = new List<string>();

            if (v.HasStorehouse)
            {
                var pos = new BlockPos(v.StorehouseX, v.StorehouseY, v.StorehouseZ, 0);
                if (sapi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityStorehouse crate
                    && crate.VillageId == v.Id)
                {
                    crate.AbandonWith(v, fraction);

                    foreach (EnumVillageResource pool in VillageResources.All)
                    {
                        float amount = v.Ledger.Get(pool) * fraction;
                        if (amount > 0) left.Add(amount.ToString("0.#") + " " + pool.ToString().ToLowerInvariant());
                    }
                }
            }

            if (v.HasMarker)
            {
                var pos = new BlockPos(v.MarkerX, v.MarkerY, v.MarkerZ, 0);
                if (sapi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityVillageCairn cairn
                    && cairn.VillageId == v.Id)
                {
                    cairn.RuinedName = v.Name;
                    cairn.VillageId = 0;
                    cairn.MarkDirty(true);
                }
            }

            foreach (FFVillager villager in LoadedMembers(id))
            {
                villager.VillageId = 0;
                villager.RefreshNameTag();
            }

            UnregisterFacilities(v);
            HideClaimEverywhere(id);
            HidePlotsEverywhere(id);
            byId.Remove(id);
            loadedMembers.Remove(id);

            report = v.Name + " is abandoned. "
                   + (left.Count == 0 ? "Its storehouse is empty." : "Left in the storehouse: " + string.Join(", ", left) + ".");
            sapi.Logger.Notification("[F&F] {0}", report);
            return true;
        }

        /// <summary>
        /// Removes a village and releases everyone in it. Members are not killed: they
        /// become unaffiliated, which is what an orphaned villager is.
        ///
        /// This is the clean delete. A village that actually failed should go through
        /// Abandon instead, which leaves the ruin behind.
        /// </summary>
        public bool Remove(long id)
        {
            if (!byId.TryGetValue(id, out Village v)) return false;

            foreach (FFVillager villager in LoadedMembers(id))
            {
                villager.VillageId = 0;
                villager.RefreshNameTag();
            }

            ClearMarker(v);
            ClearStorehouse(v);
            UnregisterFacilities(v);
            HideClaimEverywhere(id);
            HidePlotsEverywhere(id);

            byId.Remove(id);
            loadedMembers.Remove(id);
            sapi.Logger.Notification("[F&F] Removed village {0}", v);
            return true;
        }

        private string PickName(string cultureCode)
        {
            Culture culture = sapi.ModLoader.GetModSystem<CultureSystem>()?.Get(cultureCode);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Village v in byId.Values) taken.Add(v.Name);

            if (culture != null)
            {
                // A handful of tries at an unused name beats scanning the whole pool,
                // and the fallback below is fine when a culture runs out of names.
                for (int i = 0; i < 12; i++)
                {
                    string candidate = culture.RandomVillageName(sapi.World.Rand);
                    if (candidate != null && !taken.Contains(candidate)) return candidate;
                }
            }

            return "Settlement " + nextId;
        }

        // --- membership ------------------------------------------------------------

        public IReadOnlyList<FFVillager> LoadedMembers(long villageId)
        {
            if (loadedMembers.TryGetValue(villageId, out List<FFVillager> list)) return list;
            return Array.Empty<FFVillager>();
        }

        /// <summary>Total roster including villagers in unloaded chunks.</summary>
        public int Population(long villageId) => Get(villageId)?.MemberIds.Count ?? 0;

        public bool Join(FFVillager villager, Village village)
        {
            if (villager == null || village == null) return false;

            if (villager.VillageId != 0 && villager.VillageId != village.Id)
            {
                Leave(villager);
            }

            villager.VillageId = village.Id;
            if (!village.MemberIds.Contains(villager.EntityId)) village.MemberIds.Add(villager.EntityId);
            TrackLoaded(villager, village.Id);
            villager.RefreshNameTag();
            return true;
        }

        public void Leave(FFVillager villager)
        {
            if (villager == null) return;

            long id = villager.VillageId;
            if (id != 0)
            {
                Get(id)?.MemberIds.Remove(villager.EntityId);
                UntrackLoaded(villager, id);
            }

            villager.VillageId = 0;
            villager.RefreshNameTag();
        }

        private void TrackLoaded(FFVillager villager, long villageId)
        {
            if (!loadedMembers.TryGetValue(villageId, out List<FFVillager> list))
            {
                list = new List<FFVillager>();
                loadedMembers[villageId] = list;
            }
            if (!list.Contains(villager)) list.Add(villager);
        }

        private void UntrackLoaded(FFVillager villager, long villageId)
        {
            if (loadedMembers.TryGetValue(villageId, out List<FFVillager> list)) list.Remove(villager);
        }

        private void OnEntityAppeared(Entity entity)
        {
            if (entity is not FFVillager villager) return;

            long id = villager.VillageId;
            if (id == 0) return;

            Village village = Get(id);
            if (village == null)
            {
                // The village this villager remembers is gone. That is an orphan, and
                // the design says orphans look for a new home rather than vanishing.
                villager.VillageId = 0;
                villager.RefreshNameTag();
                return;
            }

            if (!village.MemberIds.Contains(villager.EntityId)) village.MemberIds.Add(villager.EntityId);
            TrackLoaded(villager, id);
        }

        private void OnEntityDespawn(Entity entity, EntityDespawnData reason)
        {
            if (entity is not FFVillager villager) return;

            long id = villager.VillageId;
            if (id == 0) return;

            UntrackLoaded(villager, id);

            // A chunk unloading is not leaving the village, so the roster keeps them.
            // Anything else means this villager is not coming back and the roster would
            // otherwise fill up with people who no longer exist.
            EnumDespawnReason why = reason?.Reason ?? EnumDespawnReason.Death;
            bool stillOnRoster =
                why == EnumDespawnReason.Unload ||
                why == EnumDespawnReason.OutOfRange ||
                why == EnumDespawnReason.Disconnect;

            if (!stillOnRoster)
            {
                Get(id)?.MemberIds.Remove(villager.EntityId);
                if (why == EnumDespawnReason.Death) DevStats.Bump(DevStats.VillagersDied);
            }
        }

        // --- tier --------------------------------------------------------------------

        /// <summary>
        /// Moves a village to a new tier and makes the world show it.
        ///
        /// Tier is not just a number on a record. The claim grows, the marker is rebuilt
        /// at the stage that tier deserves, and anyone looking at the claim outline sees
        /// the new size. E1 calls this when a gate is met; for now the dev command does.
        /// </summary>
        public bool SetTier(Village village, int tier)
        {
            if (village == null) return false;

            tier = GameMath.Clamp(tier, 0, MaxTier);
            if (tier == village.Tier) return false;

            string wasStage = StageForTier(village.Tier);
            village.Tier = tier;
            village.DaysAtCurrentTier = 0;

            // The marker only needs replacing when the stage actually changes, but the
            // claim grew either way, so anyone watching the outline gets a fresh one.
            RefreshStorehouse(village);
            ScanFacilities(village);

            if (StageForTier(tier) != wasStage || !village.HasMarker)
            {
                ClearMarker(village);
                PlaceMarker(village);
            }

            RefreshShownClaims(village.Id);
            sapi.Logger.Notification("[F&F] {0} is now tier {1}.", village.Name, tier);
            return true;
        }

        // --- claim outlines --------------------------------------------------------

        /// <summary>
        /// Highlight slot for claim outlines. Any number does, as long as nothing else in
        /// the mod reuses it, because a slot is replaced wholesale each time it is set.
        /// </summary>
        private const int ClaimHighlightSlot = 1701;

        /// <summary>
        /// Who is currently looking at which claim, by player uid.
        ///
        /// This lives here rather than in the command that draws it because the outline
        /// has to disappear when the village does, and only the registry knows when that
        /// happens. A command that draws something the world can outlive is a command
        /// that leaves litter on the ground.
        /// </summary>
        private readonly Dictionary<string, long> claimShownTo = new Dictionary<string, long>();

        public void ShowClaim(IServerPlayer player, Village village)
        {
            if (player == null || village == null) return;

            var blocks = new List<BlockPos>();
            int r = village.ClaimRadius;
            int step = r > 48 ? 2 : 1;

            for (int d = -r; d <= r; d += step)
            {
                AddOutlineBlock(blocks, village.CentreX + d, village.CentreZ - r);
                AddOutlineBlock(blocks, village.CentreX + d, village.CentreZ + r);
                AddOutlineBlock(blocks, village.CentreX - r, village.CentreZ + d);
                AddOutlineBlock(blocks, village.CentreX + r, village.CentreZ + d);
            }

            var colours = new List<int>();
            int colour = ColorUtil.ToRgba(120, 70, 190, 255);
            for (int i = 0; i < blocks.Count; i++) colours.Add(colour);

            sapi.World.HighlightBlocks(player, ClaimHighlightSlot, blocks, colours);
            claimShownTo[player.PlayerUID] = village.Id;
        }

        public void HideClaim(IServerPlayer player)
        {
            if (player == null) return;
            sapi.World.HighlightBlocks(player, ClaimHighlightSlot, new List<BlockPos>());
            claimShownTo.Remove(player.PlayerUID);
        }

        /// <summary>Redraws the outline for anyone looking, after a claim changes size.</summary>
        private void RefreshShownClaims(long villageId)
        {
            Village v = Get(villageId);
            if (v == null) return;

            var watchers = new List<string>();
            foreach (var kv in claimShownTo) if (kv.Value == villageId) watchers.Add(kv.Key);

            foreach (string uid in watchers)
            {
                if (sapi.World.PlayerByUid(uid) is IServerPlayer player) ShowClaim(player, v);
            }
        }

        /// <summary>Clears the outline for anyone currently looking at this village.</summary>
        private void HideClaimEverywhere(long villageId)
        {
            var stale = new List<string>();
            foreach (var kv in claimShownTo) if (kv.Value == villageId) stale.Add(kv.Key);

            foreach (string uid in stale)
            {
                IServerPlayer player = sapi.World.PlayerByUid(uid) as IServerPlayer;
                if (player != null) sapi.World.HighlightBlocks(player, ClaimHighlightSlot, new List<BlockPos>());
                claimShownTo.Remove(uid);
            }
        }

        private void AddOutlineBlock(List<BlockPos> into, int x, int z)
        {
            var probe = new BlockPos(x, 0, z, 0);
            int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(probe);
            into.Add(new BlockPos(x, y, z, 0));
        }

        // --- the day clock ---------------------------------------------------------

        /// <summary>
        /// Raised once per in game day per village, after its ledger has closed the day.
        /// The brain hangs off this later; right now the ledger is the only listener.
        /// </summary>
        public event Action<Village> OnNewDay;

        /// <summary>
        /// Days are caught up one at a time rather than skipped to, because the whole
        /// point of the ledger is that a day either happened or it did not. This cap
        /// stops a calendar jump of a thousand days from freezing the server, at the
        /// cost of the skipped days simply not being recorded.
        /// </summary>
        private const int MaxDaysCaughtUpAtOnce = 400;

        private void OnDayCheck(float dt)
        {
            int today = (int)sapi.World.Calendar.TotalDays;

            // Copied because a day handler is allowed to found or remove a village, and
            // the brain hanging off OnNewDay later will certainly want to.
            var todays = new List<Village>(byId.Values);

            foreach (Village v in todays)
            {
                bool observed = IsLoaded(v);
                v.Ledger.NoteObservation(observed);

                // A day counts as measured only if the village was running for enough of
                // it. Below the threshold the day is left out of the history entirely and
                // fast-forward accounts for it instead, which is F1's job.
                bool measurable = v.Ledger.ObservedFractionToday
                                  >= FFConfig.Current.Village.ObservedDayThreshold;

                int caughtUp = 0;
                while ((int)v.LastSimulatedDay < today && caughtUp < MaxDaysCaughtUpAtOnce)
                {
                    // Only the first day round can possibly have been observed.
                    //
                    // Rolling a day zeroes today's totals, so every later turn of this loop
                    // files an all-zero row. Marking those observed was the old "unwatched
                    // days count as zero production" bug arriving through the back door: a
                    // village a week behind would end up with one real day, six invented
                    // ones, full confidence, and a measured rate a seventh of the truth.
                    v.Ledger.RollDay(measurable && caughtUp == 0);
                    v.DaysAtCurrentTier++;
                    v.LastSimulatedDay += 1;
                    caughtUp++;

                    RepairMarkers(v);
                    AssignBeds(v);
                    AgePlots(v);
                    ConsiderBuilding(v);
                    OnNewDay?.Invoke(v);
                }

                if (caughtUp >= MaxDaysCaughtUpAtOnce)
                {
                    sapi.Logger.Warning(
                        "[F&F] {0} was {1} days behind and only {2} were caught up. Skipping to today.",
                        v.Name, today - (int)v.LastSimulatedDay, MaxDaysCaughtUpAtOnce);
                    v.LastSimulatedDay = today;
                }
            }
        }

        /// <summary>
        /// True when the village centre sits in a chunk the server currently has loaded.
        /// That is the same condition that decides whether its villagers can do anything,
        /// so it is the right definition of "this day happened".
        /// </summary>
        public bool IsLoaded(Village v)
        {
            if (v == null) return false;
            // Shift, not divide. Integer division truncates toward zero, so -10 / 32 is 0
            // rather than -1, and every village at a negative coordinate was asking about
            // a chunk on the wrong side of the axis. That answer feeds the observed-day
            // sampling, so half the map was either inventing production or throwing away
            // days it had genuinely earned.
            return sapi.WorldManager.GetChunk(v.CentreX >> 5, v.CentreY >> 5, v.CentreZ >> 5) != null;
        }

        /// <summary>
        /// A village with people in it puts its own marker back up, and its storehouse
        /// too, within a day of losing them.
        ///
        /// This is standing in for a real builder task. Once B7 and the maintenance job
        /// exist a villager should walk over and rebuild it out of stone the village
        /// actually has, and this goes away. Until then it is gated on the village having
        /// at least one member, because a place with nobody left in it should stay
        /// broken. That is what a ruin is.
        /// </summary>
        /// <summary>
        /// Puts the cairn and the storehouse back when they are missing, and charges the
        /// village for the materials.
        ///
        /// It used to do this for free, which was a hole: a player could break a village's
        /// storehouse every morning and the village would conjure a new one out of nothing
        /// by lunchtime. Everything else in the mod obeys the rule that nothing enters or
        /// leaves the ledger without a reason anyone can point at, and this did not.
        ///
        /// A village that cannot pay stays broken and says so. That is the same visible
        /// stall a half built house has, applied to the two things a village cannot really
        /// do without, which makes losing them matter.
        /// </summary>
        private void RepairMarkers(Village v)
        {
            if (v == null || v.MemberIds.Count == 0) return;

            var cfg = FFConfig.Current.Village;

            if (!v.HasMarker)
            {
                if (!Spend(v, EnumVillageResource.Stone, cfg.MarkerRepairStone))
                {
                    sapi.Logger.VerboseDebug(
                        "[F&F] {0} wants its stones back up and is short of stone.", v.Name);
                }
                else if (PlaceMarker(v) != null)
                {
                    sapi.Logger.Notification(
                        "[F&F] {0} put its stones back up for {1} stone.", v.Name, cfg.MarkerRepairStone);
                }
                else
                {
                    // Nowhere to put it. Give the stone back rather than charging for a
                    // cairn that never appeared.
                    Unspend(v, EnumVillageResource.Stone, cfg.MarkerRepairStone);
                }
            }

            if (!v.HasStorehouse)
            {
                if (!Spend(v, EnumVillageResource.Wood, cfg.StorehouseRepairWood))
                {
                    sapi.Logger.VerboseDebug(
                        "[F&F] {0} wants its storehouse back and is short of wood.", v.Name);
                }
                else if (PlaceStorehouse(v) != null)
                {
                    sapi.Logger.Notification(
                        "[F&F] {0} rebuilt its storehouse for {1} wood.", v.Name, cfg.StorehouseRepairWood);
                }
                else
                {
                    Unspend(v, EnumVillageResource.Wood, cfg.StorehouseRepairWood);
                }
            }
        }

        /// <summary>
        /// Rolls one day by hand, for testing without touching the calendar. Always filed
        /// as observed: you asked for the day, so the day counts.
        /// </summary>
        public void ForceDay(Village v)
        {
            if (v == null) return;
            RepairMarkers(v);
            AgePlots(v);
            ConsiderBuilding(v);
            v.Ledger.RollDay(true);
            v.DaysAtCurrentTier++;
            v.LastSimulatedDay += 1;
            OnNewDay?.Invoke(v);
        }

        /// <summary>
        /// Spends from a village's stores and keeps the crate honest about it.
        ///
        /// Every withdrawal outside the storehouse dialog must go through here. The crate
        /// caches what it is showing, and anything that debits the ledger behind its back
        /// leaves the shelves displaying the old figure. A player who then empties the row
        /// takes goods the village no longer has, which is items created from nothing.
        /// </summary>
        public bool Spend(Village village, EnumVillageResource r, float amount)
        {
            if (village == null) return false;
            if (!village.Ledger.Withdraw(r, amount)) return false;

            RefreshStorehouse(village);
            return true;
        }

        /// <summary>Puts back a failed spend, and keeps the crate in step.</summary>
        public void Unspend(Village village, EnumVillageResource r, float amount)
        {
            if (village == null) return;
            village.Ledger.Refund(r, amount);
            RefreshStorehouse(village);
        }

        // --- deposits --------------------------------------------------------------

        /// <summary>
        /// Puts what a villager is carrying into their village's stores.
        ///
        /// This is the only route resources take into a ledger during play, and it runs
        /// off a real stack a real villager really carried. Anything the village cannot
        /// use is left in their hands rather than quietly deleted.
        /// </summary>
        public bool DepositCarried(FFVillager villager, out string outcome)
        {
            outcome = null;

            if (villager == null) { outcome = "No villager."; return false; }
            if (!villager.IsCarrying) { outcome = "Carrying nothing."; return false; }

            Village village = Get(villager.VillageId);
            if (village == null) { outcome = "No village to deposit into."; return false; }

            var table = sapi.ModLoader.GetModSystem<ResourceTable>();
            ItemStack stack = villager.CarriedStack;

            EnumVillageResource? pool = table?.Classify(stack);
            if (pool == null)
            {
                outcome = village.Name + " has no use for " + stack.GetName() + ".";
                return false;
            }

            float value = table.ValueOf(stack);
            village.Ledger.Deposit(pool.Value, value, stack.Collectible?.Code?.ToShortString());

            // A meal arrives in something, and the shelves get rebuilt from the ledger, so
            // the pot would otherwise be thrown away for nothing. Credit it separately.
            if (table.ContainerOf(stack, out EnumVillageResource cpool, out float cvalue))
            {
                village.Ledger.Deposit(cpool, cvalue, null);
            }

            int count = stack.StackSize;
            string name = stack.GetName();
            villager.TakeCarried();

            RefreshStorehouse(village);
            DevStats.Bump(DevStats.DepositsMade);
            outcome = count + "x " + name + " into " + pool.Value.ToString().ToLowerInvariant()
                    + " (+" + value.ToString("0.#") + "), " + village.Name + " now holds "
                    + village.Ledger.Get(pool.Value).ToString("0.#") + ".";
            return true;
        }

        // --- the centre cairn ------------------------------------------------------

        /// <summary>
        /// Puts the marker at the village centre, or as close above it as there is room.
        /// Returns where it went, or null if it could not be placed.
        ///
        /// The centre is wherever the founder stood, which may be inside a hillside or a
        /// metre in the air. Rather than refuse, this walks up to find the first spot
        /// that will take a block and settles it onto the ground beneath.
        /// </summary>
        public BlockPos PlaceMarker(Village village)
        {
            if (village == null) return null;

            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos pos = village.Centre.Copy();

            Block cairn = CairnFor(pos, village.Tier);
            if (cairn == null)
            {
                sapi.Logger.Warning("[F&F] villagecairn block did not resolve. Is the blocktype JSON loading?");
                return null;
            }

            // Up out of any solid ground first.
            for (int i = 0; i < 8 && !IsFree(ba, pos); i++) pos.Y++;
            if (!IsFree(ba, pos)) return null;

            // Then back down onto whatever is under it, so it never floats.
            for (int i = 0; i < 8 && IsFree(ba, pos.DownCopy()); i++) pos.Y--;

            ba.SetBlock(cairn.BlockId, pos);

            if (ba.GetBlockEntity(pos) is BlockEntityVillageCairn be)
            {
                be.VillageId = village.Id;
                be.MarkDirty(true);
            }

            village.MarkerX = pos.X;
            village.MarkerY = pos.Y;
            village.MarkerZ = pos.Z;
            village.HasMarker = true;
            return pos;
        }

        /// <summary>
        /// The cairn variant matching the bedrock under this spot, so a village's marker
        /// is built out of the same stone lying around it.
        ///
        /// The rock type comes from the map chunk's top rock map, which is the same thing
        /// worldgen uses to decide what the loose stones on the surface are made of, so
        /// the cairn agrees with its surroundings rather than guessing from whatever
        /// block happens to be directly underneath a patch of soil.
        /// </summary>
        /// <summary>
        /// Which marker a village of this tier gets. Three stones at the founding, a
        /// stacked cairn once it is established, dressed stone when it has a mason, a
        /// monument when it is a town.
        /// </summary>
        public static string StageForTier(int tier)
        {
            if (tier <= 1) return "rough";
            if (tier <= 3) return "cairn";
            if (tier == 4) return "column";
            return "monument";
        }

        /// <summary>
        /// The top of the ladder. Six tiers, 0 to 5, with Town as the capstone.
        ///
        /// There is no tier 6. What a seventh tier was being asked for is density and
        /// activity rather than another material, and that scales continuously with a
        /// town's population instead of unlocking at a stage. See G11 in the build order.
        /// </summary>
        public const int MaxTier = 5;

        private Block CairnFor(BlockPos pos, int tier)
        {
            string rock = "granite";
            string stage = StageForTier(tier);

            try
            {
                IMapChunk mc = sapi.World.BlockAccessor.GetMapChunkAtBlockPos(pos);
                int[] topRock = mc?.TopRockIdMap;
                if (topRock != null && topRock.Length > 0)
                {
                    // Mask rather than modulo, for the same reason. C# leaves the sign on
                    // a negative remainder, so this went negative west or north of origin
                    // and every village over there quietly got a granite cairn whatever
                    // its bedrock was.
                    int index = (pos.Z & 31) * 32 + (pos.X & 31);
                    if (index >= 0 && index < topRock.Length)
                    {
                        Block rockBlock = sapi.World.GetBlock(topRock[index]);
                        if (rockBlock?.Variant != null && rockBlock.Variant.TryGetValue("rock", out string found)
                            && !string.IsNullOrEmpty(found))
                        {
                            rock = found;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                sapi.Logger.Warning("[F&F] Could not read the local rock type at {0}: {1}", pos, e.Message);
            }

            Block cairn = sapi.World.GetBlock(
                new AssetLocation(FoundriesFrontiersMod.ModId, "villagecairn-" + rock + "-" + stage));

            // A rock type with no cairn variant is not worth failing over.
            return cairn ?? sapi.World.GetBlock(
                new AssetLocation(FoundriesFrontiersMod.ModId, "villagecairn-granite-" + stage));
        }

        private static bool IsFree(IBlockAccessor ba, BlockPos pos)
        {
            Block b = ba.GetBlock(pos);
            return b == null || b.BlockId == 0 || b.Replaceable >= 6000;
        }

        /// <summary>
        /// Puts the storehouse crate down beside the cairn.
        ///
        /// Beside rather than on top because the cairn is the village's name and the
        /// storehouse is its stores, and in Phase C the storehouse becomes a real
        /// building that wants its own ground.
        /// </summary>
        public BlockPos PlaceStorehouse(Village village)
        {
            if (village == null) return null;

            Block crate = sapi.World.GetBlock(new AssetLocation(FoundriesFrontiersMod.ModId, "storehouse"));
            if (crate == null)
            {
                sapi.Logger.Warning("[F&F] storehouse block did not resolve. Is the blocktype JSON loading?");
                return null;
            }

            IBlockAccessor ba = sapi.World.BlockAccessor;

            // Clear whatever is at the recorded spot first. A crate left in a bad state by
            // an older build would otherwise sit there refusing to be replaced.
            if (village.HasStorehouse)
            {
                var was = new BlockPos(village.StorehouseX, village.StorehouseY, village.StorehouseZ, 0);
                if (ba.GetBlockEntity(was) is BlockEntityStorehouse stale && !stale.Abandoned)
                {
                    ba.SetBlock(0, was);
                }
                village.HasStorehouse = false;
            }

            // Try a ring of spots around the centre rather than one fixed offset, so a
            // village founded against a wall still gets its stores somewhere sensible.
            foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
            {
                for (int dist = 2; dist <= 4; dist++)
                {
                    var pos = new BlockPos(
                        village.CentreX + facing.Normali.X * dist,
                        village.CentreY,
                        village.CentreZ + facing.Normali.Z * dist, 0);

                    for (int i = 0; i < 8 && !IsFree(ba, pos); i++) pos.Y++;
                    if (!IsFree(ba, pos)) continue;
                    for (int i = 0; i < 8 && IsFree(ba, pos.DownCopy()); i++) pos.Y--;

                    ba.SetBlock(crate.BlockId, pos);

                    if (ba.GetBlockEntity(pos) is BlockEntityStorehouse be)
                    {
                        be.VillageId = village.Id;
                        be.RebuildFromLedger();
                        be.MarkDirty(true);
                    }

                    village.StorehouseX = pos.X;
                    village.StorehouseY = pos.Y;
                    village.StorehouseZ = pos.Z;
                    village.HasStorehouse = true;
                    return pos;
                }
            }

            sapi.Logger.Warning("[F&F] No room for {0}'s storehouse near {1}.", village.Name, village.Centre);
            return null;
        }

        /// <summary>
        /// Puts a storehouse back exactly as it was: block, block entity and village link.
        ///
        /// The safety net behind the refusal. If a break ever gets past it, this restores
        /// the crate whole rather than leaving one standing with nothing behind it, which
        /// is the state that made it impossible to open.
        /// </summary>
        public bool RestoreStorehouse(Village village, BlockPos pos)
        {
            if (village == null || pos == null) return false;

            Block crate = sapi.World.GetBlock(new AssetLocation(FoundriesFrontiersMod.ModId, "storehouse"));
            if (crate == null) return false;

            IBlockAccessor ba = sapi.World.BlockAccessor;
            ba.SetBlock(crate.BlockId, pos);

            if (ba.GetBlockEntity(pos) is BlockEntityStorehouse be)
            {
                be.VillageId = village.Id;
                be.RebuildFromLedger();
                be.MarkDirty(true);
            }

            village.StorehouseX = pos.X;
            village.StorehouseY = pos.Y;
            village.StorehouseZ = pos.Z;
            village.HasStorehouse = true;
            return true;
        }

        /// <summary>Takes the storehouse back out, if it is still there.</summary>
        public void ClearStorehouse(Village village)
        {
            if (village == null || !village.HasStorehouse) return;

            var pos = new BlockPos(village.StorehouseX, village.StorehouseY, village.StorehouseZ, 0);
            if (sapi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityStorehouse be
                && be.VillageId == village.Id)
            {
                be.Inventory?.Clear();
                sapi.World.BlockAccessor.SetBlock(0, pos);
            }
            village.HasStorehouse = false;
        }

        /// <summary>
        /// Pushes the ledger back into the crate, for when something changed the stores
        /// without going through the crate itself.
        /// </summary>
        public void RefreshStorehouse(Village village)
        {
            if (village == null || !village.HasStorehouse) return;

            var pos = new BlockPos(village.StorehouseX, village.StorehouseY, village.StorehouseZ, 0);
            if (sapi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityStorehouse be
                && be.VillageId == village.Id)
            {
                be.RebuildFromLedger();
            }
        }

        /// <summary>Takes the cairn back out of the world, if it is still there.</summary>
        public void ClearMarker(Village village)
        {
            if (village == null || !village.HasMarker) return;

            var pos = new BlockPos(village.MarkerX, village.MarkerY, village.MarkerZ, 0);
            if (sapi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityVillageCairn be
                && be.VillageId == village.Id)
            {
                sapi.World.BlockAccessor.SetBlock(0, pos);
            }
            village.HasMarker = false;
        }

        /// <summary>
        /// Every loaded villager standing inside this claim who belongs to nobody.
        /// Adoption is a deliberate act rather than something that happens on a timer,
        /// because a village claiming passers-by automatically would be wrong.
        /// </summary>
        public int AdoptUnaffiliated(Village village)
        {
            if (village == null) return 0;

            int adopted = 0;
            foreach (Entity e in sapi.World.LoadedEntities.Values)
            {
                if (e is not FFVillager villager) continue;
                if (villager.VillageId != 0) continue;
                if (!village.Contains(villager.Pos.AsBlockPos)) continue;

                Join(villager, village);
                adopted++;
            }
            return adopted;
        }

        // --- persistence -----------------------------------------------------------

        private class SaveData
        {
            [JsonProperty] public int Version = SaveFormatVersion;
            [JsonProperty] public long NextId = 1;
            [JsonProperty] public List<Village> Villages = new List<Village>();
        }

        private void OnSaveGameLoaded()
        {
            byId.Clear();
            loadedMembers.Clear();
            nextId = 1;

            try
            {
                byte[] raw = sapi.WorldManager.SaveGame.GetData(SaveKey);
                if (raw == null || raw.Length == 0)
                {
                    sapi.Logger.Notification("[F&F] No villages in this world yet.");
                    return;
                }

                SaveData data = JsonConvert.DeserializeObject<SaveData>(Encoding.UTF8.GetString(raw));
                if (data?.Villages == null)
                {
                    sapi.Logger.Warning("[F&F] Village save data was unreadable. Starting empty.");
                    return;
                }

                if (data.Version > SaveFormatVersion)
                {
                    sapi.Logger.Warning(
                        "[F&F] Village save data is version {0}, this build understands {1}. " +
                        "Loading anyway; anything newer will be ignored.",
                        data.Version, SaveFormatVersion);
                }

                foreach (Village v in data.Villages)
                {
                    if (v == null || v.Id <= 0) continue;
                    v.MemberIds ??= new List<long>();
                    v.Ledger ??= new VillageLedger();
                    v.Ledger.Grow();
                    v.Standing ??= new Dictionary<string, float>();
                    v.Facilities ??= new List<VillageFacility>();
                    byId[v.Id] = v;
                    loadedMembers[v.Id] = new List<FFVillager>();
                    RegisterFacilities(v);
                }

                // Never trust a saved counter over the data itself.
                nextId = Math.Max(data.NextId, 1);
                foreach (long id in byId.Keys) if (id >= nextId) nextId = id + 1;

                sapi.Logger.Notification("[F&F] Loaded {0} village(s).", byId.Count);
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[F&F] Village save data failed to load: {0}", e);
                sapi.Logger.Error("[F&F] Continuing with no villages so the world still opens.");
            }
        }

        private void OnGameWorldSave()
        {
            try
            {
                var data = new SaveData { Version = SaveFormatVersion, NextId = nextId };
                data.Villages.AddRange(byId.Values);

                string json = JsonConvert.SerializeObject(data, Formatting.None);
                sapi.WorldManager.SaveGame.StoreData(SaveKey, Encoding.UTF8.GetBytes(json));
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[F&F] Villages failed to save: {0}", e);
            }
        }
    }
}
