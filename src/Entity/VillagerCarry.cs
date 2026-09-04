using System;
using Vintagestory.API.Common;

namespace FoundriesFrontiers
{
    /// <summary>
    /// What a villager is carrying and what they are holding to work with.
    ///
    /// Two separate ideas that both live in the hands:
    ///
    ///  - The **carried load** is goods in transit - logs on the way to the storehouse,
    ///    grain on the way to a trough. It is the middle leg of every job in Phase B.
    ///  - The **tool** is what they work with, and its tier multiplies how fast they work.
    ///    That feedback loop is what makes a village accelerate as it advances: copper
    ///    tools mean faster building, which means the forge arrives sooner.
    ///
    /// Both persist, because a villager who logs out mid-haul should not silently destroy
    /// what they were carrying.
    /// </summary>
    public static class VillagerCarry
    {
        /// <summary>How much a villager can carry in one trip, in items.</summary>
        public static int CarryCapacity => FFConfig.Current.Villager.CarryCapacity;

        /// <summary>
        /// Work rate by tool tier. Tier 0 is bare hands or flint; each step up is a real
        /// improvement but with diminishing returns, so the jump from nothing to copper
        /// matters more than copper to iron.
        /// </summary>
        public static float WorkRateForToolTier(int tier)
        {
            float[] table = FFConfig.Current.Villager.WorkRateByToolTier;
            if (table == null || table.Length == 0) return 1f;
            if (tier < 0) return table[0];
            if (tier >= table.Length) return table[table.Length - 1];
            return table[tier];
        }

        /// <summary>
        /// The tier of whatever a villager is holding, or 0 for nothing useful.
        /// Reads the game's own ToolTier rather than maintaining a parallel table.
        /// </summary>
        public static int ToolTierOf(ItemStack stack)
        {
            if (stack?.Collectible == null) return 0;
            if (stack.Collectible.Tool == null) return 0;
            return Math.Max(0, stack.Collectible.ToolTier);
        }
    }
}
