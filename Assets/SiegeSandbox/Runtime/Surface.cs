using UnityEngine;

namespace SiegeSandbox
{
    public enum SurfaceKind { Hard, Wood, SoftWall }

    /// <summary>
    /// What a collider is made of. Anything without this component is treated as hard
    /// (bullets stop). Wood and soft walls let bullets through at reduced damage.
    /// </summary>
    public class Surface : MonoBehaviour
    {
        public SurfaceKind kind = SurfaceKind.Wood;
        [Range(0f, 1f)] public float damageKept = 0.75f;
    }
}
