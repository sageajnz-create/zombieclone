using UnityEngine;
using Unity.AI.Navigation;

namespace Overrun.Simulation
{
    /// <summary>
    /// Bakes the arena's navmesh at runtime.
    ///
    /// Runtime baking rather than a pre-baked asset because the arena scene is generated
    /// and edited by tooling — a stale baked surface would silently strand enemies with no
    /// path, which looks like an AI bug rather than a content bug. Baking on load costs a
    /// few milliseconds on a greybox arena and removes that whole failure class.
    ///
    /// Revisit when arenas get large: at that point bake offline and verify the asset is
    /// current in CI instead.
    /// </summary>
    [RequireComponent(typeof(NavMeshSurface))]
    public sealed class ArenaNavMesh : MonoBehaviour
    {
        private NavMeshSurface _surface;

        public bool IsBuilt { get; private set; }

        private void Awake() => _surface = GetComponent<NavMeshSurface>();

        private void Start() => Rebuild();

        public void Rebuild()
        {
            if (_surface == null) return;

            _surface.BuildNavMesh();
            IsBuilt = true;
            Debug.Log("[Overrun] Arena navmesh built.");
        }
    }
}
