namespace GodSim.Core.Religion
{
    /// <summary>
    /// Stable numeric ids of every doctrine. Stored as floats in <c>Civilization.Memory["rel.doctrine.&lt;axis&gt;"]</c>,
    /// so values must never be reordered or removed; only append.
    /// </summary>
    public enum DoctrineId : byte
    {
        // Worship
        SunWorship = 0,
        AncestorVeneration = 1,
        NatureSpirits = 2,
        CelestialOrder = 3,
        // Ethos
        Mercy = 4,
        MartialZeal = 5,
        Asceticism = 6,
        ProsperityGospel = 7,
        // Practice
        Pilgrimage = 8,
        Monasticism = 9,
        Sacrifice = 10,
        Festivals = 11,
        // View of the God
        LovingParent = 12,
        WrathfulJudge = 13,
        DistantWatcher = 14,
        Trickster = 15
    }
}
