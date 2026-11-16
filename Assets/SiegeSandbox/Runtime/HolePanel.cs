using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// A thin two-sided sheet whose holes are real holes: they are painted into the alpha of the
    /// sheet's own texture and cut out by an alpha-clipped material, so a bullet leaves a small
    /// ragged hole and a melee strike a large irregular one, both of which you can see through.
    /// The sheet spans local X (centred) and rises up local Y from the object's origin.
    /// </summary>
    public class HolePanel : MonoBehaviour
    {
        public const float PixelsPerMetre = 128f;

        public Vector2 Size { get; private set; }
        /// <summary>How many texels are still solid.</summary>
        public int SolidPixels { get; private set; }
        public int PixelCount { get { return width * height; } }

        Texture2D texture;
        Color32[] pixels;
        int width, height;
        bool dirty;
        Material instance;

        static Mesh frontQuad, backQuad;

        public static HolePanel Create(Transform parent, Vector2 size, float thickness, Material template)
        {
            GameObject go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            HolePanel panel = go.AddComponent<HolePanel>();
            panel.Build(size, thickness, template);
            return panel;
        }

        void Build(Vector2 size, float thickness, Material template)
        {
            Size = size;
            width = Mathf.Max(8, Mathf.RoundToInt(size.x * PixelsPerMetre));
            height = Mathf.Max(8, Mathf.RoundToInt(size.y * PixelsPerMetre));
            pixels = new Color32[width * height]; // starts fully open (alpha 0)

            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            instance = new Material(template);
            instance.mainTexture = texture;
            if (instance.HasProperty("_BaseMap")) instance.SetTexture("_BaseMap", texture);

            // Two faces a wall-thickness apart, so a hole shows the hollow inside rather than a paper cut-out.
            MakeFace("Front", -thickness * 0.5f, Quad(false));
            MakeFace("Back", thickness * 0.5f, Quad(true));
            dirty = true;
        }

        void MakeFace(string faceName, float z, Mesh mesh)
        {
            GameObject face = new GameObject(faceName);
            face.transform.SetParent(transform, false);
            face.transform.localPosition = new Vector3(0f, 0f, z);
            face.transform.localScale = new Vector3(Size.x, Size.y, 1f);
            face.AddComponent<MeshFilter>().sharedMesh = mesh;
            face.AddComponent<MeshRenderer>().sharedMaterial = instance;
        }

        static Mesh Quad(bool facingForward)
        {
            if (facingForward && backQuad != null) return backQuad;
            if (!facingForward && frontQuad != null) return frontQuad;

            Mesh m = new Mesh();
            m.name = facingForward ? "PanelBack" : "PanelFront";
            m.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            Vector3 n = facingForward ? Vector3.forward : Vector3.back;
            m.normals = new[] { n, n, n, n };
            m.triangles = facingForward ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            if (facingForward) backQuad = m; else frontQuad = m;
            return m;
        }

        void LateUpdate()
        {
            if (!dirty) return;
            texture.SetPixels32(pixels);
            texture.Apply(false);
            dirty = false;
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (instance != null) Destroy(instance);
        }

        // ------------------------------------------------------------------ queries

        void ToPixelSpace(Vector3 world, out float px, out float py)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            px = (local.x / Size.x + 0.5f) * width;
            py = (local.y / Size.y) * height;
        }

        /// <summary>Is there a hole at this spot? Points off the sheet count as solid.</summary>
        public bool IsOpenAt(Vector3 world)
        {
            float fx, fy;
            ToPixelSpace(world, out fx, out fy);
            int x = Mathf.FloorToInt(fx), y = Mathf.FloorToInt(fy);
            if (x < 0 || y < 0 || x >= width || y >= height) return false;
            return pixels[y * width + x].a == 0;
        }

        // ------------------------------------------------------------------ painting

        /// <summary>Paint a solid horizontal band (metres above the sheet's base) with a slightly grained colour.</summary>
        public void FillBand(float yMin, float yMax, Color32 color, float grain, float seed)
        {
            int y0 = Mathf.Clamp(Mathf.RoundToInt(yMin * PixelsPerMetre), 0, height);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(yMax * PixelsPerMetre), 0, height);
            for (int y = y0; y < y1; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float shade = 1f + grain * (Mathf.PerlinNoise(x * 0.03f + seed, y * 0.45f + seed) - 0.5f) * 2f;
                    int i = y * width + x;
                    if (pixels[i].a == 0) SolidPixels++;
                    pixels[i] = new Color32(Shade(color.r, shade), Shade(color.g, shade), Shade(color.b, shade), 255);
                }
            }
            dirty = true;
        }

        /// <summary>Open a horizontal band completely (a plank coming off).</summary>
        public void ClearBand(float yMin, float yMax)
        {
            int y0 = Mathf.Clamp(Mathf.RoundToInt(yMin * PixelsPerMetre), 0, height);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(yMax * PixelsPerMetre), 0, height);
            for (int y = y0; y < y1; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (pixels[i].a != 0) SolidPixels--;
                    pixels[i].a = 0;
                }
            }
            dirty = true;
        }

        /// <summary>A small solid dot (a nail head), in metres from the sheet's centre line and base.</summary>
        public void Dot(float x, float y, float radius, Color32 color)
        {
            float cx = (x / Size.x + 0.5f) * width, cy = (y / Size.y) * height;
            float r = Mathf.Max(1f, radius * PixelsPerMetre);
            int reach = Mathf.CeilToInt(r);
            for (int py = Mathf.Max(0, (int)cy - reach); py <= Mathf.Min(height - 1, (int)cy + reach); py++)
            {
                for (int px = Mathf.Max(0, (int)cx - reach); px <= Mathf.Min(width - 1, (int)cx + reach); px++)
                {
                    float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                    if (dx * dx + dy * dy > r * r) continue;
                    int i = py * width + px;
                    if (pixels[i].a != 0) pixels[i] = new Color32(color.r, color.g, color.b, 255);
                }
            }
            dirty = true;
        }

        /// <summary>
        /// Tear an irregular hole out of the sheet. The edge wanders in and out by 'jaggedness'
        /// (0 = a perfect circle) and the material just around it is bruised darker.
        /// Returns how many texels were opened.
        /// </summary>
        public int PunchHole(Vector3 world, float radius, float jaggedness, float rimDarken)
        {
            float cx, cy;
            ToPixelSpace(world, out cx, out cy);
            float r0 = radius * PixelsPerMetre;
            float rim = Mathf.Max(2f, r0 * 0.5f);
            int reach = Mathf.CeilToInt(r0 * (1f + jaggedness) + rim) + 1;
            float seed = Random.value * 100f;
            int opened = 0;

            int yMin = Mathf.Max(0, Mathf.FloorToInt(cy) - reach), yMax = Mathf.Min(height - 1, Mathf.FloorToInt(cy) + reach);
            int xMin = Mathf.Max(0, Mathf.FloorToInt(cx) - reach), xMax = Mathf.Min(width - 1, Mathf.FloorToInt(cx) + reach);
            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ux = d > 0.001f ? dx / d : 1f, uy = d > 0.001f ? dy / d : 0f;
                    // Two octaves of noise sampled around the circle: big lobes plus small splinters.
                    float lobes = Mathf.PerlinNoise(seed + ux * 1.6f, seed + uy * 1.6f) - 0.5f;
                    float splinters = Mathf.PerlinNoise(seed * 1.7f + ux * 5.5f, seed * 1.7f + uy * 5.5f) - 0.5f;
                    float r = r0 * (1f + jaggedness * (lobes * 1.4f + splinters * 0.6f));

                    int i = y * width + x;
                    if (d <= r)
                    {
                        if (pixels[i].a != 0)
                        {
                            pixels[i].a = 0;
                            SolidPixels--;
                            opened++;
                        }
                    }
                    else if (d <= r + rim && pixels[i].a != 0)
                    {
                        float k = Mathf.Lerp(rimDarken, 1f, (d - r) / rim);
                        Color32 c = pixels[i];
                        pixels[i] = new Color32(Shade(c.r, k), Shade(c.g, k), Shade(c.b, k), 255);
                    }
                }
            }
            dirty = true;
            return opened;
        }

        static byte Shade(byte value, float scale)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value * scale), 0, 255);
        }
    }
}
