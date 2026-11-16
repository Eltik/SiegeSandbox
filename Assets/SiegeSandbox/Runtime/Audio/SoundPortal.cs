using System.Collections.Generic;
using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// A point sound can bend through: a doorway, a window, the top of a wall, a hole punched in
    /// a soft wall. Like Siege's propagation nodes, these are what let a sound behind a wall
    /// reach you from the direction of the opening instead of straight through the wall.
    /// </summary>
    public class SoundPortal : MonoBehaviour
    {
        public static readonly List<SoundPortal> All = new List<SoundPortal>();

        [Tooltip("Extra loss in dB for squeezing through this opening (a small hole costs more than a doorway).")]
        public float extraLossDb = 0f;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
    }
}
