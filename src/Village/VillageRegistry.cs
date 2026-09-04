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
    public class VillageRegistry : ModSystem
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

            api.Event.OnEntityLoaded += OnEntityAppeared;
            api.Event.OnEntitySpawn += OnEntityAppeared;
            api.Event.OnEntityDespawn += OnEntityDespawn;
        }

        // --- the collection --------------------------------------------------------

        public IReadOnlyCollection<Village> All => byId.Values;

        public int Count => byId.Count;

        public Village Get(long id) => byId.TryGetValue(id, out Village v) ? v : null;

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
                CentreZ = centre.Z
            };

            byId[village.Id] = village;
            loadedMembers[village.Id] = new List<FFVillager>();

            sapi.Logger.Notification("[F&F] Founded {0} at {1}", village, centre);
            return village;
        }

        /// <summary>
        /// Removes a village and releases everyone in it. Members are not killed: they
        /// become unaffiliated, which is what an orphaned villager is.
        /// </summary>
        public bool Remove(long id)
        {
            if (!byId.TryGetValue(id, out Village v)) return false;

            foreach (FFVillager villager in LoadedMembers(id))
            {
                villager.VillageId = 0;
                villager.RefreshNameTag();
            }

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
                    v.Standing ??= new Dictionary<string, float>();
                    byId[v.Id] = v;
                    loadedMembers[v.Id] = new List<FFVillager>();
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
