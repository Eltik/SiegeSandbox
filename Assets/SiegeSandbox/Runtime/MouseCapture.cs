using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SiegeSandbox
{
    /// <summary>
    /// Takes the mouse for the game and makes sure it has really been taken.
    ///
    /// Asking Unity to lock the cursor is not enough on its own: in the editor the lock can be
    /// reported as on while the pointer is in fact still loose. The pointer then wanders out of
    /// the Game view, plain movement stops reaching the game, and you can only look around while
    /// a button is held (a drag keeps reporting to the view it started in). So this does three
    /// things, in order:
    ///  1. Unity's own cursor lock, switched off and on again so it is applied afresh.
    ///  2. On macOS, the pointer is pinned at system level as well: it stays put in the middle of
    ///     the view and only its movement is reported.
    ///  3. It keeps watching. If the pointer is still seen moving, it falls back to steering the
    ///     pointer back to the middle of the view by hand.
    /// Everything is undone on Escape, when the window loses focus, and when play pauses or stops.
    /// </summary>
    public static class MouseCapture
    {
        public enum Mode { Free, Locked, Pinned, Recentred }

        public static Mode Current { get; private set; }
        public static bool Active { get { return Current != Mode.Free; } }
        /// <summary>What the mouse reported this frame, before any of it is used.</summary>
        public static Vector2 RawDelta { get; private set; }
        /// <summary>How far the pointer has been seen to move lately while it was meant to be held still.</summary>
        public static float Drift { get; private set; }
        public static int Warps { get; private set; }
        /// <summary>This frame's turn came from the mouse's own counts, not the accelerated pointer.</summary>
        public static bool UsingRaw { get; private set; }

        /// <summary>One raw mouse count turns this much of what one pointer unit used to.</summary>
        const float RawScale = 0.4f;
        static int systemFrames;

        static Vector2 lastPosition;
        static int skipFrames;
        static bool pinned;

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        static extern int CGAssociateMouseAndMouseCursorPosition(int connected);
#endif

        /// <summary>Pin or free the pointer at system level. Returns false where that is not available.</summary>
        static bool Pin(bool on)
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            try
            {
                CGAssociateMouseAndMouseCursorPosition(on ? 0 : 1);
                pinned = on;
                return true;
            }
            catch (System.Exception)
            {
                pinned = false;
                return false;
            }
#else
            pinned = false;
            return false;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            Release();
        }

        static Vector2 Centre()
        {
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        public static void Take()
        {
            Mouse mouse = Mouse.current;
            Cursor.lockState = CursorLockMode.None;
            if (mouse != null) mouse.WarpCursorPosition(Centre()); // start from the middle of the view
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Current = Pin(true) ? Mode.Pinned : Mode.Locked;
            if (GameSettings.rawMouse) RawMouseInput.Start();
            lastPosition = mouse != null ? mouse.position.ReadValue() : Centre();
            skipFrames = 3;
            Drift = 0f;
        }

        public static void Release()
        {
            if (pinned) Pin(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Current = Mode.Free;
            RawDelta = Vector2.zero;
            Drift = 0f;
        }

        /// <summary>Call once a frame while the game has the mouse. Returns how far to turn, in mouse counts.</summary>
        public static Vector2 Read(Mouse mouse)
        {
            RawDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            Vector2 counts = RawMouseInput.Consume(); // every frame, so nothing piles up while the mouse is not ours
            if (!Active || mouse == null) { systemFrames = 0; UsingRaw = false; return Vector2.zero; }

            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;

            Vector2 position = mouse.position.ReadValue();
            Vector2 moved = position - lastPosition;
            lastPosition = position;
            if (skipFrames > 0)
            {
                skipFrames--;
                return Vector2.zero; // the jump from being put back in the middle is not a turn
            }

            // A pointer that is truly held does not go anywhere. One that does is loose.
            Drift = Drift * Mathf.Exp(-Time.unscaledDeltaTime * 2f) + moved.magnitude;
            if (Current != Mode.Recentred && Drift > 80f)
            {
                if (pinned) Pin(false);
                Current = Mode.Recentred;
            }

            Vector2 look = RawDelta;

            // With acceleration off, turn by what the mouse itself reported. The system pointer is
            // only a fallback: for a trackpad, or if raw movement never arrives.
            UsingRaw = false;
            if (GameSettings.rawMouse && RawMouseInput.Running && RawMouseInput.HasReported)
            {
                bool mouseInUse = Time.unscaledTime - RawMouseInput.LastReportTime < 0.25f;
                if (counts != Vector2.zero)
                {
                    look = counts * RawScale;
                    UsingRaw = true;
                    systemFrames = 0;
                }
                else if (mouseInUse)
                {
                    look = Vector2.zero; // the pointer's copy of movement the mouse has already reported
                    UsingRaw = true;
                    systemFrames = 0;
                }
                else if (RawDelta != Vector2.zero && ++systemFrames < 2)
                {
                    look = Vector2.zero; // one stray frame is not a trackpad
                }
            }

            if (Current == Mode.Recentred)
            {
                if (look == Vector2.zero) look = moved; // movement that only showed up as a change of position
                Vector2 centre = Centre();
                float leash = Mathf.Min(Screen.width, Screen.height) * 0.2f;
                if ((position - centre).sqrMagnitude > leash * leash)
                {
                    mouse.WarpCursorPosition(centre);
                    InputState.Change(mouse.position, centre);
                    lastPosition = centre;
                    skipFrames = 1;
                    Warps++;
                }
            }
            return look;
        }
    }
}
