namespace GodSim.Core.Buildings
{
    /// <summary>Small constructors shared by the BuildingsMiddle definition files (keeps the tables readable).</summary>
    internal static class MiddleBuildingKit
    {
        /// <summary>Resource amount (cost, input or output).</summary>
        internal static ResourceAmount Res(ResourceType type, float amount) => new ResourceAmount(type, amount);

        /// <summary>Service amount (points per tick for a fully staffed building).</summary>
        internal static ServiceAmount Svc(ServiceType type, float amount) => new ServiceAmount(type, amount);

        /// <summary>Packs an opaque 0xRRGGBB tint into the 0xRRGGBBAA format used by renderers.</summary>
        internal static uint Tint(uint rrggbb) => MathUtil.Hex(rrggbb);
    }
}
