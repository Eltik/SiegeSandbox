using System.Collections.Generic;
using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// A destructible "soft" wall, modelled on Siege's: a thin sheet over a wooden frame.
    ///  - A bullet leaves a little hole and carries on at reduced damage.
    ///  - Several bullets landing close together break a chunk out between them.
    ///  - A melee strike knocks a smallish peek hole.
    ///  - Bullets and melee never remove the wooden frame, so you cannot walk through.
    /// Holes are real: you can see through them, later bullets pass through them untouched, and
    /// sound comes through them.
    /// The object sits at the bottom centre of the wall; it spans local X and rises up Y.
    /// </summary>
    public class SoftWall : MonoBehaviour, IBulletSurface
    {
        public Vector2 size = new Vector2(4f, 3f);
        public float thickness = 0.12f;
        public float studSpacing = 0.5f;
        public Color32 color = new Color32(219, 199, 158, 255);
        public Material panelMaterial;   // alpha-clipped sheet material
        public Material studMaterial;
        public Material debrisMaterial;

        [Header("Holes (metres)")]
        public float bulletHoleRadius = 0.022f;
        public float meleeHoleRadius = 0.11f;
        public float chunkRadius = 0.12f;       // a chunk breaks out when this many bullets...
        public int bulletsPerChunk = 5;         // ...land within this distance of each other

        public int BulletHoles { get; private set; }
        public int ChunkHoles { get; private set; }
        public int MeleeHoles { get; private set; }
        /// <summary>Fraction of the sheet that has been shot or punched away.</summary>
        public float OpenFraction { get { return panel == null ? 0f : 1f - panel.SolidPixels / (float)panel.PixelCount; } }

        HolePanel panel;
        Collider panelCollider;
        readonly List<Vector2> looseHoles = new List<Vector2>(); // bullet holes not yet part of a chunk, in wall space

        void Awake()
        {
            Transform preview = transform.Find("Preview");
            if (preview != null) Destroy(preview.gameObject);

            panel = HolePanel.Create(transform, size, thickness, panelMaterial, 200f);
            panel.FillBand(0f, size.y, color, 0.06f, Random.value * 50f);

            // One collider for the whole sheet. Bullets ask the sheet whether they met wall or hole.
            GameObject col = new GameObject("PanelCollider");
            col.transform.SetParent(transform, false);
            col.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            BoxCollider box = col.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, thickness);
            Surface sheet = col.AddComponent<Surface>();
            sheet.kind = SurfaceKind.SoftWall;
            sheet.damageKept = 0.8f;
            panelCollider = box;

            // The frame inside the wall: uprights too close together to squeeze between, and two rails across.
            Transform frame = new GameObject("Frame").transform;
            frame.SetParent(transform, false);
            int studs = Mathf.Max(1, Mathf.RoundToInt(size.x / studSpacing));
            for (int i = 0; i <= studs; i++)
                Beam(frame, new Vector3(-size.x * 0.5f + size.x * i / studs, size.y * 0.5f, 0f), new Vector3(0.05f, size.y, thickness * 0.7f));
            for (float y = 1f; y < size.y - 0.3f; y += 1f)
                Beam(frame, new Vector3(0f, y, 0f), new Vector3(size.x, 0.05f, thickness * 0.6f));
        }

        void Beam(Transform parent, Vector3 localPosition, Vector3 beamSize)
        {
            GameObject beam = Prims.Cube("Beam", parent, localPosition, beamSize, studMaterial, true);
            Surface s = beam.AddComponent<Surface>();
            s.kind = SurfaceKind.Wood;
            s.damageKept = 0.6f;
        }

        public bool IsOpenAt(Vector3 point)
        {
            return panel.IsOpenAt(point);
        }

        public bool IsOpenAt(Collider collider, Vector3 point)
        {
            return collider == panelCollider && panel.IsOpenAt(point);
        }

        public BulletSurfaceResult OnBullet(Collider collider, Vector3 point, Vector3 direction)
        {
            if (collider != panelCollider) return BulletSurfaceResult.NotMine;
            if (panel.IsOpenAt(point)) return BulletSurfaceResult.Open;

            panel.PunchHole(point, bulletHoleRadius * Random.Range(0.85f, 1.2f), 0.4f, 0.5f);
            BulletHoles++;
            Prims.Debris(point, Vector3.one * 0.015f, direction, 1, debrisMaterial);
            Sfx.Play(SoundId.ImpactSoftWall, point, 0.7f);

            // Enough holes close together and the sheet between them gives way.
            Vector3 local3 = transform.InverseTransformPoint(point);
            Vector2 local = new Vector2(local3.x, local3.y);
            Vector2 centre = local;
            int near = 1;
            for (int i = 0; i < looseHoles.Count; i++)
            {
                if ((looseHoles[i] - local).sqrMagnitude > chunkRadius * chunkRadius) continue;
                centre += looseHoles[i];
                near++;
            }
            if (near >= bulletsPerChunk)
            {
                centre /= near;
                looseHoles.RemoveAll(h => (h - local).sqrMagnitude <= chunkRadius * chunkRadius);
                Vector3 world = transform.TransformPoint(new Vector3(centre.x, centre.y, local3.z));
                OpenHole(world, direction, chunkRadius, 0.5f, 4);
                ChunkHoles++;
                Sfx.Play(SoundId.WallPunch, world, 0.55f, 1.15f);
            }
            else
            {
                looseHoles.Add(local);
            }
            return BulletSurfaceResult.Punched;
        }

        /// <summary>A melee strike: knock a smallish peek hole around the point.</summary>
        public void Punch(Vector3 point, Vector3 direction)
        {
            OpenHole(point, direction, meleeHoleRadius, 0.45f, 5);
            MeleeHoles++;
            Sfx.Play(SoundId.WallPunch, point, 0.9f);
        }

        void OpenHole(Vector3 point, Vector3 direction, float radius, float jaggedness, int debris)
        {
            panel.PunchHole(point, radius, jaggedness, 0.6f);
            Prims.Debris(point, Vector3.one * radius * 0.6f, direction, debris, debrisMaterial);

            // A hole you could put your head through also lets sound through: give it a propagation node.
            Vector3 local = transform.InverseTransformPoint(point);
            GameObject node = new GameObject("HolePortal");
            node.transform.SetParent(transform, false);
            node.transform.localPosition = new Vector3(local.x, local.y, 0f);
            node.AddComponent<SoundPortal>().extraLossDb = 4f;
        }
    }
}
