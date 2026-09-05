namespace FoundriesFrontiers
{
    /// <summary>
    /// The six pools a village keeps.
    ///
    /// Six is enough resolution to make decisions interesting without turning the mod
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
        Cloth = 5
    }

    public static class VillageResources
    {
        /// <summary>How many pools exist. Read this rather than hardcoding six.</summary>
        public const int Count = 6;

        public static readonly EnumVillageResource[] All =
        {
            EnumVillageResource.Food,
            EnumVillageResource.Wood,
            EnumVillageResource.Stone,
            EnumVillageResource.Clay,
            EnumVillageResource.Metal,
            EnumVillageResource.Cloth
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

        public static string Names => "food, wood, stone, clay, metal, cloth";
    }
}
