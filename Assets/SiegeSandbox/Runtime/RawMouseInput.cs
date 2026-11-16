using System.Runtime.InteropServices;
using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Mouse movement as the mouse reports it, with no pointer acceleration: the same distance on
    /// the desk is always the same turn, however fast you move. On macOS this comes from a small
    /// native plugin (Tools/RawMouse); on Windows Unity's own mouse input is already raw. Where it
    /// is not available, or the movement came from a trackpad, the caller falls back to ordinary
    /// pointer movement.
    /// </summary>
    public static class RawMouseInput
    {
        /// <summary>The plugin is loaded and listening.</summary>
        public static bool Running { get; private set; }
        /// <summary>How many mice the system is offering for raw input.</summary>
        public static int Devices { get; private set; }
        /// <summary>Raw movement has actually arrived at some point this session.</summary>
        public static bool HasReported { get; private set; }
        /// <summary>When raw movement last arrived (unscaled time).</summary>
        public static float LastReportTime { get; private set; } = -100f;
        /// <summary>Counts read in the most recent frame, for diagnostics.</summary>
        public static Vector2 LastCounts { get; private set; }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("SiegeRawMouse")] static extern int siege_rawmouse_start();
        [DllImport("SiegeRawMouse")] static extern void siege_rawmouse_stop();
        [DllImport("SiegeRawMouse")] static extern int siege_rawmouse_read(out int x, out int y);
        [DllImport("SiegeRawMouse")] static extern int siege_rawmouse_devices();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            HasReported = false;
            LastReportTime = -100f;
            LastCounts = Vector2.zero;
        }

        public static void Start()
        {
            if (Running) return;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            try { Running = siege_rawmouse_start() > 0; }
            catch (System.Exception) { Running = false; } // plugin missing or not loadable: carry on without it
#endif
        }

        public static void Stop()
        {
            if (!Running) return;
            Running = false;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            try { siege_rawmouse_stop(); } catch (System.Exception) { }
#endif
        }

        /// <summary>Movement since the last call, in mouse counts, with up positive. Call once a frame.</summary>
        public static Vector2 Consume()
        {
            LastCounts = Vector2.zero;
            if (!Running) return Vector2.zero;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            try
            {
                int reports = siege_rawmouse_read(out int x, out int y);
                Devices = siege_rawmouse_devices();
                if (reports > 0)
                {
                    HasReported = true;
                    LastReportTime = Time.unscaledTime;
                    LastCounts = new Vector2(x, y);
                }
            }
            catch (System.Exception) { Running = false; }
#endif
            return LastCounts;
        }
    }
}
