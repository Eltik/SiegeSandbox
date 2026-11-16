using UnityEditor;

namespace SiegeSandbox.EditorTools
{
    /// <summary>
    /// Makes sure the game never keeps hold of the mouse when it should not: when play is paused
    /// or stopped, when scripts are about to reload, or when the editor is not the active app. The game releases the mouse itself in the normal course of things;
    /// this is the safety net for when the game is not running to do it.
    /// </summary>
    [InitializeOnLoad]
    public static class MouseCaptureGuard
    {
        static MouseCaptureGuard()
        {
            EditorApplication.update += Watch;
            EditorApplication.pauseStateChanged += state => MouseCapture.Release();
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
                {
                    MouseCapture.Release();
                    RawMouseInput.Stop();
                }
            };
            AssemblyReloadEvents.beforeAssemblyReload += MouseCapture.Release;
            EditorApplication.quitting += MouseCapture.Release;
        }

        /// <summary>Writes what the raw mouse plugin can see to the Console and to Logs/rawmouse-status.txt.</summary>
        [MenuItem("Siege Sandbox/Raw Mouse Status")]
        static void RawMouseStatus()
        {
            RawMouseInput.Start();
            RawMouseInput.Consume();
            string text = System.DateTime.Now.ToString("HH:mm:ss") + "  raw mouse plugin " + (RawMouseInput.Running ? "running" : "NOT running") +
                ", mice offered by macOS: " + RawMouseInput.Devices + ", movement seen this session: " + (RawMouseInput.HasReported ? "yes" : "no");
            UnityEngine.Debug.Log(text);
            System.IO.File.AppendAllText("Logs/rawmouse-status.txt", text + "\n");
        }

        static void Watch()
        {
            if (!MouseCapture.Active) return;
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || !UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                MouseCapture.Release();
        }
    }
}
