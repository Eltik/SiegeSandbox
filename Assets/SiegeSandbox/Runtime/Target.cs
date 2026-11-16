using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Practice dummy: takes damage, dies to a single headshot, topples over with the force
    /// of the killing hit, and comes back. Can strafe to practise leading shots.
    /// </summary>
    public class Target : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        public float respawnDelay = 3f;
        public GameObject visual;
        public Vector3 patrolOffset = Vector3.zero;
        public float patrolSpeed = 0f;

        public float Health { get; private set; }
        public bool IsAlive { get; private set; }

        /// <summary>(target, damage dealt, body part hit or null, was the killing hit)</summary>
        public static event System.Action<Target, float, DamageZone, bool> Damaged;

        Vector3 home;
        Renderer[] renderers;
        Color[] baseColors;
        float flash, respawnTimer, patrolPhase;

        void Awake()
        {
            home = transform.position;
            Health = maxHealth;
            IsAlive = true;
            GameObject root = visual != null ? visual : gameObject;
            renderers = root.GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].material.color;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;

            float amount = info.amount;
            // One bullet to the head is always a kill, even after passing through a wall.
            if (info.zone != null && info.zone.lethal && !info.melee) amount = Mathf.Max(amount, Health);

            Health -= amount;
            flash = 1f;
            bool killed = Health <= 0f;
            if (killed)
            {
                IsAlive = false;
                respawnTimer = respawnDelay;
                SpawnCorpse(info);
                if (visual != null) visual.SetActive(false);
            }
            if (Damaged != null) Damaged.Invoke(this, amount, info.zone, killed);
        }

        /// <summary>Leave a physics copy of the body behind that falls the way it was hit.</summary>
        void SpawnCorpse(DamageInfo info)
        {
            if (visual == null) return;
            GameObject corpse = Instantiate(visual, visual.transform.position, visual.transform.rotation);
            corpse.name = name + "_Corpse";
            foreach (Transform t in corpse.GetComponentsInChildren<Transform>()) t.gameObject.layer = Prims.IgnoreRaycastLayer;
            foreach (DamageZone z in corpse.GetComponentsInChildren<DamageZone>()) Destroy(z);

            Rigidbody rb = corpse.AddComponent<Rigidbody>();
            rb.mass = 60f;
            Vector3 dir = info.direction.sqrMagnitude > 0.0001f ? info.direction.normalized : -transform.forward;
            rb.AddForceAtPosition(dir * Mathf.Max(60f, info.impulse), info.point, ForceMode.Impulse);
            Destroy(corpse, respawnDelay);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (!IsAlive)
            {
                respawnTimer -= dt;
                if (respawnTimer <= 0f)
                {
                    Health = maxHealth;
                    IsAlive = true;
                    flash = 0f;
                    if (visual != null) visual.SetActive(true);
                    ApplyFlash();
                }
                return;
            }

            if (patrolSpeed > 0f && patrolOffset.sqrMagnitude > 0f)
            {
                patrolPhase += dt * patrolSpeed / patrolOffset.magnitude;
                transform.position = home + patrolOffset * Mathf.Sin(patrolPhase);
            }

            if (flash > 0f)
            {
                flash = Mathf.MoveTowards(flash, 0f, dt * 5f);
                ApplyFlash();
            }
        }

        void ApplyFlash()
        {
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].material.color = Color.Lerp(baseColors[i], Color.red, flash);
        }
    }
}
