using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// A bullet that actually flies: it moves at its velocity each frame, drops with gravity,
    /// and only deals damage when it reaches something. Each frame's travel is swept with a
    /// raycast so fast bullets cannot tunnel through thin walls. Wood and soft walls
    /// (see Surface) are punched through at reduced damage; everything else stops the bullet.
    /// Damage also falls off with the distance flown.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public const int MaxPenetrations = 4;
        public const float MinDamage = 4f;

        public static int ActiveCount { get; private set; }

        Vector3 velocity;
        float damage, gravityScale, life, traveled;
        float falloffStart, falloffEnd, falloffMinScale = 1f;
        int penetrations;
        Material impactMaterial;
        Renderer rend;

        public static Projectile Spawn(Vector3 position, Vector3 velocity, float damage, float gravityScale,
            Material tracerMaterial, Material impactMaterial)
        {
            GameObject go = new GameObject("Projectile");
            go.layer = Prims.IgnoreRaycastLayer;
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(velocity.sqrMagnitude > 0f ? velocity : Vector3.forward);
            go.transform.localScale = new Vector3(0.025f, 0.025f, 0.7f);
            go.AddComponent<MeshFilter>().sharedMesh = Prims.CubeMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (tracerMaterial != null) mr.sharedMaterial = tracerMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.enabled = false; // hidden until it clears the muzzle

            Projectile p = go.AddComponent<Projectile>();
            p.velocity = velocity;
            p.damage = damage;
            p.gravityScale = gravityScale;
            p.life = 4f;
            p.impactMaterial = impactMaterial;
            p.rend = mr;
            return p;
        }

        /// <summary>
        /// Damage fall-off: full damage up to 'start' metres, dropping in a straight line to
        /// 'minScale' of it at 'end' metres and beyond. Without this call there is no fall-off.
        /// </summary>
        public void SetFalloff(float start, float end, float minScale)
        {
            falloffStart = start;
            falloffEnd = end;
            falloffMinScale = minScale;
        }

        public static float FalloffScale(float distance, float start, float end, float minScale)
        {
            if (end <= start) return distance >= end && end > 0f ? minScale : 1f;
            return Mathf.Lerp(1f, minScale, Mathf.InverseLerp(start, end, distance));
        }

        void OnEnable() { ActiveCount++; }
        void OnDisable() { ActiveCount--; }

        void Update()
        {
            float dt = Time.deltaTime;
            velocity += Physics.gravity * (gravityScale * dt);

            float remaining = velocity.magnitude * dt;
            if (remaining > 0.0001f)
            {
                Vector3 dir = velocity.normalized;
                Vector3 pos = transform.position;
                int guard = 0;
                while (remaining > 0.0001f && guard++ < MaxPenetrations + 2)
                {
                    RaycastHit hit;
                    if (!Physics.Raycast(pos, dir, out hit, remaining, PlayerController.WorldMask, QueryTriggerInteraction.Ignore))
                    {
                        pos += dir * remaining;
                        traveled += remaining;
                        break;
                    }

                    traveled += hit.distance;
                    remaining -= hit.distance;
                    if (Impact(hit, dir)) return; // stopped here

                    // Punched through: carry on from just inside the surface (a ray never hits a collider it starts in).
                    pos = hit.point + dir * 0.01f;
                    remaining -= 0.01f;
                }
                transform.position = pos;
                transform.rotation = Quaternion.LookRotation(dir);
            }

            if (rend != null && !rend.enabled && traveled > 2f) rend.enabled = true;

            life -= dt;
            if (life <= 0f) Destroy(gameObject);
        }

        /// <summary>Deal with whatever was hit. Returns true if the bullet stops.</summary>
        bool Impact(RaycastHit hit, Vector3 dir)
        {
            Collider col = hit.collider;

            // Soft walls and barricades keep their own holes. Through an existing hole the bullet is untouched.
            BulletSurfaceResult sheet = BulletSurfaceResult.NotMine;
            IBulletSurface holed = col.GetComponentInParent<IBulletSurface>();
            if (holed != null) sheet = holed.OnBullet(col, hit.point, dir);
            if (sheet == BulletSurfaceResult.Open) return false;

            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                DamageZone zone = col.GetComponent<DamageZone>();
                DamageInfo info = new DamageInfo();
                info.amount = damage * FalloffScale(traveled, falloffStart, falloffEnd, falloffMinScale)
                    * (zone != null ? zone.multiplier : 1f);
                info.point = hit.point;
                info.direction = dir;
                info.zone = zone;
                info.impulse = 70f;
                target.TakeDamage(info);
            }

            Surface surface = col.GetComponentInParent<Surface>();
            if (surface != null && surface.kind != SurfaceKind.Hard && penetrations < MaxPenetrations)
            {
                penetrations++;
                damage *= surface.damageKept;
                if (damage >= MinDamage)
                {
                    if (target == null && sheet != BulletSurfaceResult.Punched) Mark(hit, 0.04f); // a bullet hole in solid wood
                    return false;
                }
            }

            if (target == null && sheet != BulletSurfaceResult.Punched) Mark(hit, 0.06f);
            Destroy(gameObject);
            return true;
        }

        void Mark(RaycastHit hit, float size)
        {
            GameObject mark = Prims.Cube("Impact", null, hit.point + hit.normal * 0.01f, new Vector3(size, size, 0.02f), impactMaterial, false);
            mark.layer = Prims.IgnoreRaycastLayer;
            mark.transform.rotation = Quaternion.LookRotation(hit.normal);
            mark.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Destroy(mark, 8f);
        }
    }
}
