using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>A single approved building's glTFast-imported meshes, packaged together on demand.</summary>
    public sealed class BuildingArtModel : ScriptableObject
    {
        public Mesh Lod0;
        public Mesh Lod1;
        public Mesh Lod2;
    }
}
