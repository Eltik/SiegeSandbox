using System.Collections.Generic;
using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Wooden barricade for a doorway or window, built from horizontal planks with slits between
    /// them. Bullets splinter holes in it and carry on at reduced damage, three melee strikes
    /// bring it down, and enough bullet damage makes what is left collapse. The player nails it
    /// up plank by plank (see Interactor) and can pull it down the same way.
    /// The object sits at the bottom centre of the opening; planks span local X and stack up Y.
    /// </summary>
    public class Barricade : MonoBehaviour, IBulletSurface
    {
        public static readonly List<Barricade> All = new List<Barricade>();

        public Vector2 size = new Vector2(1.4f, 2.2f);
        public float thickness = 0.05f;
        public float plankHeight = 0.18f;
        public float slit = 0.014f;                 // gap between planks, enough to peek through
        public bool builtAtStart = true;
        [Range(0.1f, 0.9f)] public float collapseAt = 0.5f; // fraction of the wood shot away before the rest falls
        public Color32 woodColor = new Color32(140, 97, 56, 255);
        public Material panelMaterial;              // alpha-clipped sheet material
        public Material plankMaterial;              // plain wood, for planks in flight and on the floor

        public int PlankCount { get; private set; }
        public int PlanksPlaced { get; private set; }
        public bool IsBuilt { get { return PlanksPlaced > 0; } }
        public bool IsComplete { get { return PlankCount > 0 && PlanksPlaced == PlankCount; } }
        public int BulletHoles { get; private set; }
        /// <summary>Fraction of the wood that was put up and has since been shot or knocked out.</summary>
        public float OpenFraction { get { return placedPixels <= 0 ? 0f : 1f - panel.SolidPixels / (float)placedPixels; } }
        public float PlankStep { get { return size.y / PlankCount; } }
        public Vector3 PlankSize { get { return new Vector3(size.x, PlankStep - slit, thickness); } }

        HolePanel panel;
        BoxCollider blocker;
        int placedPixels, meleeHits;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Awake()
        {
            Transform preview = transform.Find("Preview");
            if (preview != null) Destroy(preview.gameObject);

            PlankCount = Mathf.Max(1, Mathf.RoundToInt(size.y / Mathf.Max(0.05f, plankHeight)));
            panel = HolePanel.Create(transform, size, thickness, panelMaterial);

            GameObject col = new GameObject("Blocker");
            col.transform.SetParent(transform, false);
            blocker = col.AddComponent<BoxCollider>();
            Surface wood = col.AddComponent<Surface>();
            wood.kind = SurfaceKind.Wood;
            wood.damageKept = 0.75f;
            UpdateBlocker();

            if (builtAtStart) BuildAll();
        }

        // ------------------------------------------------------------------ building and removing

        public Vector3 PlankWorldCenter(int index)
        {
            return transform.TransformPoint(new Vector3(0f, PlankStep * (index + 0.5f), 0f));
        }

        /// <summary>Nail the next plank on (bottom first). Returns false if the barricade is already complete.</summary>
        public bool PlaceNextPlank()
        {
            if (PlanksPlaced >= PlankCount) return false;
            int i = PlanksPlaced;
            float y0 = PlankStep * i + slit * 0.5f, y1 = PlankStep * (i + 1) - slit * 0.5f;
            int before = panel.SolidPixels;

            // Each plank gets its own shade, a grain, and a nail at either end.
            float shade = 0.9f + 0.2f * Mathf.PerlinNoise(i * 0.83f + transform.position.x, 0.37f);
            Color32 c = new Color32((byte)(woodColor.r * shade), (byte)(woodColor.g * shade), (byte)(woodColor.b * shade), 255);
            panel.FillBand(y0, y1, c, 0.10f, i * 7.3f);
            Color32 nail = new Color32(45, 42, 40, 255);
            float nailX = size.x * 0.5f - 0.06f, nailY = (y0 + y1) * 0.5f;
            panel.Dot(-nailX, nailY, 0.012f, nail);
            panel.Dot(nailX, nailY, 0.012f, nail);

            placedPixels += panel.SolidPixels - before;
            PlanksPlaced++;
            UpdateBlocker();
            return true;
        }

        /// <summary>Pull the top plank off and let it drop toward 'away'.</summary>
        public bool RemoveTopPlank(Vector3 away)
        {
            if (PlanksPlaced <= 0) return false;
            int i = PlanksPlaced - 1;
            int before = panel.SolidPixels;
            panel.ClearBand(PlankStep * i, PlankStep * (i + 1));
            placedPixels = Mathf.Max(0, placedPixels - EstimatedPlankPixels());
            if (before - panel.SolidPixels > 0) DropPlank(i, away, 1f);
            PlanksPlaced--;
            if (PlanksPlaced == 0) Reset();
            UpdateBlocker();
            return true;
        }

        public void BuildAll()
        {
            while (PlaceNextPlank()) { }
        }

        /// <summary>Take everything down at once. With dropPlanks the boards fall to the floor.</summary>
        public void Clear(bool dropPlanks, Vector3 away)
        {
            if (dropPlanks)
                for (int i = 0; i < PlanksPlaced; i++) DropPlank(i, away, Random.Range(0.45f, 1f));
            panel.ClearBand(0f, size.y);
            Reset();
            UpdateBlocker();
        }

        void Reset()
        {
            PlanksPlaced = 0;
            placedPixels = 0;
            meleeHits = 0;
            BulletHoles = 0;
        }

        int EstimatedPlankPixels()
        {
            return PlanksPlaced <= 0 ? 0 : placedPixels / PlanksPlaced;
        }

        void UpdateBlocker()
        {
            float top = PlankStep * PlanksPlaced;
            blocker.enabled = PlanksPlaced > 0;
            blocker.transform.localPosition = new Vector3(0f, top * 0.5f, 0f);
            blocker.size = new Vector3(size.x, Mathf.Max(0.01f, top), thickness);
        }

        void DropPlank(int index, Vector3 away, float lengthScale)
        {
            Vector3 plank = PlankSize;
            plank.x *= lengthScale;
            GameObject go = Prims.Cube("LoosePlank", null, PlankWorldCenter(index), plank, plankMaterial, true);
            go.transform.rotation = transform.rotation;
            go.layer = Prims.IgnoreRaycastLayer;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 2f;
            Vector3 push = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
            rb.AddForce(push * Random.Range(1.2f, 2.4f) + Vector3.up * Random.Range(0f, 0.8f), ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.VelocityChange);
            Destroy(go, 5f);
        }

        // ------------------------------------------------------------------ damage

        public BulletSurfaceResult OnBullet(Collider collider, Vector3 point, Vector3 direction)
        {
            if (collider != blocker) return BulletSurfaceResult.NotMine;
            if (panel.IsOpenAt(point)) return BulletSurfaceResult.Open; // through a slit or an earlier hole

            panel.PunchHole(point, Random.Range(0.045f, 0.07f), 0.55f, 0.6f);
            BulletHoles++;
            Prims.Debris(point, Vector3.one * 0.03f, direction, 2, plankMaterial);
            if (OpenFraction >= collapseAt) Collapse(direction);
            return BulletSurfaceResult.Punched;
        }

        /// <summary>Two strikes knock big chunks out; the third brings the whole thing down.</summary>
        public void MeleeHit(Vector3 point, Vector3 direction)
        {
            if (!IsBuilt) return;
            meleeHits++;
            if (meleeHits >= 3)
            {
                Collapse(direction);
                return;
            }
            panel.PunchHole(point, Mathf.Min(0.34f, size.y * 0.4f), 0.5f, 0.6f);
            Prims.Debris(point, new Vector3(0.2f, 0.2f, 0.02f), direction, 8, plankMaterial);
            if (OpenFraction >= collapseAt) Collapse(direction);
        }

        void Collapse(Vector3 direction)
        {
            Vector3 centre = transform.TransformPoint(new Vector3(0f, PlankStep * PlanksPlaced * 0.5f, 0f));
            Prims.Debris(centre, new Vector3(size.x * 0.45f, PlankStep * PlanksPlaced * 0.45f, 0.02f), direction, 10, plankMaterial);
            Clear(true, direction);
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
