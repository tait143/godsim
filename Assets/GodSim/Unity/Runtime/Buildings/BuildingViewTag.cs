using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Marks a pooled building view and names the simulation building it currently shows (-1 = none / ruin).
    /// It sits on the view root next to the selection <see cref="BoxCollider"/>, so selection code can map
    /// <c>hit.collider.GetComponent&lt;BuildingViewTag&gt;().BuildingId</c> to a building id.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingViewTag : MonoBehaviour
    {
        public int BuildingId = -1;
    }
}
