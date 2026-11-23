namespace GodSim.Core.Fixtures
{
    public class STRING_HASH
    {
        public int Seed(string name) { return name.GetHashCode(); }
    }
}
