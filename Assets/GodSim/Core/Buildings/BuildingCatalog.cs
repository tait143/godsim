namespace GodSim.Core.Buildings
{
    /// <summary>
    /// Registers every building definition. The catalog is split across three content teams by era:
    /// <see cref="BuildingsEarly"/> (Dawn–Iron, types 0–27), <see cref="BuildingsMiddle"/> (Classical–Renaissance,
    /// types 28–47 and shared wonders 72–74) and <see cref="BuildingsLate"/> (Industrial–Ascension and the five
    /// racial wonders, types 48–71).
    /// </summary>
    public static class BuildingCatalog
    {
        public static void Register(GameDatabase db)
        {
            Guard.NotNull(db, nameof(db));
            BuildingsEarly.Register(db);
            BuildingsMiddle.Register(db);
            BuildingsLate.Register(db);
        }
    }
}
