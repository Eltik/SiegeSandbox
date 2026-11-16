using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Marks a collider as a body part with its own damage multiplier (limbs take less than
    /// the torso). A lethal zone (the head) kills in one bullet.
    /// </summary>
    public class DamageZone : MonoBehaviour
    {
        public string label = "body";
        public float multiplier = 1f;
        public bool isHead;
        public bool lethal;
    }
}
