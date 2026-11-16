using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// A destructible "soft" wall: thin panels over wooden studs. Bullets punch a panel out
    /// and keep going at reduced damage; a melee strike knocks a wider hole. The studs stay,
    /// so you can see and shoot through the wall but not walk through it.
    /// The object sits at the bottom centre of the wall; it spans local X and rises up Y.
    /// </summary>
    public class SoftWall : MonoBehaviour
    {
        public Vector2 size = new Vector2(4f, 3f);
        public float thickness = 0.12f;
        public float panelSize = 0.25f;
        public float studSpacing = 0.5f;
        public Material panelMaterial;
        public Material studMaterial;

        Breakable[] panels;

        public int PanelsLeft
        {
            get
            {
                int n = 0;
                if (panels == null) return 0;
                for (int i = 0; i < panels.Length; i++)
                    if (panels[i] != null && !panels[i].IsBroken) n++;
                return n;
            }
        }

        public int PanelCount { get { return panels != null ? panels.Length : 0; } }

        void Awake()
        {
            Transform preview = transform.Find("Preview");
            if (preview != null) Destroy(preview.gameObject);

            int cols = Mathf.Max(1, Mathf.RoundToInt(size.x / panelSize));
            int rows = Mathf.Max(1, Mathf.RoundToInt(size.y / panelSize));
            float w = size.x / cols, h = size.y / rows;
            panels = new Breakable[cols * rows];

            Transform panelRoot = new GameObject("Panels").transform;
            panelRoot.SetParent(transform, false);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    Vector3 pos = new Vector3(-size.x * 0.5f + w * (x + 0.5f), h * (y + 0.5f), 0f);
                    GameObject panel = Prims.Cube("Panel", panelRoot, pos, new Vector3(w, h, thickness), panelMaterial, true);
                    Surface s = panel.AddComponent<Surface>();
                    s.kind = SurfaceKind.SoftWall;
                    s.damageKept = 0.8f;
                    Breakable b = panel.AddComponent<Breakable>();
                    b.health = 1f;
                    b.debrisCount = 1;
                    panels[y * cols + x] = b;
                }
            }

            // Studs: too close together to squeeze through, thin enough to shoot between.
            Transform studRoot = new GameObject("Studs").transform;
            studRoot.SetParent(transform, false);
            int studs = Mathf.Max(1, Mathf.RoundToInt(size.x / studSpacing));
            for (int i = 0; i <= studs; i++)
            {
                float x = -size.x * 0.5f + size.x * i / studs;
                GameObject stud = Prims.Cube("Stud", studRoot, new Vector3(x, size.y * 0.5f, 0f),
                    new Vector3(0.05f, size.y, thickness * 0.8f), studMaterial, true);
                Surface s = stud.AddComponent<Surface>();
                s.kind = SurfaceKind.Wood;
                s.damageKept = 0.6f;
            }
        }

        /// <summary>Knock out every panel within a radius of the hit (used by melee).</summary>
        public int Punch(Vector3 point, Vector3 direction, float radius)
        {
            int broken = 0;
            Vector3 local = transform.InverseTransformPoint(point);
            for (int i = 0; i < panels.Length; i++)
            {
                Breakable p = panels[i];
                if (p == null || p.IsBroken) continue;
                Vector3 d = p.transform.localPosition - local;
                d.z = 0f;
                if (d.magnitude > radius) continue;
                p.Break(direction);
                broken++;
            }
            return broken;
        }
    }
}
