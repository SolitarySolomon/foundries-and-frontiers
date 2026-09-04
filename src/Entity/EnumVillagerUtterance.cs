namespace FoundriesFrontiers
{
    /// <summary>
    /// What a villager is doing when they make a noise.
    ///
    /// Kept separate from the engine's EnumTalkType so the mod can decide what a greeting
    /// or a passing remark should sound like without being limited to the trader
    /// vocabulary, and so the wire format stays ours.
    /// </summary>
    public enum EnumVillagerUtterance
    {
        Greet = 0,
        Remark = 1,
        Laugh = 2,
        Shrug = 3,
        Complain = 4,
        Hurt = 5,
        Death = 6
    }
}
