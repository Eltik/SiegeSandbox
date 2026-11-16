using UnityEngine;
using UnityEngine.InputSystem;

namespace SiegeSandbox
{
    /// <summary>Minimal on-screen readout: crosshair that shows the real spread, ammo, stance, lean and hit markers.</summary>
    public class Hud : MonoBehaviour
    {
        public PlayerController player;
        public Weapon weapon;
        public Interactor interactor;
        public bool showHelp = true;

        float hitTimer;
        bool hitKill;
        string lastHit = "";
        float lastHitTime = -10f;
        GUIStyle label, right, center;
        int styledForHeight;

        void OnEnable() { Target.Damaged += OnDamaged; }
        void OnDisable() { Target.Damaged -= OnDamaged; }

        void OnDamaged(Target t, float amount, DamageZone zone, bool killed)
        {
            bool head = zone != null && zone.isHead;
            hitTimer = 0.2f;
            hitKill = killed;
            lastHit = head && killed ? "HEADSHOT"
                : (killed ? "KILL" : "HIT") + (zone != null ? "  " + zone.label : "") + "   -" + Mathf.RoundToInt(amount) + "   target hp " + Mathf.CeilToInt(Mathf.Max(0f, t.Health));
            lastHitTime = Time.time;
        }

        void Update()
        {
            hitTimer -= Time.deltaTime;
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) showHelp = !showHelp;
        }

        void EnsureStyles()
        {
            if (label != null && styledForHeight == Screen.height) return;
            styledForHeight = Screen.height;
            int size = Mathf.Max(11, Mathf.RoundToInt(Screen.height / 48f));
            label = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true };
            label.normal.textColor = Color.white;
            right = new GUIStyle(label) { alignment = TextAnchor.LowerRight, fontSize = Mathf.RoundToInt(size * 1.6f) };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        }

        static void Fill(float x, float y, float w, float h, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void Shadowed(Rect r, string text, GUIStyle style)
        {
            Color old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            style.normal.textColor = old;
            GUI.Label(r, text, style);
        }

        void OnGUI()
        {
            if (player == null || weapon == null || weapon.cam == null) return;
            EnsureStyles();

            float w = Screen.width, h = Screen.height;
            float cx = w * 0.5f, cy = h * 0.5f;
            float aim = weapon.AimBlend;
            float unit = h / 720f;

            // Crosshair: the gap is the actual spread cone projected onto the screen.
            float halfFov = weapon.cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float gap = Mathf.Tan(weapon.CurrentSpreadDeg * Mathf.Deg2Rad) / Mathf.Tan(halfFov) * cy + 3f * unit;
            float hipAlpha = (1f - aim) * (player.IsSprinting ? 0.25f : 1f);
            if (hipAlpha > 0.01f)
            {
                Color c = new Color(1f, 1f, 1f, 0.9f * hipAlpha);
                float len = 9f * unit, th = Mathf.Max(2f, 2f * unit);
                Fill(cx - gap - len, cy - th * 0.5f, len, th, c);
                Fill(cx + gap, cy - th * 0.5f, len, th, c);
                Fill(cx - th * 0.5f, cy - gap - len, th, len, c);
                Fill(cx - th * 0.5f, cy + gap, th, len, c);
            }
            if (aim > 0.01f)
            {
                float d = Mathf.Max(3f, 4f * unit);
                Fill(cx - d * 0.5f, cy - d * 0.5f, d, d, new Color(1f, 0.15f, 0.1f, aim));
            }

            // Hit marker: a small X, red on a kill.
            if (hitTimer > 0f)
            {
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
                Color c = hitKill ? new Color(1f, 0.2f, 0.2f, 1f) : Color.white;
                float len = 10f * unit, g = 7f * unit, th = Mathf.Max(2f, 2f * unit);
                Fill(cx - g - len, cy - th * 0.5f, len, th, c);
                Fill(cx + g, cy - th * 0.5f, len, th, c);
                Fill(cx - th * 0.5f, cy - g - len, th, len, c);
                Fill(cx - th * 0.5f, cy + g, th, len, c);
                GUI.matrix = m;
            }

            float pad = 16f * unit;
            float line = label.fontSize * 1.35f;

            // Ammo, bottom right.
            string ammo = weapon.IsReloading ? "RELOADING" : weapon.Ammo + " / " + weapon.magazineSize;
            Shadowed(new Rect(w - 420f * unit - pad, h - 80f * unit - pad, 420f * unit, 80f * unit), ammo, right);

            // State, bottom left.
            string lean = player.Lean > 0.3f ? "RIGHT" : (player.Lean < -0.3f ? "LEFT" : "-");
            Bounds hb = player.GetHitboxBounds();
            string state =
                "Stance: " + player.Stance.ToString().ToUpper() + "    Lean: " + lean + "\n" +
                "Hitbox: " + hb.size.y.ToString("0.00") + " m tall, " + hb.size.z.ToString("0.00") + " m long\n" +
                "Spray shot #" + weapon.ShotIndex + "    Spread " + weapon.CurrentSpreadDeg.ToString("0.00") + " deg" +
                (aim > 0.5f ? "    ADS" : "") + (player.IsSprinting ? "    SPRINT" : "") + (player.IsVaulting ? "    VAULT" : (player.IsGrounded ? "" : "    AIR"));
            Shadowed(new Rect(pad, h - line * 3.4f - pad, 900f * unit, line * 3.4f), state, label);

            if (Time.time - lastHitTime < 2f)
                Shadowed(new Rect(cx - 300f * unit, cy + 60f * unit, 600f * unit, line * 1.4f), lastHit, center);

            if (showHelp)
            {
                string help =
                    "<b>SIEGE SANDBOX</b>\n" +
                    "WASD  move        Shift  sprint\n" +
                    "Space  jump (into cover + W: climb)\n" +
                    "V  melee\n" +
                    "C  crouch          Ctrl  prone\n" +
                    "Q / E  lean left / right (toggle)\n" +
                    "Right mouse  aim down sights\n" +
                    "Left mouse  fire        R  reload\n" +
                    "Hold F  build / remove barricade\n" +
                    "H  hitbox camera    Tab  hide help\n" +
                    "Esc  free the cursor";
                Shadowed(new Rect(pad, pad, 520f * unit, line * 11.6f), help, label);
            }

            // Interaction prompt and progress bar.
            if (interactor != null && !string.IsNullOrEmpty(interactor.Prompt))
            {
                Shadowed(new Rect(cx - 300f * unit, cy + 110f * unit, 600f * unit, line * 1.4f), interactor.Prompt, center);
                if (interactor.IsBusy)
                {
                    float bw = 220f * unit, bh = 6f * unit;
                    Fill(cx - bw * 0.5f, cy + 150f * unit, bw, bh, new Color(0f, 0f, 0f, 0.6f));
                    Fill(cx - bw * 0.5f, cy + 150f * unit, bw * interactor.Progress, bh, Color.white);
                }
            }

            if (player.useDeviceInput && Cursor.lockState != CursorLockMode.Locked)
                Shadowed(new Rect(cx - 300f * unit, cy - 90f * unit, 600f * unit, line * 1.4f), "Click to play", center);
        }
    }
}
