using System.Collections.Generic;
using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// A propagation node: a point sound can bend through or round. Doorways, windows, the tops
    /// and ends of walls, and holes punched in soft walls all get one. Sound that cannot reach you
    /// in a straight line is routed from node to node, as in Siege, and is heard from the
    /// direction of the last node on its way to you.
    /// </summary>
    public class SoundPortal : MonoBehaviour
    {
        public static readonly List<SoundPortal> All = new List<SoundPortal>();

        /// <summary>Changes whenever nodes come or go or the level's destruction state changes; routes are rebuilt when it does.</summary>
        public static int Version { get; private set; }

        [Tooltip("Extra loss in dB for squeezing through this opening (a small hole costs more than a doorway).")]
        public float extraLossDb = 0f;

        [Tooltip("Treble limit for sound passing through this opening (lower when something is hung across it).")]
        public float cutoffHz = 22000f;

        [Tooltip("How far sound can spill to either side of this point. Use it on the ends and tops of walls; leave it at zero for an opening something can be put across.")]
        public float radius = 0f;

        /// <summary>Call when something that could block or open a route has changed (a board broken, a wall holed).</summary>
        public static void MarkDirty() { Version++; }

        void OnEnable() { All.Add(this); Version++; }
        void OnDisable() { All.Remove(this); Version++; }
    }
}
