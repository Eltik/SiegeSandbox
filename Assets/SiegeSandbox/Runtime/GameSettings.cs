using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// The player's settings: what they are, their defaults, saving them between sessions and
    /// applying them to the game. Changed from the in-game menu (Escape).
    /// </summary>
    public static class GameSettings
    {
        // ------------------------------------------------------------------ controls
        /// <summary>Use the mouse's own movement, with no pointer acceleration.</summary>
        public static bool rawMouse = true;
        /// <summary>1 to 100. 50 is the sandbox's original speed.</summary>
        public static int sensitivity = 50;
        /// <summary>How fast you turn while aiming, as a percentage of normal.</summary>
        public static int adsSensitivity = 65;
        public static bool invertY = false;
        /// <summary>True: tap Q/E to lean and tap again to straighten. False: lean only while the key is held.</summary>
        public static bool leanToggle = true;
        /// <summary>True: click to aim and click again to stop. False: aim only while the button is held.</summary>
        public static bool aimToggle = false;
        /// <summary>True: tap C to crouch and tap again to stand. False: crouch only while the key is held.</summary>
        public static bool crouchToggle = true;

        // ------------------------------------------------------------------ display
        /// <summary>Vertical field of view in degrees, as in Siege (60 to 90 there; a little more allowed here).</summary>
        public static int fov = 78;
        /// <summary>Index into AspectNames. 0 follows the window.</summary>
        public static int aspect = 0;
        /// <summary>With a fixed aspect ratio: stretch the picture to fill the window (true) or add black bars (false).</summary>
        public static bool stretch = true;
        /// <summary>Index into FrameLimitNames.</summary>
        public static int frameLimit = 0;

        // ------------------------------------------------------------------ sound
        public static int volume = 100;

        public static readonly string[] AspectNames = { "Auto", "4:3", "5:4", "3:2", "16:10", "16:9", "21:9" };
        static readonly float[] AspectValues = { 0f, 4f / 3f, 5f / 4f, 3f / 2f, 16f / 10f, 16f / 9f, 21f / 9f };
        public static readonly string[] FrameLimitNames = { "Unlimited", "60", "120", "144", "240" };
        static readonly int[] FrameLimitValues = { -1, 60, 120, 144, 240 };

        public const int MinFov = 60, MaxFov = 100;
        /// <summary>Degrees turned per mouse count at sensitivity 50.</summary>
        public const float BaseSensitivity = 0.08f;
        /// <summary>The aimed view is this fraction of the normal field of view.</summary>
        public const float AimZoom = 50f / 78f;

        /// <summary>The chosen aspect ratio as width over height, or 0 to follow the window.</summary>
        public static float AspectValue { get { return AspectValues[Mathf.Clamp(aspect, 0, AspectValues.Length - 1)]; } }

        public static void ResetToDefaults()
        {
            rawMouse = true; sensitivity = 50; adsSensitivity = 65; invertY = false;
            leanToggle = true; aimToggle = false; crouchToggle = true;
            fov = 78; aspect = 0; stretch = true; frameLimit = 0; volume = 100;
        }

        public static void Load()
        {
            ResetToDefaults();
            rawMouse = PlayerPrefs.GetInt("siege.rawMouse", rawMouse ? 1 : 0) != 0;
            sensitivity = Mathf.Clamp(PlayerPrefs.GetInt("siege.sensitivity", sensitivity), 1, 100);
            adsSensitivity = Mathf.Clamp(PlayerPrefs.GetInt("siege.adsSensitivity", adsSensitivity), 10, 100);
            invertY = PlayerPrefs.GetInt("siege.invertY", invertY ? 1 : 0) != 0;
            leanToggle = PlayerPrefs.GetInt("siege.leanToggle", leanToggle ? 1 : 0) != 0;
            aimToggle = PlayerPrefs.GetInt("siege.aimToggle", aimToggle ? 1 : 0) != 0;
            crouchToggle = PlayerPrefs.GetInt("siege.crouchToggle", crouchToggle ? 1 : 0) != 0;
            fov = Mathf.Clamp(PlayerPrefs.GetInt("siege.fov", fov), MinFov, MaxFov);
            aspect = Mathf.Clamp(PlayerPrefs.GetInt("siege.aspect", aspect), 0, AspectNames.Length - 1);
            stretch = PlayerPrefs.GetInt("siege.stretch", stretch ? 1 : 0) != 0;
            frameLimit = Mathf.Clamp(PlayerPrefs.GetInt("siege.frameLimit", frameLimit), 0, FrameLimitNames.Length - 1);
            volume = Mathf.Clamp(PlayerPrefs.GetInt("siege.volume", volume), 0, 100);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("siege.rawMouse", rawMouse ? 1 : 0);
            PlayerPrefs.SetInt("siege.sensitivity", sensitivity);
            PlayerPrefs.SetInt("siege.adsSensitivity", adsSensitivity);
            PlayerPrefs.SetInt("siege.invertY", invertY ? 1 : 0);
            PlayerPrefs.SetInt("siege.leanToggle", leanToggle ? 1 : 0);
            PlayerPrefs.SetInt("siege.aimToggle", aimToggle ? 1 : 0);
            PlayerPrefs.SetInt("siege.crouchToggle", crouchToggle ? 1 : 0);
            PlayerPrefs.SetInt("siege.fov", fov);
            PlayerPrefs.SetInt("siege.aspect", aspect);
            PlayerPrefs.SetInt("siege.stretch", stretch ? 1 : 0);
            PlayerPrefs.SetInt("siege.frameLimit", frameLimit);
            PlayerPrefs.SetInt("siege.volume", volume);
            PlayerPrefs.Save();
        }

        /// <summary>Push the current values into the game. Safe to call at any time.</summary>
        public static void Apply()
        {
            PlayerController player = PlayerController.Instance;
            if (player != null)
            {
                player.mouseSensitivity = BaseSensitivity * sensitivity / 50f;
                player.adsSensitivityScale = adsSensitivity / 100f;
                player.toggleLean = leanToggle;
            }
            Weapon weapon = Weapon.Instance;
            if (weapon != null)
            {
                weapon.hipFov = fov;
                weapon.adsFov = fov * AimZoom;
            }
            Sfx.MasterVolume = volume / 100f;
            int limit = FrameLimitValues[Mathf.Clamp(frameLimit, 0, FrameLimitValues.Length - 1)];
            if (limit > 0) QualitySettings.vSyncCount = 0; // a frame limit only holds with v-sync off
            Application.targetFrameRate = limit;
            if (rawMouse) { if (MouseCapture.Active) RawMouseInput.Start(); }
            else RawMouseInput.Stop();
        }

        /// <summary>Shape the camera's picture to the chosen aspect ratio: stretched to fill, or with black bars.</summary>
        public static void ApplyAspect(Camera cam)
        {
            if (cam == null) return;
            float target = AspectValue;
            float window = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            Rect full = new Rect(0f, 0f, 1f, 1f);
            if (target <= 0f || Mathf.Abs(target - window) < 0.01f)
            {
                if (cam.rect != full) cam.rect = full;
                cam.ResetAspect();
            }
            else if (stretch)
            {
                if (cam.rect != full) cam.rect = full;
                cam.aspect = target; // drawn for this shape, then pulled to fit the window
            }
            else
            {
                Rect r = target < window
                    ? new Rect((1f - target / window) * 0.5f, 0f, target / window, 1f)
                    : new Rect(0f, (1f - window / target) * 0.5f, 1f, window / target);
                if (cam.rect != r) cam.rect = r;
                cam.ResetAspect();
            }
        }
    }
}
