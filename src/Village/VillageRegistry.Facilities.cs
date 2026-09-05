using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Beds and workstations: finding them, owning them, handing them out.
    ///
    /// Split from the rest of the registry because it is a separate concern with its own
    /// lifecycle, not because it is a separate thing. It is still the same registry.
    /// </summary>
    public partial class VillageRegistry
    {
        private POIRegistry pois;

        /// <summary>Block code fragment to what it is, loaded from config/facilities.json.</summary>
        private readonly Dictionary<string, EnumTrade> workstationCodes =
            new Dictionary<string, EnumTrade>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> bedCodes = new List<string>();

        private void LoadFacilityConfig(ICoreServerAPI api)
        {
            pois = api.ModLoader.GetModSystem<POIRegistry>();
            if (pois == null) api.Logger.Warning("[F&F] POIRegistry missing. Bed lookups will be slow.");

            IAsset asset = api.Assets.TryGet(
                new AssetLocation(FoundriesFrontiersMod.ModId, "config/facilities.json"));

            if (asset == null)
            {
                api.Logger.Error("[F&F] config/facilities.json missing. No beds will be found.");
                return;
            }

            try
            {
                var root = JsonConvert.DeserializeObject<Dictionary<string, object>>(asset.ToText());

                if (root.TryGetValue("beds", out object bedsRaw))
                {
                    var beds = JsonConvert.DeserializeObject<Dictionary<string, int>>(bedsRaw.ToString());
                    foreach (string code in beds.Keys) bedCodes.Add(code.ToLowerInvariant());
                }

                if (root.TryGetValue("workstations", out object workRaw))
                {
                    var work = JsonConvert.DeserializeObject<Dictionary<string, string>>(workRaw.ToString());
                    foreach (var kv in work)
                    {
                        if (Enum.TryParse(kv.Value, true, out EnumTrade trade))
                        {
                            workstationCodes[kv.Key.ToLowerInvariant()] = trade;
                        }
                        else
                        {
                            api.Logger.Warning("[F&F] facilities.json names an unknown trade '{0}', ignoring it.", kv.Value);
                        }
                    }
                }

                api.Logger.Notification("[F&F] Facilities: {0} bed code(s), {1} workstation code(s).",
                    bedCodes.Count, workstationCodes.Count);
            }
            catch (Exception e)
            {
                api.Logger.Error("[F&F] facilities.json failed to parse: {0}", e.Message);
            }
        }

        // --- recognising things -----------------------------------------------------

        /// <summary>What a block at this position counts as, if anything.</summary>
        private bool Recognise(Block block, out EnumFacilityKind kind, out EnumTrade serves)
        {
            kind = EnumFacilityKind.Bed;
            serves = EnumTrade.Forager;

            string code = block?.Code?.Path?.ToLowerInvariant();
            if (string.IsNullOrEmpty(code)) return false;

            foreach (string bed in bedCodes)
            {
                if (code.Contains(bed)) { kind = EnumFacilityKind.Bed; return true; }
            }

            // Longest fragment wins, so "clayoven" beats "oven" and a specific station
            // is not shadowed by a general one just because of dictionary order.
            string bestFragment = null;
            foreach (var kv in workstationCodes)
            {
                if (code.Contains(kv.Key) && (bestFragment == null || kv.Key.Length > bestFragment.Length))
                {
                    bestFragment = kv.Key;
                    serves = kv.Value;
                }
            }

            if (bestFragment != null) { kind = EnumFacilityKind.Workstation; return true; }
            return false;
        }

        // --- scanning ---------------------------------------------------------------

        /// <summary>
        /// Walks a claim and records every bed and workstation in it.
        ///
        /// Deliberately not run on a timer. A full scan of a tier-5 claim is most of a
        /// million blocks, and beds do not move on their own: block placement and
        /// breaking keep the list current between scans. This is for founding, for a
        /// tier change, and for when a player asks.
        /// </summary>
        public int ScanFacilities(Village v)
        {
            if (v == null) return 0;

            foreach (VillageFacility old in v.Facilities) pois?.RemovePOI(old);

            // Ownership is worth keeping across a rescan. Losing everyone's bed because
            // somebody typed a command would be a poor way to behave.
            var owners = new Dictionary<string, long>();
            foreach (VillageFacility old in v.Facilities)
            {
                if (old.OwnerEntityId != 0) owners[old.X + "," + old.Y + "," + old.Z] = old.OwnerEntityId;
            }

            v.Facilities.Clear();

            int r = v.ClaimRadius;
            var cfg = FFConfig.Current.Village;
            var min = new BlockPos(v.CentreX - r, Math.Max(1, v.CentreY - cfg.FacilityScanDown), v.CentreZ - r, 0);
            var max = new BlockPos(v.CentreX + r, v.CentreY + cfg.FacilityScanUp, v.CentreZ + r, 0);

            sapi.World.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
            {
                if (!Recognise(block, out EnumFacilityKind kind, out EnumTrade serves)) return;

                var facility = new VillageFacility
                {
                    X = x, Y = y, Z = z,
                    Kind = kind,
                    Serves = serves,
                    BlockCode = block.Code?.ToShortString() ?? "",
                    VillageId = v.Id
                };

                if (owners.TryGetValue(x + "," + y + "," + z, out long owner)) facility.OwnerEntityId = owner;

                v.Facilities.Add(facility);
                pois?.AddPOI(facility);
            }, true);

            sapi.Logger.Notification("[F&F] {0}: found {1} bed(s) and {2} workstation(s).",
                v.Name, CountOf(v, EnumFacilityKind.Bed), CountOf(v, EnumFacilityKind.Workstation));

            return v.Facilities.Count;
        }

        /// <summary>Re-registers a loaded village's facilities with the point of interest registry.</summary>
        private void RegisterFacilities(Village v)
        {
            foreach (VillageFacility f in v.Facilities) pois?.AddPOI(f);
        }

        private void UnregisterFacilities(Village v)
        {
            foreach (VillageFacility f in v.Facilities) pois?.RemovePOI(f);
        }

        // --- keeping up with the world ----------------------------------------------

        /// <summary>
        /// A block changed somewhere. If it is inside a claim and it is something a
        /// village cares about, the list is corrected on the spot rather than waiting
        /// for the next scan.
        /// </summary>
        public void NoticeBlockChanged(BlockPos pos)
        {
            if (pos == null) return;

            Village v = VillageAt(pos);
            if (v == null) return;

            VillageFacility existing = FacilityAt(v, pos);
            Block block = sapi.World.BlockAccessor.GetBlock(pos);
            bool isFacility = Recognise(block, out EnumFacilityKind kind, out EnumTrade serves);

            if (existing != null && !isFacility)
            {
                pois?.RemovePOI(existing);
                v.Facilities.Remove(existing);
                if (existing.OwnerEntityId != 0)
                {
                    sapi.Logger.Notification("[F&F] {0} lost a {1} that somebody was using.",
                        v.Name, existing.Kind.ToString().ToLowerInvariant());
                }
                return;
            }

            if (existing == null && isFacility)
            {
                var facility = new VillageFacility
                {
                    X = pos.X, Y = pos.Y, Z = pos.Z,
                    Kind = kind,
                    Serves = serves,
                    BlockCode = block.Code?.ToShortString() ?? "",
                    VillageId = v.Id
                };
                v.Facilities.Add(facility);
                pois?.AddPOI(facility);
            }
        }

        public static VillageFacility FacilityAt(Village v, BlockPos pos)
        {
            foreach (VillageFacility f in v.Facilities)
            {
                if (f.X == pos.X && f.Y == pos.Y && f.Z == pos.Z) return f;
            }
            return null;
        }

        // --- ownership ---------------------------------------------------------------

        public static int CountOf(Village v, EnumFacilityKind kind)
        {
            int n = 0;
            foreach (VillageFacility f in v.Facilities) if (f.Kind == kind) n++;
            return n;
        }

        public static int FreeCountOf(Village v, EnumFacilityKind kind)
        {
            int n = 0;
            foreach (VillageFacility f in v.Facilities) if (f.Kind == kind && f.IsFree) n++;
            return n;
        }

        /// <summary>The bed belonging to this villager, or null.</summary>
        public static VillageFacility BedOf(Village v, long entityId)
        {
            if (v == null) return null;
            foreach (VillageFacility f in v.Facilities)
            {
                if (f.Kind == EnumFacilityKind.Bed && f.OwnerEntityId == entityId) return f;
            }
            return null;
        }

        /// <summary>
        /// Gives a villager a bed if there is one going, nearest to where they are.
        ///
        /// Nearest rather than first so a village that spreads out does not have everyone
        /// walking past three empty houses to reach the one at the far end.
        /// </summary>
        public VillageFacility AssignBed(Village v, FFVillager villager)
        {
            if (v == null || villager == null) return null;

            VillageFacility already = BedOf(v, villager.EntityId);
            if (already != null) return already;

            VillageFacility best = null;
            double bestDist = double.MaxValue;
            Vec3d at = villager.Pos.XYZ;

            foreach (VillageFacility f in v.Facilities)
            {
                if (f.Kind != EnumFacilityKind.Bed || !f.IsFree) continue;
                double d = f.Position.SquareDistanceTo(at);
                if (d < bestDist) { bestDist = d; best = f; }
            }

            if (best != null)
            {
                best.OwnerEntityId = villager.EntityId;
                sapi.Logger.Notification("[F&F] {0} took the bed at {1}.",
                    villager.GivenName == "" ? "#" + villager.EntityId : villager.GivenName, best.Pos);
            }
            return best;
        }

        /// <summary>Hands out beds to anyone in the village who has not got one.</summary>
        public int AssignBeds(Village v)
        {
            if (v == null) return 0;

            // A bed whose owner is gone is a free bed. Nobody is coming back for it.
            var alive = new HashSet<long>(v.MemberIds);
            foreach (VillageFacility f in v.Facilities)
            {
                if (f.OwnerEntityId != 0 && !alive.Contains(f.OwnerEntityId)) f.OwnerEntityId = 0;
            }

            int given = 0;
            foreach (FFVillager villager in LoadedMembers(v.Id))
            {
                if (BedOf(v, villager.EntityId) != null) continue;
                if (AssignBed(v, villager) != null) given++;
            }
            return given;
        }

        /// <summary>Whether the village has a workstation for this trade at all.</summary>
        public static bool HasStationFor(Village v, EnumTrade trade)
        {
            if (v == null) return false;
            foreach (VillageFacility f in v.Facilities)
            {
                if (f.Kind == EnumFacilityKind.Workstation && f.Serves == trade) return true;
            }
            return false;
        }

        /// <summary>A readable breakdown for the debug output.</summary>
        public static string DescribeFacilities(Village v)
        {
            int beds = CountOf(v, EnumFacilityKind.Bed);
            int freeBeds = FreeCountOf(v, EnumFacilityKind.Bed);

            var byTrade = new Dictionary<EnumTrade, int>();
            foreach (VillageFacility f in v.Facilities)
            {
                if (f.Kind != EnumFacilityKind.Workstation) continue;
                byTrade.TryGetValue(f.Serves, out int n);
                byTrade[f.Serves] = n + 1;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Beds       " + beds + " total, " + freeBeds + " free, "
                          + (beds - freeBeds) + " taken, population " + v.MemberIds.Count);

            if (byTrade.Count == 0)
            {
                sb.Append("Stations   none");
            }
            else
            {
                var parts = new List<string>();
                foreach (var kv in byTrade) parts.Add(kv.Value + "x " + kv.Key.ToString().ToLowerInvariant());
                sb.Append("Stations   " + string.Join(", ", parts));
            }
            return sb.ToString();
        }
    }
}
