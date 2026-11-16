using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SiegeSandbox
{
    /// <summary>
    /// Editor-only flight recorder for mouse input: once a second it adds a line to
    /// Logs/input-diagnostics.txt saying what the mouse reported and what the game did with it.
    /// It is there so an input problem can be read off afterwards instead of guessed at.
    /// </summary>
    public static class InputDiagnostics
    {
#if UNITY_EDITOR
        const int MaxLines = 900;
        static string path;
        static int lines, frames, quietFrames, heldFrames, session;
        static float nextWrite, quietSum, heldSum, positionSum, turned, rawSum;
        static int rawFrames;
        static Vector2 lastPosition;
        static float lastYaw;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Begin()
        {
            path = null;
        }

        static void Open()
        {
            path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs/input-diagnostics.txt");
            lines = 0;
            session++;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# session started " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  Unity " + Application.unityVersion + "  " + SystemInfo.operatingSystem);
            sb.Append("# devices:");
            foreach (InputDevice d in InputSystem.devices) sb.Append("  [" + d.deviceId + "] " + d.displayName + " (" + d.layout + ")");
            sb.AppendLine();
            sb.AppendLine("# time  focus  lock  capture  frames | moving with no button: frames, counts | with a button: frames, counts | pointer travel px | warps | view turned deg | screen | raw input: mice, frames, counts");
            try { System.IO.File.WriteAllText(path, sb.ToString()); } catch (System.Exception) { path = ""; }
            nextWrite = Time.unscaledTime + 1f;
        }
#endif

        public static void Tick(Mouse mouse, float yaw)
        {
#if UNITY_EDITOR
            if (path == null) Open();
            if (string.IsNullOrEmpty(path) || mouse == null || lines >= MaxLines) return;

            Vector2 delta = MouseCapture.RawDelta;
            Vector2 position = mouse.position.ReadValue();
            bool held = mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            frames++;
            if (delta != Vector2.zero)
            {
                if (held) { heldFrames++; heldSum += delta.magnitude; }
                else { quietFrames++; quietSum += delta.magnitude; }
            }
            if (RawMouseInput.LastCounts != Vector2.zero) { rawFrames++; rawSum += RawMouseInput.LastCounts.magnitude; }
            positionSum += (position - lastPosition).magnitude;
            lastPosition = position;
            turned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw));
            lastYaw = yaw;

            if (Time.unscaledTime < nextWrite) return;
            nextWrite = Time.unscaledTime + 1f;
            string line = Time.unscaledTime.ToString("0.0") + "  " + (Application.isFocused ? "focus" : "NOFOCUS") + "  " + Cursor.lockState + "  " + MouseCapture.Current +
                "  " + frames + " | " + quietFrames + ", " + quietSum.ToString("0") + " | " + heldFrames + ", " + heldSum.ToString("0") +
                " | " + positionSum.ToString("0") + " | " + MouseCapture.Warps + " | " + turned.ToString("0.0") + " | " + Screen.width + "x" + Screen.height +
                " | " + (RawMouseInput.Running ? RawMouseInput.Devices.ToString() : "off") + ", " + rawFrames + ", " + rawSum.ToString("0") + "\n";
            try { System.IO.File.AppendAllText(path, line); } catch (System.Exception) { path = ""; }
            lines++;
            frames = quietFrames = heldFrames = rawFrames = 0;
            quietSum = heldSum = positionSum = turned = rawSum = 0f;
#endif
        }
    }
}
