using UnityEngine;
using UnityEngine.InputSystem;

namespace SiegeSandbox
{
    /// <summary>
    /// The in-game settings menu. Escape opens it and gives the mouse back; Escape again (or
    /// Resume) closes it and takes the mouse again. Every change applies at once and is saved.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        static SettingsMenu instance;
        GUIStyle title, heading, label, small, button, option, optionOn;
        Vector2 scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            IsOpen = false;
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null) return;
            GameObject go = new GameObject("[Settings]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SettingsMenu>();
            GameSettings.Load();
            GameSettings.Apply();
        }

        public static void Open()
        {
            IsOpen = true;
            MouseCapture.Release();
        }

        public static void Close()
        {
            IsOpen = false;
            PlayerController player = PlayerController.Instance;
            if (player != null && player.useDeviceInput)
            {
                MouseCapture.Take();
                player.SwallowClick(); // the click on Resume is not a shot
            }
        }

        void Update()
        {
            PlayerController player = PlayerController.Instance;
            if (player == null) return;
            GameSettings.Apply(); // cheap, and it catches a player or weapon that has just been loaded

            Keyboard kb = Keyboard.current;
            if (player.useDeviceInput && kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else Open();
            }
            if (IsOpen && !player.useDeviceInput) IsOpen = false; // scripted play (tests) never sits in a menu
        }

        void LateUpdate()
        {
            Weapon weapon = Weapon.Instance;
            if (weapon != null) GameSettings.ApplyAspect(weapon.cam);
        }

        // ------------------------------------------------------------------ drawing

        void EnsureStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = 12, wordWrap = true };
            small.normal.textColor = new Color(0.78f, 0.8f, 0.84f);
            heading = new GUIStyle(label) { fontSize = 13, fontStyle = FontStyle.Bold };
            heading.normal.textColor = new Color(1f, 0.78f, 0.2f);
            title = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 30f };
            option = new GUIStyle(GUI.skin.button) { fontSize = 13, fixedHeight = 28f, margin = new RectOffset(1, 1, 2, 2), padding = new RectOffset(2, 2, 2, 2) };
            option.normal.textColor = new Color(0.8f, 0.82f, 0.85f);
            optionOn = new GUIStyle(option) { fontStyle = FontStyle.Bold };
            optionOn.normal.textColor = Color.white;
            optionOn.hover.textColor = Color.white;
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void Heading(string text)
        {
            GUILayout.Space(7f);
            GUILayout.Label(text, heading);
        }

        int Slider(string name, int value, int min, int max, string unit)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(180f), GUILayout.Height(26f));
            GUILayout.BeginVertical();
            GUILayout.Space(9f);
            int result = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(300f)));
            GUILayout.EndVertical();
            GUILayout.Label(result + unit, label, GUILayout.Width(60f), GUILayout.Height(26f));
            GUILayout.EndHorizontal();
            return result;
        }

        int Choice(string name, int value, string[] options, float width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(180f), GUILayout.Height(30f));
            int result = value;
            float each = width / options.Length - 2f;
            for (int i = 0; i < options.Length; i++)
            {
                // The chosen option is lit amber, so the state reads at a glance.
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = i == value ? new Color(1f, 0.66f, 0.05f) : new Color(0.5f, 0.53f, 0.58f);
                if (GUILayout.Button(options[i], i == value ? optionOn : option, GUILayout.Width(each))) result = i;
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
            if (result != value) GUI.changed = true;
            return result;
        }

        bool Choice(string name, bool value, string whenTrue, string whenFalse)
        {
            return Choice(name, value ? 0 : 1, new[] { whenTrue, whenFalse }, 220f) == 0;
        }

        static string RawStatus()
        {
            if (!GameSettings.rawMouse) return "Using the system pointer, which includes macOS pointer acceleration.";
            if (Application.platform != RuntimePlatform.OSXEditor && Application.platform != RuntimePlatform.OSXPlayer)
                return "On this platform the mouse is already read without acceleration.";
            if (!RawMouseInput.Running) return "Starts the next time the game takes the mouse.";
            if (RawMouseInput.HasReported) return "Active: reading " + RawMouseInput.Devices + (RawMouseInput.Devices == 1 ? " mouse" : " mice") + " directly, acceleration off. A trackpad still uses the system pointer.";
            return "Ready, but the mouse has not reported yet (" + RawMouseInput.Devices + " found). Until it does, movement comes from the system pointer.";
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();

            float scale = Screen.height / 720f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;

            Fill(new Rect(0f, 0f, width, 720f), new Color(0f, 0f, 0f, 0.72f));
            Rect panel = new Rect((width - 640f) * 0.5f, 20f, 640f, 680f);
            Fill(panel, new Color(0.09f, 0.1f, 0.12f, 0.97f));

            GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 14f, panel.width - 48f, panel.height - 28f));
            GUILayout.Label("SETTINGS", title);
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(585f));
            GUI.changed = false;

            Heading("MOUSE");
            GameSettings.rawMouse = Choice("Mouse acceleration", !GameSettings.rawMouse, "On", "Off") == false;
            GUILayout.Label(RawStatus(), small);
            GameSettings.sensitivity = Slider("Sensitivity", GameSettings.sensitivity, 1, 100, "");
            GameSettings.adsSensitivity = Slider("Aiming sensitivity", GameSettings.adsSensitivity, 10, 100, "%");
            GameSettings.invertY = Choice("Vertical look", GameSettings.invertY, "Inverted", "Normal");

            Heading("CONTROLS");
            GameSettings.leanToggle = Choice("Lean (Q / E)", GameSettings.leanToggle, "Toggle", "Hold");
            GameSettings.aimToggle = Choice("Aim (right mouse)", GameSettings.aimToggle, "Toggle", "Hold");
            GameSettings.crouchToggle = Choice("Crouch (C)", GameSettings.crouchToggle, "Toggle", "Hold");

            Heading("DISPLAY");
            GameSettings.fov = Slider("Field of view", GameSettings.fov, GameSettings.MinFov, GameSettings.MaxFov, "°");
            GameSettings.aspect = Choice("Aspect ratio", GameSettings.aspect, GameSettings.AspectNames, 400f);
            if (GameSettings.aspect != 0)
                GameSettings.stretch = Choice("Fit to window", GameSettings.stretch, "Stretch", "Black bars");
            GameSettings.frameLimit = Choice("Frame rate limit", GameSettings.frameLimit, GameSettings.FrameLimitNames, 400f);

            Heading("SOUND");
            GameSettings.volume = Slider("Volume", GameSettings.volume, 0, 100, "%");

            if (GUI.changed)
            {
                GameSettings.Apply();
                GameSettings.Save();
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Resume  (Esc)", button, GUILayout.Width(170f))) Close();
            if (GUILayout.Button("Reset to defaults", button, GUILayout.Width(170f)))
            {
                GameSettings.ResetToDefaults();
                GameSettings.Apply();
                GameSettings.Save();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Quit", button, GUILayout.Width(90f))) Quit();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.matrix = old;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
