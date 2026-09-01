using UnityEngine;
using Overrun.Core;
using Overrun.Data;

namespace Overrun.Simulation
{
    /// <summary>
    /// Server-side weapon resolution. Fire rate, ammo, reload, spread and recoil all
    /// happen here; the client only ever asks by holding the Fire button in its InputFrame.
    ///
    /// Presentation (muzzle flash, sound, recoil kick) fires immediately client-side on the
    /// button press — see Docs/NETWORKING.md §5. The player perceives zero input latency on
    /// the parts they can feel, while everything that matters stays authoritative.
    /// </summary>
    public sealed class WeaponRuntime : MonoBehaviour
    {
        [SerializeField] private WeaponDefinition _definition;
        [SerializeField] private LayerMask _hitMask = ~0;

        private StatBlock _stats;
        private RunContext _run;
        private PlayerId _owner = PlayerId.None;
        private DeterministicRandom _rng;

        private int _magazine;
        private int _reserve;
        private float _nextShotTime;
        private float _reloadCompleteTime;
        private bool _reloading;
        private Vector2 _recoil; // x = pitch degrees, y = yaw degrees

        public WeaponDefinition Definition => _definition;
        public int Magazine => _magazine;
        public int Reserve => _reserve;
        public bool IsReloading => _reloading;

        /// <summary>Current kick in degrees (pitch, yaw). Presentation reads this; it does not write it.</summary>
        public Vector2 RecoilEuler => _recoil;

        /// <summary>Server-only.</summary>
        public void ServerInitialise(WeaponDefinition definition, StatBlock stats, PlayerId owner, RunContext run)
        {
            _definition = definition != null ? definition : _definition;
            _stats = stats;
            _owner = owner;
            _run = run;

            if (_definition == null) return;

            ServerResetAmmo();

            // Crit rolls draw from a per-weapon generator seeded off the run seed rather
            // than from a named content stream. Combat rolls must not advance the streams
            // that pick augments or loot, or the run's content would depend on how many
            // shots were fired (ADR-006).
            ulong salt = (ulong)owner.ClientId * 1000003UL + owner.LocalSlot + 1UL;
            _rng = new DeterministicRandom(run != null ? run.Seed.Value ^ salt : salt);
        }

        public void ServerResetAmmo()
        {
            if (_definition == null) return;
            float magBase = _stats != null
                ? _stats.ResolveFor(_definition.MagazineSize, StatId.MagazineSize, _definition.Tags)
                : _definition.MagazineSize;
            _magazine = Mathf.RoundToInt(magBase);
            _reserve = _definition.ReserveAmmo;
            _reloading = false;
            _nextShotTime = 0f;
            _recoil = Vector2.zero;
        }

        /// <summary>Server-only. One fixed step of weapon behaviour driven by client intent.</summary>
        public void ServerTick(InputFrame frame, Transform aimOrigin, float now, float deltaTime)
        {
            if (_definition == null || _stats == null || aimOrigin == null) return;

            RecoverRecoil(deltaTime);

            if (_reloading)
            {
                if (now < _reloadCompleteTime) return;
                FinishReload();
            }

            if (frame.WasPressed(InputButton.Reload)) { BeginReload(now); return; }

            if (!frame.IsHeld(InputButton.Fire)) return;
            if (now < _nextShotTime) return;

            if (_magazine <= 0)
            {
                BeginReload(now);
                return;
            }

            Fire(aimOrigin, now);
        }

        private void Fire(Transform aimOrigin, float now)
        {
            Tag tags = _definition.Tags;

            float interval = _definition.SecondsPerShot;
            float rate = _stats.ResolveFor(1f, StatId.FireRate, tags);
            _nextShotTime = now + interval / Mathf.Max(0.05f, rate);

            _magazine--;

            ApplyRecoilKick();

            float range = _stats.ResolveFor(_definition.Range, StatId.Range, tags);
            float spread = Mathf.Max(0f, _definition.Spread);
            int pellets = Mathf.Max(1, _definition.PelletCount);

            Vector3 aim = RecoilAim(aimOrigin.forward);

            for (int i = 0; i < pellets; i++)
            {
                Vector3 direction = ApplySpread(aim, spread);
                TraceOne(aimOrigin.position, direction, range, tags);
            }
        }

        private void ApplyRecoilKick()
        {
            _recoil.x += _definition.RecoilPitch;
            float yaw = _definition.RecoilYaw;
            if (yaw > 0f && _rng != null) _recoil.y += (_rng.NextFloat() * 2f - 1f) * yaw;
            else _recoil.y += yaw;
        }

        private void RecoverRecoil(float deltaTime)
        {
            float recovery = _definition.RecoilRecovery * Mathf.Max(0f, deltaTime);
            if (recovery <= 0f) return;
            _recoil = Vector2.MoveTowards(_recoil, Vector2.zero, recovery);
        }

        private Vector3 RecoilAim(Vector3 forward)
        {
            if (_recoil.sqrMagnitude <= 0.0001f) return forward;
            return Quaternion.Euler(-_recoil.x, _recoil.y, 0f) * forward;
        }

        private void TraceOne(Vector3 origin, Vector3 direction, float range, Tag tags)
        {
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, range, _hitMask, QueryTriggerInteraction.Ignore))
                return;

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.IsDead) return;

            var hitPawn = hit.collider.GetComponentInParent<PlayerPawn>();
            if (hitPawn != null && hitPawn.Id.Equals(_owner)) return;

            float damage = _stats.ResolveFor(_definition.Damage, StatId.Damage, tags);

            float critChance = _stats.ResolveFor(_definition.CritChance, StatId.CritChance, tags);
            bool crit = _rng != null && _rng.Chance(critChance);
            if (crit)
            {
                damage *= _stats.ResolveFor(_definition.CritMultiplier, StatId.CritMultiplier, tags | Tag.Critical);
                tags |= Tag.Critical;
            }

            var context = new DamageContext();
            context.Set(_owner, tags, damage, hit.point);
            context.HitNormal = hit.normal;
            context.IsCritical = crit;

            damageable.ApplyDamage(context);
        }

        private Vector3 ApplySpread(Vector3 forward, float degrees)
        {
            if (degrees <= 0f || _rng == null) return forward;

            // Uniform disc sample so pellets do not cluster towards the centre.
            float angle = _rng.NextFloat() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(_rng.NextFloat()) * Mathf.Tan(degrees * Mathf.Deg2Rad);

            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            Vector3 up = Vector3.Cross(right, forward).normalized;

            return (forward + right * (Mathf.Cos(angle) * radius) + up * (Mathf.Sin(angle) * radius)).normalized;
        }

        private void BeginReload(float now)
        {
            if (_reloading || _reserve <= 0) return;

            int capacity = Mathf.RoundToInt(_stats.ResolveFor(_definition.MagazineSize, StatId.MagazineSize, _definition.Tags));
            if (_magazine >= capacity) return;

            float speed = Mathf.Max(0.1f, _stats.ResolveFor(1f, StatId.ReloadSpeed, _definition.Tags));
            _reloadCompleteTime = now + _definition.ReloadSeconds / speed;
            _reloading = true;
        }

        private void FinishReload()
        {
            _reloading = false;

            int capacity = Mathf.RoundToInt(_stats.ResolveFor(_definition.MagazineSize, StatId.MagazineSize, _definition.Tags));
            int needed = Mathf.Max(0, capacity - _magazine);
            int taken = Mathf.Min(needed, _reserve);

            _magazine += taken;
            _reserve -= taken;
        }
    }
}
