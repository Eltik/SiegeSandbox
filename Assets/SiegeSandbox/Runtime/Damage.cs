using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>Everything a hit carries: how hard, where, from which direction, and which body part.</summary>
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 direction;   // the way the bullet or strike was travelling
        public DamageZone zone;
        public bool melee;
        public float impulse;       // push applied to physics objects, in newton-seconds
    }

    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
    }
}
