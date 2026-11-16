using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// A destructible "soft" wall: a thin sheet over wooden studs. A bullet leaves a small ragged
    /// hole and carries on at reduced damage; a second bullet through the same hole is untouched.
    /// A melee strike tears out a much larger irregular hole. The studs stay, so you can see and
    /// shoot through the wall but not walk through it.
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

        public int BulletHoles { get; private set; }
        public int MeleeHoles { get; private set; }
        /// <summary>Fraction of the sheet that has been shot or punched away.</summary>
        public float OpenFraction { get { return panel == null ? 0f : 1f - panel.SolidPixels / (float)panel.PixelCount; } }

        HolePanel panel;
        Collider panelCollider;

        void Awake()
        {
            Transform preview = transform.Find("Preview");
            if (preview != null) Destroy(preview.gameObject);

            panel = HolePanel.Create(transform, size, thickness, panelMaterial);
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

            // Studs inside the wall: too close together to squeeze through, thin enough to shoot between.
            Transform studRoot = new GameObject("Studs").transform;
            studRoot.SetParent(transform, false);
            int studs = Mathf.Max(1, Mathf.RoundToInt(size.x / studSpacing));
            for (int i = 0; i <= studs; i++)
            {
                float x = -size.x * 0.5f + size.x * i / studs;
                GameObject stud = Prims.Cube("Stud", studRoot, new Vector3(x, size.y * 0.5f, 0f),
                    new Vector3(0.05f, size.y, thickness * 0.7f), studMaterial, true);
                Surface s = stud.AddComponent<Surface>();
                s.kind = SurfaceKind.Wood;
                s.damageKept = 0.6f;
            }
        }

        public bool IsOpenAt(Vector3 point)
        {
            return panel.IsOpenAt(point);
        }

        public BulletSurfaceResult OnBullet(Collider collider, Vector3 point, Vector3 direction)
        {
            if (collider != panelCollider) return BulletSurfaceResult.NotMine;
            if (panel.IsOpenAt(point)) return BulletSurfaceResult.Open;

            panel.PunchHole(point, Random.Range(0.028f, 0.04f), 0.45f, 0.55f);
            BulletHoles++;
            Prims.Debris(point, Vector3.one * 0.02f, direction, 1, debrisMaterial);
            return BulletSurfaceResult.Punched;
        }

        /// <summary>A melee strike: tear out a large ragged hole around the point.</summary>
        public void Punch(Vector3 point, Vector3 direction, float radius)
        {
            panel.PunchHole(point, radius, 0.5f, 0.6f);
            MeleeHoles++;
            Prims.Debris(point, Vector3.one * radius * 0.6f, direction, 7, debrisMaterial);
        }
    }
}
