using UnityEditor;
using UnityEngine.InputSystem;

namespace SiegeSandbox.EditorTools
{
    /// <summary>
    /// Makes sure the game never keeps hold of the mouse when it should not: when play is paused
    /// or stopped, when scripts are about to reload, when the editor is not the active app, or
    /// when Escape is held. The game releases the mouse itself in the normal course of things;
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
                if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode) MouseCapture.Release();
            };
            AssemblyReloadEvents.beforeAssemblyReload += MouseCapture.Release;
            EditorApplication.quitting += MouseCapture.Release;
        }

        static void Watch()
        {
            if (!MouseCapture.Active) return;
            Keyboard kb = Keyboard.current;
            bool escape = kb != null && kb.escapeKey.isPressed;
            if (escape || !EditorApplication.isPlaying || EditorApplication.isPaused || !UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                MouseCapture.Release();
        }
    }
}
