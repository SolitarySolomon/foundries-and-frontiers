using System;
using Newtonsoft.Json;

namespace FoundriesFrontiers
{
    /// <summary>
    /// A village's balance sheet: what it has, and what it has actually been earning.
    ///
    /// The important rule here, and the reason this class guards its own fields, is that
    /// <b>flow is measured, never granted</b>. Nothing may write a daily rate directly.
    /// The only way a number moves is a real deposit or a real withdrawal, and the daily
    /// figures are the sum of what happened. Fast-forward simulates an unloaded village
    /// from these rates, so the moment anything is allowed to invent a rate, a village
    /// left alone starts earning resources nobody ever carried anywhere.
    ///
    /// Stored as arrays indexed by EnumVillageResource. Compact in save data, and adding
    /// a seventh pool later just makes the arrays longer.
    /// </summary>
    public class VillageLedger
    {
        /// <summary>How many days of measured history the flow average runs over.</summary>
        public const int HistoryDays = 7;

        [JsonProperty] private float[] stock = new float[VillageResources.Count];

        /// <summary>Deposited so far today, per resource.</summary>
        [JsonProperty] private float[] inToday = new float[VillageResources.Count];

        /// <summary>Withdrawn or consumed so far today, per resource.</summary>
        [JsonProperty] private float[] outToday = new float[VillageResources.Count];

        /// <summary>
        /// Net movement per resource for each of the last few days, newest last.
        /// A jagged array rather than a ring buffer because it is written once a day and
        /// read by a human in the debug output, and clarity is worth more than the cycles.
        /// </summary>
        [JsonProperty] private System.Collections.Generic.List<float[]> history =
            new System.Collections.Generic.List<float[]>();

        /// <summary>Total ever deposited, per resource. Never decreases. For the record.</summary>
        [JsonProperty] private float[] lifetimeIn = new float[VillageResources.Count];

        // --- reading ---------------------------------------------------------------

        public float Get(EnumVillageResource r) => At(stock, r);

        public float DepositedToday(EnumVillageResource r) => At(inToday, r);

        public float SpentToday(EnumVillageResource r) => At(outToday, r);

        public float LifetimeDeposited(EnumVillageResource r) => At(lifetimeIn, r);

        /// <summary>Ignored on save: it is just the history length, and writing it twice
        /// only makes the save data bigger and easier to contradict itself.</summary>
        [JsonIgnore] public int DaysRecorded => history.Count;

        /// <summary>
        /// Average net movement per day over the recorded history. Positive means the
        /// village is accumulating, negative means it is eating into its stores.
        ///
        /// Returns 0 with no history rather than guessing, because a village that has
        /// existed for half a day has no trend and pretending otherwise is how a brand
        /// new settlement talks itself into a famine response.
        /// </summary>
        public float DailyFlow(EnumVillageResource r)
        {
            if (history.Count == 0) return 0;

            int i = (int)r;
            float sum = 0;
            int counted = 0;
            foreach (float[] day in history)
            {
                if (day == null || i >= day.Length) continue;
                sum += day[i];
                counted++;
            }
            return counted == 0 ? 0 : sum / counted;
        }

        /// <summary>
        /// How many days the current stock lasts at the current burn rate, or -1 when
        /// the pool is not shrinking. The number the starvation check reads.
        /// </summary>
        public float DaysOfStockLeft(EnumVillageResource r)
        {
            float flow = DailyFlow(r);
            if (flow >= -0.0001f) return -1;
            return Get(r) / -flow;
        }

        public bool CanAfford(EnumVillageResource r, float amount) => Get(r) >= amount;

        // --- writing ---------------------------------------------------------------

        /// <summary>
        /// Credits the pool. This is the only way resources enter a village, and it is
        /// called when a villager physically puts something down, not on a timer.
        /// </summary>
        public void Deposit(EnumVillageResource r, float amount)
        {
            if (amount <= 0) return;
            int i = (int)r;
            Grow();
            stock[i] += amount;
            inToday[i] += amount;
            lifetimeIn[i] += amount;
        }

        /// <summary>
        /// Spends from the pool if there is enough, and reports whether there was.
        ///
        /// Returns false rather than going negative on purpose: a builder who cannot
        /// draw materials must stall visibly at a half finished building, which is the
        /// rule the whole economy rests on.
        /// </summary>
        public bool Withdraw(EnumVillageResource r, float amount)
        {
            if (amount <= 0) return true;
            int i = (int)r;
            Grow();
            if (stock[i] < amount) return false;

            stock[i] -= amount;
            outToday[i] += amount;
            return true;
        }

        /// <summary>
        /// Spends as much as is available and reports how much that was. For consumption,
        /// where a village short of food eats what it has rather than refusing to eat.
        /// </summary>
        public float WithdrawUpTo(EnumVillageResource r, float amount)
        {
            if (amount <= 0) return 0;
            int i = (int)r;
            Grow();
            float taken = Math.Min(stock[i], amount);
            stock[i] -= taken;
            outToday[i] += taken;
            return taken;
        }

        /// <summary>
        /// Closes the day: files today's net movement into the history and starts a fresh
        /// pair of counters. Called once per in game day by the village day clock.
        /// </summary>
        public void RollDay()
        {
            Grow();

            var net = new float[VillageResources.Count];
            for (int i = 0; i < VillageResources.Count; i++)
            {
                net[i] = inToday[i] - outToday[i];
                inToday[i] = 0;
                outToday[i] = 0;
            }

            history.Add(net);
            while (history.Count > HistoryDays) history.RemoveAt(0);
        }

        /// <summary>
        /// Sets a pool outright. Only for the founding kit and for developer commands:
        /// nothing in the simulation may call this, because it writes stock without any
        /// corresponding work having happened.
        /// </summary>
        public void SetDirectly(EnumVillageResource r, float amount)
        {
            Grow();
            stock[(int)r] = Math.Max(0, amount);
        }

        // --- housekeeping ----------------------------------------------------------

        private static float At(float[] arr, EnumVillageResource r)
        {
            int i = (int)r;
            return arr != null && i < arr.Length ? arr[i] : 0;
        }

        /// <summary>
        /// Brings arrays up to the current pool count. A save written before a resource
        /// was added has shorter arrays, and the missing pools should read as empty
        /// rather than as an index out of range on load.
        /// </summary>
        public void Grow()
        {
            stock = Resize(stock);
            inToday = Resize(inToday);
            outToday = Resize(outToday);
            lifetimeIn = Resize(lifetimeIn);
            history ??= new System.Collections.Generic.List<float[]>();
        }

        private static float[] Resize(float[] arr)
        {
            if (arr != null && arr.Length == VillageResources.Count) return arr;
            var grown = new float[VillageResources.Count];
            if (arr != null) Array.Copy(arr, grown, Math.Min(arr.Length, grown.Length));
            return grown;
        }

        /// <summary>One line per pool: stock, today's movement, and the measured trend.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("           stock     today      /day");
            foreach (EnumVillageResource r in VillageResources.All)
            {
                float flow = DailyFlow(r);
                string today = (DepositedToday(r) - SpentToday(r)).ToString("+0.#;-0.#;0");
                sb.AppendLine(
                    r.ToString().ToLowerInvariant().PadRight(8)
                    + Get(r).ToString("0.#").PadLeft(8)
                    + today.PadLeft(10)
                    + flow.ToString("+0.#;-0.#;0").PadLeft(10));
            }
            sb.Append(history.Count == 0
                ? "No full day recorded yet, so every trend reads 0."
                : "Trend averaged over " + history.Count + " day(s).");
            return sb.ToString();
        }
    }
}
