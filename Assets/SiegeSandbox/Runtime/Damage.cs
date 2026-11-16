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

    public enum BulletSurfaceResult
    {
        NotMine,    // this collider is not part of the holed sheet (a stud, say): treat it normally
        Punched,    // the bullet made a new hole and carries on at reduced damage
        Open        // there was already a hole here: the bullet passes untouched
    }

    /// <summary>A sheet that keeps track of its own holes: soft walls and barricades.</summary>
    public interface IBulletSurface
    {
        BulletSurfaceResult OnBullet(Collider collider, Vector3 point, Vector3 direction);
        /// <summary>Is there already a hole in this collider's sheet at this point? (Sound asks this too.)</summary>
        bool IsOpenAt(Collider collider, Vector3 point);
    }
}
