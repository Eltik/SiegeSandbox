using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>A piece that can be shot or struck apart: a barricade plank or a soft-wall panel.</summary>
    public class Breakable : MonoBehaviour, IDamageable
    {
        public float health = 1f;
        public int debrisCount = 2;
        public Material debrisMaterial;

        public bool IsBroken { get; private set; }

        public void TakeDamage(DamageInfo info)
        {
            if (IsBroken) return;
            health -= info.amount;
            if (health <= 0f) Break(info.direction);
        }

        public void Break(Vector3 direction)
        {
            if (IsBroken) return;
            IsBroken = true;

            Collider col = GetComponent<Collider>();
            Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one * 0.1f);
            if (col != null) col.enabled = false; // gone for the very next bullet, not just next frame

            Material mat = debrisMaterial;
            if (mat == null)
            {
                Renderer r = GetComponent<Renderer>();
                if (r != null) mat = r.sharedMaterial;
            }
            Prims.Debris(b.center, b.extents, direction, debrisCount, mat);
            Destroy(gameObject);
        }
    }
}
