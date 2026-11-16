using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>Hard is concrete or masonry, and is what anything without a Surface component counts as.</summary>
    public enum SurfaceKind { Hard, Wood, SoftWall, Metal, Glass }

    /// <summary>
    /// What a collider is made of. Bullets pass through wood, soft walls and glass at reduced
    /// damage and stop at everything else. Sound uses the same materials: each one takes its own
    /// amount of level and treble out of anything heard through it (see Sfx).
    /// </summary>
    public class Surface : MonoBehaviour
    {
        public SurfaceKind kind = SurfaceKind.Wood;
        [Range(0f, 1f)] public float damageKept = 0.75f;
    }
}
