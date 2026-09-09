namespace FoundriesFrontiers
{
    /// <summary>
    /// The pools a village keeps.
    ///
    /// This many is enough resolution to make decisions interesting without turning the mod
    /// into an accounting simulator. A village that is short of clay behaves differently
    /// from one that is short of food; a village short of "resource 14" does not.
    ///
    /// Values are explicit and permanent. They index the ledger's arrays and are written
    /// into save data, so a value may be appended but never reordered or reused.
    /// </summary>
    public enum EnumVillageResource
    {
        /// <summary>Anything anyone can eat. The pool that kills a village when it runs out.</summary>
        Food = 0,

        /// <summary>Logs, planks, firewood. Fuel and building material both.</summary>
        Wood = 1,

        /// <summary>Rock, cobble, gravel. Walls and foundations.</summary>
        Stone = 2,

        /// <summary>Raw clay and fired ceramic. Bricks, pots, crucibles.</summary>
        Clay = 3,

        /// <summary>Ore, nuggets, ingots. The one pool a village can never fully close.</summary>
        Metal = 4,

        /// <summary>Flax, wool, linen. Clothing, and warmth through a winter.</summary>
        Cloth = 5,

        /// <summary>
        /// Soil, sand, dry grass, peat. Dug, not mined.
        ///
        /// This is its own pool because pool value is fungible and dirt must not be
        /// spendable on pottery. A village that dug two hundred units of earth into the
        /// clay pool could afford a kiln without owning a scrap of usable clay, which is
        /// nonsense. Folding it into stone fails the same way.
        ///
        /// The case for it is fields rather than walls. Earth is daub infill at tier 1
        /// and cob at tier 2 and then stops mattering for building; what does not stop is
        /// farmland, where soil grade is a five step ladder that runs the whole game and
        /// is the one thing a village on poor ground cannot dig its way out of.
        /// </summary>
        Earth = 6
    }

    public static class VillageResources
    {
        /// <summary>How many pools exist. Read this rather than hardcoding the number.</summary>
        public const int Count = 7;

        public static readonly EnumVillageResource[] All =
        {
            EnumVillageResource.Food,
            EnumVillageResource.Wood,
            EnumVillageResource.Stone,
            EnumVillageResource.Clay,
            EnumVillageResource.Metal,
            EnumVillageResource.Cloth,
            EnumVillageResource.Earth
        };

        /// <summary>Parses a resource by name, for commands and config. Null if unknown.</summary>
        public static EnumVillageResource? Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (EnumVillageResource r in All)
            {
                if (string.Equals(r.ToString(), name.Trim(), System.StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        public static string Names => "food, wood, stone, clay, metal, cloth, earth";
    }
}
