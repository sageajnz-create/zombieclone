using UnityEngine;

namespace Overrun.Simulation
{
    /// <summary>
    /// Greybox bulkhead. Costs scrip, unlocks a region (and that region's spawn zones),
    /// and drops its collider so the navmesh can be rebaked through the opening.
    /// </summary>
    public sealed class PurchasableDoor : MonoBehaviour, IInteractable
    {
        [SerializeField] private int _cost = 80;
        [SerializeField] private int _unlocksRegion = 1;
        [SerializeField] private string _displayName = "Site Bulkhead";
        [SerializeField] private float _interactRange = 3.2f;

        private Collider[] _colliders;
        private Renderer[] _renderers;
        private RunContext _run;

        public int Cost => _cost;
        public int UnlocksRegion => _unlocksRegion;
        public bool IsOpen { get; private set; }
        public string Prompt => IsOpen ? string.Empty : _displayName + "  " + _cost + " scrip  [Interact]";
        public bool IsAvailable => !IsOpen;
        public float InteractRange => _interactRange;

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>();
            _renderers = GetComponentsInChildren<Renderer>();
        }

        public void ServerInitialise(RunContext run, int cost, int region, string displayName)
        {
            _run = run;
            _cost = cost;
            _unlocksRegion = region;
            if (!string.IsNullOrEmpty(displayName)) _displayName = displayName;
            if (_colliders == null) _colliders = GetComponentsInChildren<Collider>();
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>();
        }

        public bool TryInteract(PlayerState player)
        {
            if (IsOpen || player == null || !player.IsAlive) return false;
            if (!player.TrySpendScrip(_cost)) return false;

            ServerOpen();
            return true;
        }

        public void ServerOpen()
        {
            if (IsOpen) return;
            IsOpen = true;
            SetSolid(false);
            if (_run != null) _run.UnlockRegion(_unlocksRegion);
        }

        public void ServerReset()
        {
            IsOpen = false;
            SetSolid(true);
        }

        private void SetSolid(bool solid)
        {
            if (_colliders != null)
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i] != null) _colliders[i].enabled = solid;
                }
            }

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null) _renderers[i].enabled = solid;
                }
            }
        }
    }
}
