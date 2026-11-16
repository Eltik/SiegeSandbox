using UnityEditor;
using UnityEngine;

namespace SiegeSandbox.EditorTools
{
    /// <summary>
    /// Stops Unity recompiling and reloading scripts in the middle of a Play session.
    ///
    /// If a script file changes while the game is running, Unity's default is to reload every
    /// script on the spot. That wipes everything the game set up at startup (the player's
    /// controller reference, each barricade's boards, every sheet's hole texture), and the game
    /// carries on half broken: barricades that fill their progress bar but never appear, for one.
    /// With this guard, a change made during play is simply compiled when you stop.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeReloadGuard
    {
        static bool locked;

        static PlayModeReloadGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (EditorApplication.isPlayingOrWillChangePlaymode) Lock();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) Lock();
            else if (change == PlayModeStateChange.EnteredEditMode) Unlock();
        }

        static void Lock()
        {
            if (locked) return;
            EditorApplication.LockReloadAssemblies();
            locked = true;
        }

        static void Unlock()
        {
            if (!locked) return;
            EditorApplication.UnlockReloadAssemblies();
            locked = false;
            AssetDatabase.Refresh(); // pick up anything that changed while we were playing
        }
    }
}
