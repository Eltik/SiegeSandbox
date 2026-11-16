using UnityEngine;
using UnityEngine.InputSystem;

namespace SiegeSandbox
{
    /// <summary>
    /// Picture-in-picture third-person view of the player's hitbox, so you can watch it
    /// change as you crouch, go prone and lean. Toggle with H.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HitboxDebugCamera : MonoBehaviour
    {
        public PlayerController player;
        public Vector3 offset = new Vector3(1.6f, 1.5f, -3.4f);

        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) cam.enabled = !cam.enabled;
        }

        void LateUpdate()
        {
            if (player == null) return;
            Transform t = player.transform;
            transform.position = t.TransformPoint(offset);
            transform.LookAt(t.position + Vector3.up * 0.8f);
        }
    }
}
