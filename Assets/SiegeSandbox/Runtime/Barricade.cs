using System.Collections.Generic;
using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Wooden barricade for a doorway or window. Bullets pass through and chew the planks
    /// apart, three melee strikes knock it out, and the player can build or remove it with F.
    /// The object sits at the bottom centre of the opening; planks span local X and stack up Y.
    /// </summary>
    public class Barricade : MonoBehaviour
    {
        public static readonly List<Barricade> All = new List<Barricade>();

        public Vector2 size = new Vector2(1.4f, 2.2f);
        public float thickness = 0.06f;
        public float plankHeight = 0.18f;
        public float plankHealth = 60f;
        public bool builtAtStart = true;
        public Material plankMaterial;

        readonly List<Breakable> planks = new List<Breakable>();
        int builtCount;

        public int PlanksLeft
        {
            get
            {
                int n = 0;
                for (int i = 0; i < planks.Count; i++)
                    if (planks[i] != null && !planks[i].IsBroken) n++;
                return n;
            }
        }

        public bool IsBuilt { get { return PlanksLeft > 0; } }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Awake()
        {
            Transform preview = transform.Find("Preview");
            if (preview != null) Destroy(preview.gameObject);
            if (builtAtStart) Build();
        }

        public void Build()
        {
            Remove();
            builtCount = Mathf.Max(1, Mathf.RoundToInt(size.y / Mathf.Max(0.05f, plankHeight)));
            float h = size.y / builtCount;
            for (int i = 0; i < builtCount; i++)
            {
                // A small gap between planks leaves slits to see and shoot through.
                GameObject plank = Prims.Cube("Plank", transform, new Vector3(0f, h * (i + 0.5f), 0f),
                    new Vector3(size.x, h - 0.025f, thickness), plankMaterial, true);
                Surface s = plank.AddComponent<Surface>();
                s.kind = SurfaceKind.Wood;
                s.damageKept = 0.75f;
                Breakable b = plank.AddComponent<Breakable>();
                b.health = plankHealth;
                b.debrisCount = 3;
                planks.Add(b);
            }
        }

        /// <summary>Take the barricade down cleanly (no debris).</summary>
        public void Remove()
        {
            for (int i = 0; i < planks.Count; i++)
                if (planks[i] != null && !planks[i].IsBroken) Destroy(planks[i].gameObject);
            planks.Clear();
        }

        /// <summary>Each strike smashes a third of the original planks, nearest the hit first.</summary>
        public void MeleeHit(Vector3 point, Vector3 direction)
        {
            int toBreak = Mathf.CeilToInt(builtCount / 3f);
            for (int n = 0; n < toBreak; n++)
            {
                Breakable best = null;
                float bestDist = float.MaxValue;
                for (int i = 0; i < planks.Count; i++)
                {
                    Breakable p = planks[i];
                    if (p == null || p.IsBroken) continue;
                    float d = Mathf.Abs(p.transform.position.y - point.y);
                    if (d < bestDist) { bestDist = d; best = p; }
                }
                if (best == null) return;
                best.Break(direction);
            }
        }

        /// <summary>Nearest point on the barricade's opening to a world position.</summary>
        public Vector3 ClosestPoint(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            local.x = Mathf.Clamp(local.x, -size.x * 0.5f, size.x * 0.5f);
            local.y = Mathf.Clamp(local.y, 0f, size.y);
            local.z = 0f;
            return transform.TransformPoint(local);
        }
    }
}
