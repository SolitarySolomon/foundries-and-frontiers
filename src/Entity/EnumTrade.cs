namespace FoundriesFrontiers
{
    /// <summary>
    /// Every trade a villager can be born to.
    ///
    /// Only the ones with real work implemented are wired up so far; the rest are
    /// declared now so that tier tables, aptitude matrices and save data do not have to
    /// be migrated every time a job is added.
    ///
    /// Order matters for persistence - append new trades at the end, never insert.
    /// </summary>
    public enum EnumTrade
    {
        // Tier 0
        Headman = 0,
        Builder = 1,
        Forager = 2,
        FireTender = 3,

        // Tier 1
        Farmer = 10,
        Herder = 11,
        Lumberjack = 12,
        Angler = 13,
        Cook = 14,

        // Tier 2
        Preserver = 20,
        Forester = 21,
        Weaver = 22,
        Clothier = 23,
        Tanner = 24,
        CharcoalBurner = 25,
        Spearman = 26,

        // Tier 3
        Quarrier = 30,
        Potter = 31,
        Smith = 32,
        Miner = 33,
        Trader = 34,
        Archer = 35,

        // Tier 4
        Mason = 40,
        Miller = 41,
        Baker = 42,
        Healer = 43,
        Swordsman = 44
    }
}
