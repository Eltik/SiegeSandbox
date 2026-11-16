using System.Collections.Generic;
using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// A doorway or window barricade, modelled on Siege's: horizontal wooden boards held together
    /// by two yellow fabric straps, with a yellow cross sprayed across it.
    ///  - It blocks sight completely until damaged.
    ///  - Each board is its own piece. A bullet makes a small hole and carries on weaker; a few
    ///    bullets in one board break that board off; about twenty bullets destroy the whole thing.
    ///  - Each of the first two melee strikes knocks out the boards around the hit; the third
    ///    always destroys it.
    ///  - Missing boards leave real gaps: you can see and shoot through them, crawl under a door
    ///    barricade with its bottom boards gone, or climb through a window with its lower boards
    ///    gone. Going through destroys what is left.
    ///  - It is unrolled from the top while the interact key is held and pried off in one piece.
    /// The object sits at the bottom centre of the opening; boards span local X and stack up Y.
    /// </summary>
    public class Barricade : MonoBehaviour, IBulletSurface
    {
        public static readonly List<Barricade> All = new List<Barricade>();

        public Vector2 size = new Vector2(1.4f, 2.2f);
        public float thickness = 0.05f;
        public float plankHeight = 0.18f;
        public float bottomGap = 0f;                // door barricades stop short of the floor
        public int bulletsPerPlank = 3;
        public int bulletsToDestroy = 20;
        public bool builtAtStart = true;
        public Color32 woodColor = new Color32(140, 97, 56, 255);
        public Color32 fabricColor = new Color32(226, 184, 40, 255);
        public Material panelMaterial;              // alpha-clipped sheet material
        public Material plankMaterial;              // plain wood, for boards on the floor

        class Plank
        {
            public BoxCollider collider;
            public bool present;
            public int bulletHits;
        }

        public int PlankCount { get { return planks.Length; } }
        public int PlanksPresent { get; private set; }
        public bool IsBuilt { get { return PlanksPresent > 0; } }
        public bool IsComplete { get { return PlanksPresent == planks.Length; } }
        public int BulletHoles { get; private set; }
        public float PlankStep { get { return (size.y - bottomGap) / planks.Length; } }

        Plank[] planks = new Plank[0];
        HolePanel panel;
        int meleeHits;
        bool deploying;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Awake()
        {
            Transform preview = transform.Find("Preview");
            if (preview != null) Destroy(preview.gameObject);

            int count = Mathf.Max(1, Mathf.RoundToInt((size.y - bottomGap) / Mathf.Max(0.05f, plankHeight)));
            planks = new Plank[count];
            panel = HolePanel.Create(transform, size, thickness, panelMaterial, 160f);

            for (int i = 0; i < count; i++)
            {
                GameObject go = new GameObject("Board" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, PlankBottom(i) + PlankStep * 0.5f, 0f);
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(size.x, PlankStep, thickness);
                box.enabled = false;
                Surface wood = go.AddComponent<Surface>();
                wood.kind = SurfaceKind.Wood;
                wood.damageKept = 0.75f;
                planks[i] = new Plank { collider = box };
            }

            // Sound can always try the opening; the boards in the way decide how much gets through.
            GameObject node = new GameObject("OpeningPortal");
            node.transform.SetParent(transform, false);
            node.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            node.AddComponent<SoundPortal>();

            if (builtAtStart) BuildAll();
        }

        void Update()
        {
            // Squeezing or climbing through a damaged barricade tears the rest of it down.
            if (!IsBuilt || IsComplete || deploying) return;
            PlayerController player = PlayerController.Instance;
            if (player == null) return;
            Vector3 local = transform.InverseTransformPoint(player.transform.position + Vector3.up * (player.CurrentHeight * 0.5f));
            if (Mathf.Abs(local.z) < 0.22f && Mathf.Abs(local.x) < size.x * 0.5f && local.y > -0.2f && local.y < size.y + 0.4f)
                Collapse(transform.forward * Mathf.Sign(local.z));
        }

        // ------------------------------------------------------------------ boards

        float PlankBottom(int index)
        {
            return bottomGap + PlankStep * index;
        }

        int PlankAt(Vector3 world)
        {
            float y = transform.InverseTransformPoint(world).y;
            return Mathf.Clamp(Mathf.FloorToInt((y - bottomGap) / PlankStep), 0, planks.Length - 1);
        }

        public bool IsPlankPresent(int index)
        {
            return index >= 0 && index < planks.Length && planks[index].present;
        }

        public Vector3 PlankWorldCenter(int index)
        {
            return transform.TransformPoint(new Vector3(0f, PlankBottom(index) + PlankStep * 0.5f, 0f));
        }

        void AddPlank(int i)
        {
            Plank p = planks[i];
            if (p.present) return;
            float y0 = PlankBottom(i), y1 = y0 + PlankStep;

            // Wood with its own shade and grain, a dark seam along the top edge, nails at the ends.
            float shade = 0.9f + 0.2f * Mathf.PerlinNoise(i * 0.83f + transform.position.x, 0.37f);
            Color32 wood = new Color32((byte)(woodColor.r * shade), (byte)(woodColor.g * shade), (byte)(woodColor.b * shade), 255);
            panel.FillBand(y0, y1, wood, 0.10f, i * 7.3f);
            panel.FillRect(-size.x * 0.5f, size.x * 0.5f, y1 - 0.008f, y1, new Color32(60, 40, 24, 255));
            Color32 nail = new Color32(45, 42, 40, 255);
            panel.Dot(-size.x * 0.5f + 0.05f, y0 + PlankStep * 0.5f, 0.011f, nail);
            panel.Dot(size.x * 0.5f - 0.05f, y0 + PlankStep * 0.5f, 0.011f, nail);
            PaintFabric(y0, y1, true);

            p.present = true;
            p.bulletHits = 0;
            p.collider.enabled = true;
            PlanksPresent++;
        }

        /// <summary>The two straps, and the part of the sprayed cross that falls in these rows.</summary>
        void PaintFabric(float y0, float y1, bool withCross)
        {
            float strapX = size.x * 0.27f, strapHalf = 0.035f;
            panel.FillRect(-strapX - strapHalf, -strapX + strapHalf, y0, y1, fabricColor);
            panel.FillRect(strapX - strapHalf, strapX + strapHalf, y0, y1, fabricColor);
            if (!withCross) return;
            float bottom = bottomGap + 0.08f, top = size.y - 0.08f, half = size.x * 0.5f - 0.08f;
            panel.StrokeOverSolid(new Vector2(-half, bottom), new Vector2(half, top), 0.05f, fabricColor, y0, y1);
            panel.StrokeOverSolid(new Vector2(-half, top), new Vector2(half, bottom), 0.05f, fabricColor, y0, y1);
        }

        /// <summary>Break one board off. The fabric straps are left hanging across the gap.</summary>
        public void BreakPlank(int i, Vector3 direction)
        {
            Plank p = planks[i];
            if (!p.present) return;
            float y0 = PlankBottom(i);
            panel.ClearBand(y0, y0 + PlankStep);
            PaintFabric(y0, y0 + PlankStep, false);
            p.present = false;
            p.collider.enabled = false;
            PlanksPresent--;

            DropPlank(i, direction, Random.Range(0.35f, 0.6f));
            DropPlank(i, direction, Random.Range(0.25f, 0.4f));
            Prims.Debris(PlankWorldCenter(i), new Vector3(size.x * 0.4f, PlankStep * 0.4f, 0.02f), direction, 3, plankMaterial);
            Sfx.Play(SoundId.PlankBreak, PlankWorldCenter(i), 0.9f);
            if (PlanksPresent == 0) ClearEverything();
        }

        void DropPlank(int index, Vector3 away, float lengthScale)
        {
            Vector3 board = new Vector3(size.x * lengthScale, PlankStep * 0.9f, thickness);
            Vector3 at = PlankWorldCenter(index) + transform.right * Random.Range(-0.3f, 0.3f) * size.x;
            GameObject go = Prims.Cube("LooseBoard", null, at, board, plankMaterial, true);
            go.transform.rotation = transform.rotation;
            go.layer = Prims.IgnoreRaycastLayer;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.5f;
            Vector3 push = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
            rb.AddForce(push * Random.Range(1.2f, 2.6f) + Vector3.up * Random.Range(0f, 0.8f), ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
            Destroy(go, 5f);
        }

        void ClearEverything()
        {
            panel.ClearBand(0f, size.y);
            for (int i = 0; i < planks.Length; i++)
            {
                planks[i].present = false;
                planks[i].bulletHits = 0;
                planks[i].collider.enabled = false;
            }
            PlanksPresent = 0;
            meleeHits = 0;
            BulletHoles = 0;
            deploying = false;
        }

        // ------------------------------------------------------------------ putting up and taking down

        public void BuildAll()
        {
            for (int i = 0; i < planks.Length; i++) AddPlank(i);
            deploying = false;
        }

        /// <summary>Unroll from the top: show this many boards, counting down from the top one.</summary>
        public void SetDeployed(int boardsFromTop)
        {
            deploying = true;
            int n = Mathf.Clamp(boardsFromTop, 0, planks.Length);
            for (int k = 0; k < n; k++) AddPlank(planks.Length - 1 - k);
            if (n == planks.Length) deploying = false;
        }

        /// <summary>Abandon a half-unrolled barricade: it comes away with nothing left behind.</summary>
        public void Retract()
        {
            ClearEverything();
        }

        /// <summary>Pried off in one piece: every board drops at once.</summary>
        public void PryOff(Vector3 away)
        {
            if (!IsBuilt) return;
            for (int i = 0; i < planks.Length; i++)
                if (planks[i].present) DropPlank(i, away, Random.Range(0.6f, 1f));
            Sfx.Play(SoundId.BarricadeCollapse, transform.TransformPoint(new Vector3(0f, size.y * 0.5f, 0f)), 0.7f, 0.9f);
            ClearEverything();
        }

        /// <summary>Remove silently (used when resetting a scene or a test).</summary>
        public void Clear()
        {
            ClearEverything();
        }

        // ------------------------------------------------------------------ damage

        public bool IsOpenAt(Collider collider, Vector3 point)
        {
            return panel.IsOpenAt(point);
        }

        public BulletSurfaceResult OnBullet(Collider collider, Vector3 point, Vector3 direction)
        {
            int index = -1;
            for (int i = 0; i < planks.Length; i++)
                if (planks[i].collider == collider) { index = i; break; }
            if (index < 0) return BulletSurfaceResult.NotMine;
            if (panel.IsOpenAt(point)) return BulletSurfaceResult.Open; // through an earlier bullet hole

            panel.PunchHole(point, Random.Range(0.02f, 0.03f), 0.5f, 0.55f);
            BulletHoles++;
            planks[index].bulletHits++;
            Prims.Debris(point, Vector3.one * 0.02f, direction, 1, plankMaterial);
            Sfx.Play(SoundId.ImpactWood, point, 0.8f);

            if (BulletHoles >= bulletsToDestroy) Collapse(direction);
            else if (planks[index].bulletHits >= bulletsPerPlank) BreakPlank(index, direction);
            return BulletSurfaceResult.Punched;
        }

        /// <summary>The first two strikes knock out the boards around the hit; the third always destroys it.</summary>
        public void MeleeHit(Vector3 point, Vector3 direction)
        {
            if (!IsBuilt) return;
            meleeHits++;
            if (meleeHits >= 3)
            {
                Collapse(direction);
                return;
            }

            int toBreak = Mathf.Max(1, Mathf.RoundToInt(planks.Length * 0.3f));
            int aimed = PlankAt(point);
            for (int n = 0; n < toBreak && PlanksPresent > 0; n++)
            {
                int best = -1;
                for (int i = 0; i < planks.Length; i++)
                {
                    if (!planks[i].present) continue;
                    if (best < 0 || Mathf.Abs(i - aimed) < Mathf.Abs(best - aimed)) best = i;
                }
                if (best < 0) break;
                BreakPlank(best, direction);
            }
        }

        void Collapse(Vector3 direction)
        {
            if (!IsBuilt) return;
            Vector3 centre = transform.TransformPoint(new Vector3(0f, size.y * 0.5f, 0f));
            for (int i = 0; i < planks.Length; i++)
                if (planks[i].present) DropPlank(i, direction, Random.Range(0.4f, 0.9f));
            Prims.Debris(centre, new Vector3(size.x * 0.45f, size.y * 0.4f, 0.02f), direction, 10, plankMaterial);
            Sfx.Play(SoundId.BarricadeCollapse, centre, 1f);
            ClearEverything();
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
