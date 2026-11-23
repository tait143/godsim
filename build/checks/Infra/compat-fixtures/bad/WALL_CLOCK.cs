namespace GodSim.Core.Fixtures
{
    public class WALL_CLOCK
    {
        public long Stamp() { return System.DateTime.UtcNow.Ticks; }
    }
}
