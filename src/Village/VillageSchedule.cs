using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>What a villager should be doing with the hour it is.</summary>
    public enum EnumDayPhase
    {
        /// <summary>In bed, or trying to get there.</summary>
        Sleep = 0,

        /// <summary>Out and about, doing whatever their trade is.</summary>
        Work = 1,

        /// <summary>Awake but off duty: the hour either side of the working day.</summary>
        Rest = 2,

        /// <summary>Indoors and staying there. Overrides everything else.</summary>
        Shelter = 3
    }

    /// <summary>
    /// The clock every villager reads.
    ///
    /// One place decides what hour means what, so a job never has to reason about the
    /// time of day itself. Jobs ask whether it is a working hour and get on with it.
    ///
    /// Sheltering outranks the clock because a temporal storm does not care what time
    /// it is, and a village that keeps farming through one loses the farmer.
    /// </summary>
    public static class VillageSchedule
    {
        /// <summary>What this villager should be doing right now.</summary>
        public static EnumDayPhase PhaseFor(ICoreAPI api, BlockPos where)
        {
            if (IsStorming(api, where)) return EnumDayPhase.Shelter;

            var cfg = FFConfig.Current.Schedule;
            float hour = (float)api.World.Calendar.HourOfDay;

            if (hour >= cfg.WorkStartHour && hour < cfg.WorkEndHour) return EnumDayPhase.Work;
            if (hour >= cfg.SleepStartHour || hour < cfg.SleepEndHour) return EnumDayPhase.Sleep;

            return EnumDayPhase.Rest;
        }

        /// <summary>
        /// Whether a temporal storm is running badly enough to send people indoors.
        ///
        /// Read from the game's own stability system rather than from a timer of ours, so
        /// a village hides during exactly the storms a player would hide from.
        /// </summary>
        public static bool IsStorming(ICoreAPI api, BlockPos where)
        {
            var stability = api.ModLoader.GetModSystem<SystemTemporalStability>();
            if (stability?.StormData == null) return false;
            if (!stability.StormData.nowStormActive) return false;

            return stability.GetTemporalStability(where) < FFConfig.Current.Schedule.ShelterBelowStability;
        }

        public static string Describe(EnumDayPhase phase)
        {
            switch (phase)
            {
                case EnumDayPhase.Sleep: return "asleep";
                case EnumDayPhase.Work: return "working";
                case EnumDayPhase.Shelter: return "sheltering";
                default: return "resting";
            }
        }
    }
}
