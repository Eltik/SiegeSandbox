using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SiegeSandbox.EditorTools
{
    /// <summary>
    /// Builds the whole greybox test range from code: Siege Sandbox > Build Sandbox Scene.
    /// Re-run it any time; it regenerates the scene, materials and meshes from scratch.
    /// </summary>
    public static class SandboxBuilder
    {
        const string Root = "Assets/SiegeSandbox";
        const string ArtDir = Root + "/Art";
        const string SceneDir = Root + "/Scenes";
        public const string ScenePath = SceneDir + "/Sandbox.unity";
        const string MeshPath = ArtDir + "/GreyboxMeshes.asset";
        const float DividerZ = -8f;

        static Dictionary<string, Mesh> meshCache;
        static bool meshAssetCreated;
        static Material matFloor, matWall, matCover, matGun, matSkin, matTarget, matTargetHead, matTargetLimb;
        static Material matTracer, matImpact, matFlash, matHbLegs, matHbTorso, matHbHead;
        static Material matWood, matSoftWall, matSheet;

        [MenuItem("Siege Sandbox/Build Sandbox Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SiegeSandbox] Stop Play mode before rebuilding the sandbox scene.");
                return;
            }

            EnsureLayers();
            Directory.CreateDirectory(ArtDir);
            Directory.CreateDirectory(SceneDir);
            AssetDatabase.Refresh();

            Texture2D checker = MakeChecker();
            matFloor = Mat("Floor", new Color(1f, 1f, 1f), checker, false);
            matWall = Mat("Wall", new Color(0.90f, 0.91f, 0.93f), checker, false);
            matCover = Mat("Cover", new Color(0.62f, 0.70f, 0.82f), checker, false);
            matGun = Mat("Gun", new Color(0.10f, 0.10f, 0.11f), null, false);
            matSkin = Mat("Arms", new Color(0.93f, 0.90f, 0.86f), null, false);
            matTarget = Mat("Target", new Color(0.95f, 0.55f, 0.15f), null, false);
            matTargetHead = Mat("TargetHead", new Color(1.0f, 0.80f, 0.35f), null, false);
            matTargetLimb = Mat("TargetLimb", new Color(0.80f, 0.42f, 0.12f), null, false);
            matTracer = Mat("Tracer", new Color(1.0f, 0.85f, 0.30f), null, true);
            matImpact = Mat("Impact", new Color(0.08f, 0.08f, 0.08f), null, true);
            matFlash = Mat("MuzzleFlash", new Color(1.0f, 0.90f, 0.55f), null, true);
            matHbLegs = Mat("HitboxLegs", new Color(0.25f, 0.80f, 0.35f), null, false);
            matHbTorso = Mat("HitboxTorso", new Color(0.95f, 0.75f, 0.20f), null, false);
            matHbHead = Mat("HitboxHead", new Color(0.90f, 0.25f, 0.25f), null, false);
            matWood = Mat("Wood", new Color(0.55f, 0.38f, 0.22f), null, false);
            matSoftWall = Mat("SoftWall", new Color(0.86f, 0.78f, 0.62f), null, false);
            matSheet = CutoutMat("HoleSheet");

            AssetDatabase.DeleteAsset(MeshPath);
            meshCache = new Dictionary<string, Mesh>();
            meshAssetCreated = false;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLighting();
            BuildLevel();
            BuildTargets();
            BuildPlayer();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[SiegeSandbox] Sandbox scene built: " + ScenePath);
        }

        [MenuItem("Siege Sandbox/Open Sandbox Scene")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) { Build(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // ------------------------------------------------------------------ project setup

        static void EnsureLayers()
        {
            try
            {
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                if (assets == null || assets.Length == 0) return;
                SerializedObject tm = new SerializedObject(assets[0]);
                SerializedProperty layers = tm.FindProperty("layers");
                if (layers == null || layers.arraySize <= PlayerController.HitboxLayer) return;
                layers.GetArrayElementAtIndex(PlayerController.PlayerLayer).stringValue = "Player";
                layers.GetArrayElementAtIndex(PlayerController.HitboxLayer).stringValue = "PlayerHitbox";
                tm.ApplyModifiedProperties();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[SiegeSandbox] Could not name layers (they still work by index): " + e.Message);
            }
        }

        static Texture2D MakeChecker()
        {
            string path = ArtDir + "/Checker.png";
            const int size = 64, half = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool light = ((x / half) + (y / half)) % 2 == 0;
                    float v = light ? 0.88f : 0.70f;
                    if (x % half == 0 || y % half == 0) v -= 0.06f;
                    tex.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.filterMode = FilterMode.Point;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, Color color, Texture2D tex, bool unlit)
        {
            string path = ArtDir + "/" + name + ".mat";
            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = shader;
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (tex != null)
            {
                m.mainTexture = tex;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Two-sided, alpha-clipped material for sheets that get holes punched in them. Each sheet
        /// makes its own copy at runtime with its own texture; keeping this one as an asset makes
        /// sure the alpha-clip shader variant is included in builds.
        /// </summary>
        static Material CutoutMat(string name)
        {
            Material m = Mat(name, Color.white, null, false);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ geometry helpers

        /// <summary>Box mesh whose UVs are in metres, so the checker stays the same size on every surface.</summary>
        static Mesh BoxMesh(Vector3 size)
        {
            string key = size.x.ToString("F3") + "x" + size.y.ToString("F3") + "x" + size.z.ToString("F3");
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached)) return cached;

            const float tile = 2f; // one texture repeat (2x2 checks) every 2 m -> 1 m checks
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            System.Action<Vector3, Vector3, Vector3, float, float, float> face = (n, u, v, w, h, d) =>
            {
                int i = verts.Count;
                Vector3 c = n * d;
                verts.Add(c - u * (w * 0.5f) - v * (h * 0.5f));
                verts.Add(c + u * (w * 0.5f) - v * (h * 0.5f));
                verts.Add(c + u * (w * 0.5f) + v * (h * 0.5f));
                verts.Add(c - u * (w * 0.5f) + v * (h * 0.5f));
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(w / tile, 0f));
                uvs.Add(new Vector2(w / tile, h / tile));
                uvs.Add(new Vector2(0f, h / tile));
                for (int k = 0; k < 4; k++) normals.Add(n);
                if (Vector3.Dot(Vector3.Cross(u, v), n) > 0f)
                {
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
                }
                else
                {
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                    tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
                }
            };

            Vector3 hs = size * 0.5f;
            face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y, hs.x);
            face(Vector3.left, Vector3.forward, Vector3.up, size.z, size.y, hs.x);
            face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z, hs.y);
            face(Vector3.down, Vector3.right, Vector3.forward, size.x, size.z, hs.y);
            face(Vector3.forward, Vector3.right, Vector3.up, size.x, size.y, hs.z);
            face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y, hs.z);

            Mesh mesh = new Mesh();
            mesh.name = "Box_" + key;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            if (!meshAssetCreated)
            {
                AssetDatabase.CreateAsset(mesh, MeshPath);
                meshAssetCreated = true;
            }
            else
            {
                AssetDatabase.AddObjectToAsset(mesh, MeshPath);
            }
            meshCache[key] = mesh;
            return mesh;
        }

        static GameObject Box(string name, Vector3 center, Vector3 size, Material mat, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<BoxCollider>().size = size;
            go.isStatic = true;
            return go;
        }

        /// <summary>A visual-only cube (no collider), used for the gun and arms.</summary>
        static GameObject Part(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, Vector3 euler)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        // ------------------------------------------------------------------ scene content

        static void BuildLighting()
        {
            GameObject lightGo = new GameObject("Sun");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.64f, 0.68f);
        }

        static void DividerSegment(Transform parent, string name, float x0, float x1, float y0, float y1)
        {
            Box(name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, DividerZ),
                new Vector3(x1 - x0, y1 - y0, 0.4f), matWall, parent);
        }

        static void BuildLevel()
        {
            Transform level = new GameObject("Level").transform;

            // Arena: 30 m wide, 60 m long.
            Box("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(30f, 0.5f, 60f), matFloor, level);
            Box("Wall_North", new Vector3(0f, 2.5f, 30.25f), new Vector3(31f, 5f, 0.5f), matWall, level);
            Box("Wall_South", new Vector3(0f, 2.5f, -30.25f), new Vector3(31f, 5f, 0.5f), matWall, level);
            Box("Wall_East", new Vector3(15.25f, 2.5f, 0f), new Vector3(0.5f, 5f, 60f), matWall, level);
            Box("Wall_West", new Vector3(-15.25f, 2.5f, 0f), new Vector3(0.5f, 5f, 60f), matWall, level);

            // First line of cover in front of the spawn.
            Box("LeanWall", new Vector3(-8f, 1.5f, -18f), new Vector3(5f, 3f, 0.4f), matCover, level);
            Box("CrouchWall", new Vector3(8f, 0.55f, -18f), new Vector3(5f, 1.1f, 0.4f), matCover, level);
            Box("Crate_Small", new Vector3(-0.2f, 0.5f, -19f), new Vector3(1f, 1f, 1f), matCover, level);
            Box("Crate_Large", new Vector3(1.05f, 0.75f, -19.1f), new Vector3(1.5f, 1.5f, 1.5f), matCover, level);

            // Dividing wall with one opening per mechanic: a window, a doorway to lean around,
            // and a low slit you can only see through from prone.
            Transform divider = new GameObject("Divider").transform;
            divider.SetParent(level, false);
            DividerSegment(divider, "Solid_A", -15f, -9.6f, 0f, 3.5f);
            DividerSegment(divider, "Window_Sill", -9.6f, -6.4f, 0f, 1.0f);
            DividerSegment(divider, "Window_Top", -9.6f, -6.4f, 1.9f, 3.5f);
            DividerSegment(divider, "Solid_B", -6.4f, -5f, 0f, 3.5f);
            DividerSegment(divider, "SoftWall_Header", -5f, -1f, 3f, 3.5f);
            DividerSegment(divider, "Solid_B2", -1f, -0.7f, 0f, 3.5f);
            DividerSegment(divider, "Door_Lintel", -0.7f, 0.7f, 2.2f, 3.5f);
            DividerSegment(divider, "Solid_C", 0.7f, 6f, 0f, 3.5f);
            DividerSegment(divider, "Crawl_Top", 6f, 10f, 0.8f, 3.5f);

            // Crawl tunnel behind the low gap: 0.8 m of headroom, so only a prone player fits.
            Box("Crawl_Roof", new Vector3(8f, 0.95f, -6.8f), new Vector3(4f, 0.3f, 2f), matCover, level);
            Box("Crawl_Side_L", new Vector3(6.1f, 0.4f, -6.8f), new Vector3(0.2f, 0.8f, 2f), matCover, level);
            Box("Crawl_Side_R", new Vector3(9.9f, 0.4f, -6.8f), new Vector3(0.2f, 0.8f, 2f), matCover, level);

            // Destructible pieces: a soft wall, a boarded-up window, and a doorway you can board up yourself.
            MakeSoftWall(level, "SoftWall", new Vector3(-3f, 0f, DividerZ), new Vector2(4f, 3f));
            MakeBarricade(level, "Barricade_Window", new Vector3(-8f, 1.0f, DividerZ), new Vector2(3.2f, 0.9f), 0f, true);
            MakeBarricade(level, "Barricade_Door", new Vector3(0f, 0f, DividerZ), new Vector2(1.4f, 2.2f), 0.2f, false);

            // Places sound can bend round or over. (Doorways, windows and holes add their own.)
            Transform portals = new GameObject("SoundPortals").transform;
            portals.SetParent(level, false);
            // Ends and tops of walls are given some width: sound spills round the whole edge.
            for (int i = -2; i <= 2; i++) MakePortal(portals, "OverDivider", new Vector3(i * 6f, 3.9f, DividerZ), 2f, 0.5f);
            MakePortal(portals, "CrawlTunnel", new Vector3(8f, 0.4f, -7f), 2f);
            MakePortal(portals, "LeanWall_Right", new Vector3(-4.95f, 1.5f, -18f), 0f, 0.45f);
            MakePortal(portals, "LeanWall_Left", new Vector3(-11.05f, 1.5f, -18f), 0f, 0.45f);
            MakePortal(portals, "LeanWall_Over", new Vector3(-8f, 3.4f, -18f), 1f, 0.5f);
            MakePortal(portals, "CrouchWall_Over", new Vector3(8f, 1.5f, -18f), 0f, 0.5f);
            foreach (float px in new[] { -4f, 4f })
            {
                MakePortal(portals, "Pillar_Side", new Vector3(px - 1f, 1.5f, 6f), 0f, 0.45f);
                MakePortal(portals, "Pillar_Side", new Vector3(px + 1f, 1.5f, 6f), 0f, 0.45f);
            }
            DividerSegment(divider, "Solid_D", 10f, 15f, 0f, 3.5f);

            // The range beyond the divider.
            Box("Pillar_L", new Vector3(-4f, 1.75f, 6f), new Vector3(1f, 3.5f, 1f), matCover, level);
            Box("Pillar_R", new Vector3(4f, 1.75f, 6f), new Vector3(1f, 3.5f, 1f), matCover, level);
            Box("LowCover_R", new Vector3(6f, 0.55f, 14f), new Vector3(3f, 1.1f, 0.4f), matCover, level);
            Box("LowCover_L", new Vector3(-4f, 0.55f, 20f), new Vector3(3f, 1.1f, 0.4f), matCover, level);

            // Raised platform with a ramp, for some height variation.
            Box("Platform", new Vector3(-10f, 0.75f, 18f), new Vector3(6f, 1.5f, 6f), matCover, level);
            GameObject ramp = Box("Ramp", new Vector3(-10f, 0.6075f, 12.767f), new Vector3(2.5f, 0.3f, 4.8f), matCover, level);
            ramp.transform.rotation = Quaternion.Euler(-18.2f, 0f, 0f);
        }

        /// <summary>
        /// The real panels and planks are spawned when the game starts; in the editor a single
        /// placeholder box (removed on play) shows where they will be.
        /// </summary>
        static void MakePreview(Transform parent, Vector2 size, float thickness, Material mat)
        {
            GameObject preview = Prims.Cube("Preview", parent, new Vector3(0f, size.y * 0.5f, 0f),
                new Vector3(size.x, size.y, thickness), mat, false);
            preview.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void MakeSoftWall(Transform parent, string name, Vector3 bottomCenter, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = bottomCenter;
            SoftWall wall = go.AddComponent<SoftWall>();
            wall.size = size;
            wall.panelMaterial = matSheet;
            wall.studMaterial = matWood;
            wall.debrisMaterial = matSoftWall;
            MakePreview(go.transform, size, wall.thickness, matSoftWall);
        }

        static void MakePortal(Transform parent, string name, Vector3 position, float extraLossDb, float radius = 0f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            SoundPortal portal = go.AddComponent<SoundPortal>();
            portal.extraLossDb = extraLossDb;
            portal.radius = radius;
        }

        static void MakeBarricade(Transform parent, string name, Vector3 bottomCenter, Vector2 size, float bottomGap, bool built)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = bottomCenter;
            Barricade b = go.AddComponent<Barricade>();
            b.size = size;
            b.builtAtStart = built;
            b.bottomGap = bottomGap;
            b.panelMaterial = matSheet;
            b.plankMaterial = matWood;
            if (built) MakePreview(go.transform, size, b.thickness, matWood);
        }

        static void BodyPart(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 scale,
            Material mat, string label, float multiplier, bool head)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = mat;
            DamageZone zone = part.AddComponent<DamageZone>();
            zone.label = label;
            zone.multiplier = multiplier;
            zone.isHead = head;
            zone.lethal = head; // one bullet to the head always kills
        }

        /// <summary>A 1.83 m dummy with separate head, torso, arms and legs, each with its own damage.</summary>
        static void MakeTarget(Transform parent, string name, Vector3 pos, Vector3 patrol, float patrolSpeed)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            const float Limb = 0.75f; // arms and legs take three quarters of torso damage
            BodyPart(visual, "Torso", PrimitiveType.Capsule, new Vector3(0f, 1.17f, 0f), new Vector3(0.40f, 0.35f, 0.40f), matTarget, "torso", 1f, false);
            BodyPart(visual, "Leg_L", PrimitiveType.Capsule, new Vector3(-0.12f, 0.425f, 0f), new Vector3(0.22f, 0.425f, 0.22f), matTargetLimb, "leg", Limb, false);
            BodyPart(visual, "Leg_R", PrimitiveType.Capsule, new Vector3(0.12f, 0.425f, 0f), new Vector3(0.22f, 0.425f, 0.22f), matTargetLimb, "leg", Limb, false);
            BodyPart(visual, "Arm_L", PrimitiveType.Capsule, new Vector3(-0.29f, 1.15f, 0f), new Vector3(0.14f, 0.325f, 0.14f), matTargetLimb, "arm", Limb, false);
            BodyPart(visual, "Arm_R", PrimitiveType.Capsule, new Vector3(0.29f, 1.15f, 0f), new Vector3(0.14f, 0.325f, 0.14f), matTargetLimb, "arm", Limb, false);
            BodyPart(visual, "Head", PrimitiveType.Sphere, new Vector3(0f, 1.68f, 0f), Vector3.one * 0.3f, matTargetHead, "head", 2.5f, true);

            Target t = root.AddComponent<Target>();
            t.visual = visual.gameObject;
            t.patrolOffset = patrol;
            t.patrolSpeed = patrolSpeed;
        }

        static void BuildTargets()
        {
            Transform targets = new GameObject("Targets").transform;
            MakeTarget(targets, "Target_Doorway", new Vector3(0f, 0f, 2f), Vector3.zero, 0f);
            MakeTarget(targets, "Target_Window", new Vector3(-8f, 0f, 0f), Vector3.zero, 0f);
            MakeTarget(targets, "Target_Crawl", new Vector3(8f, 0f, -2f), Vector3.zero, 0f);
            MakeTarget(targets, "Target_SoftWall", new Vector3(-3.3f, 0f, -4f), Vector3.zero, 0f);
            MakeTarget(targets, "Target_Strafing_Near", new Vector3(0f, 0f, 12f), new Vector3(5f, 0f, 0f), 2f);
            MakeTarget(targets, "Target_Far", new Vector3(4f, 0f, 26f), Vector3.zero, 0f);
            MakeTarget(targets, "Target_Strafing_Far", new Vector3(-2f, 0f, 24f), new Vector3(6f, 0f, 0f), 3.5f);
            MakeTarget(targets, "Target_Platform", new Vector3(-10f, 1.5f, 18f), Vector3.zero, 0f);
            MakeTarget(targets, "Target_Spawn", new Vector3(5f, 0f, -22f), Vector3.zero, 0f);
        }

        static Transform MakeHitbox(string name, PrimitiveType type, Transform parent, Material mat,
            string label, float multiplier, bool head)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.GetComponent<Collider>().isTrigger = true;
            DamageZone zone = go.AddComponent<DamageZone>();
            zone.label = label;
            zone.multiplier = multiplier;
            zone.isHead = head;
            zone.lethal = head;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        static void BuildPlayer()
        {
            GameObject root = new GameObject("Player");
            root.transform.position = new Vector3(0f, 0.05f, -26f);

            CharacterController cc = root.AddComponent<CharacterController>();
            cc.radius = 0.28f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.03f;
            cc.slopeLimit = 45f;

            PlayerController pc = root.AddComponent<PlayerController>();

            Transform leanPivot = new GameObject("LeanPivot").transform;
            leanPivot.SetParent(root.transform, false);
            leanPivot.localPosition = new Vector3(0f, pc.standEye * 0.55f, 0f);

            Transform cameraPivot = new GameObject("CameraPivot").transform;
            cameraPivot.SetParent(leanPivot, false);
            cameraPivot.localPosition = new Vector3(0f, pc.standEye * 0.45f, 0f);

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(cameraPivot, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 78f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();

            // Weapon: a blocky SMG with a holographic sight frame.
            GameObject weaponGo = new GameObject("Weapon");
            weaponGo.transform.SetParent(camGo.transform, false);
            Weapon weapon = weaponGo.AddComponent<Weapon>();

            Transform model = new GameObject("GunModel").transform;
            model.SetParent(weaponGo.transform, false);
            model.localPosition = weapon.hipPosition;

            Vector3 zero = Vector3.zero;
            Part("Receiver", model, new Vector3(0f, 0f, 0.05f), new Vector3(0.05f, 0.07f, 0.30f), matGun, zero);
            Part("Barrel", model, new Vector3(0f, 0.012f, 0.26f), new Vector3(0.022f, 0.022f, 0.14f), matGun, zero);
            Part("Grip", model, new Vector3(0f, -0.075f, -0.03f), new Vector3(0.04f, 0.10f, 0.045f), matGun, new Vector3(15f, 0f, 0f));
            GameObject magazine = Part("Magazine", model, new Vector3(0f, -0.095f, 0.07f), new Vector3(0.03f, 0.14f, 0.04f), matGun, zero);
            GameObject bolt = Part("Bolt", model, new Vector3(-0.031f, 0.02f, -0.02f), new Vector3(0.014f, 0.016f, 0.04f), matGun, zero);
            Part("Stock", model, new Vector3(0f, -0.005f, -0.17f), new Vector3(0.035f, 0.05f, 0.16f), matGun, zero);
            Part("SightBase", model, new Vector3(0f, 0.041f, 0.03f), new Vector3(0.04f, 0.012f, 0.07f), matGun, zero);
            Part("SightPost_L", model, new Vector3(-0.02f, 0.068f, 0.03f), new Vector3(0.004f, 0.044f, 0.05f), matGun, zero);
            Part("SightPost_R", model, new Vector3(0.02f, 0.068f, 0.03f), new Vector3(0.004f, 0.044f, 0.05f), matGun, zero);
            Part("SightTop", model, new Vector3(0f, 0.09f, 0.03f), new Vector3(0.044f, 0.004f, 0.05f), matGun, zero);
            Part("Arm_Right", model, new Vector3(0.035f, -0.11f, -0.17f), new Vector3(0.06f, 0.06f, 0.30f), matSkin, new Vector3(-18f, -8f, 0f));
            GameObject leftArm = Part("Arm_Left", model, new Vector3(-0.07f, -0.085f, 0.03f), new Vector3(0.055f, 0.055f, 0.34f), matSkin, new Vector3(-12f, 28f, 0f));

            Transform muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(model, false);
            muzzle.localPosition = new Vector3(0f, 0.012f, 0.34f);
            GameObject flash = Part("MuzzleFlash", muzzle, new Vector3(0f, 0f, 0.03f), new Vector3(0.06f, 0.06f, 0.09f), matFlash, new Vector3(0f, 0f, 45f));

            weapon.player = pc;
            weapon.cam = cam;
            weapon.model = model;
            weapon.muzzle = muzzle;
            weapon.muzzleFlash = flash;
            weapon.magazine = magazine.transform;
            weapon.bolt = bolt.transform;
            weapon.leftArm = leftArm.transform;
            weapon.tracerMaterial = matTracer;
            weapon.impactMaterial = matImpact;

            SetLayerRecursive(root, PlayerController.PlayerLayer);

            // Hitbox: legs stay under the body, torso and head hang off the lean pivot so they swing with it.
            // They carry the same damage zones as the dummies, ready for enemies that shoot back.
            Transform legs = MakeHitbox("Hitbox_Legs", PrimitiveType.Capsule, root.transform, matHbLegs, "leg", 0.75f, false);
            Transform torso = MakeHitbox("Hitbox_Torso", PrimitiveType.Capsule, leanPivot, matHbTorso, "torso", 1f, false);
            Transform head = MakeHitbox("Hitbox_Head", PrimitiveType.Sphere, leanPivot, matHbHead, "head", 2.5f, true);
            legs.gameObject.layer = PlayerController.HitboxLayer;
            torso.gameObject.layer = PlayerController.HitboxLayer;
            head.gameObject.layer = PlayerController.HitboxLayer;

            // The first-person camera never draws your own hitbox.
            cam.cullingMask = ~(1 << PlayerController.HitboxLayer);

            pc.leanPivot = leanPivot;
            pc.cameraPivot = cameraPivot;
            pc.cam = cam;
            pc.legsHitbox = legs;
            pc.torsoHitbox = torso;
            pc.headHitbox = head;

            // Picture-in-picture camera that shows the hitbox from behind.
            GameObject dbg = new GameObject("HitboxDebugCamera");
            Camera dcam = dbg.AddComponent<Camera>();
            dcam.rect = new Rect(0.76f, 0.05f, 0.22f, 0.32f);
            dcam.depth = 5f;
            dcam.fieldOfView = 45f;
            dcam.clearFlags = CameraClearFlags.SolidColor;
            dcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
            dbg.AddComponent<HitboxDebugCamera>().player = pc;
            dbg.transform.position = root.transform.TransformPoint(new Vector3(1.6f, 1.5f, -3.4f));
            dbg.transform.LookAt(root.transform.position + Vector3.up * 0.8f);

            // Work arms for barricading: one steadies the barricade, the other swings a hammer or levers a crowbar.
            Transform hands = new GameObject("Hands").transform;
            hands.SetParent(camGo.transform, false);
            Part("HoldArm", hands, new Vector3(-0.21f, -0.19f, 0.40f), new Vector3(0.06f, 0.06f, 0.42f), matSkin, new Vector3(-22f, 16f, 0f));
            Transform toolArm = new GameObject("ToolArm").transform;
            toolArm.SetParent(hands, false);
            toolArm.localPosition = new Vector3(0.24f, -0.27f, 0.22f);
            Part("Forearm", toolArm, new Vector3(0f, 0f, 0.19f), new Vector3(0.06f, 0.06f, 0.38f), matSkin, Vector3.zero);
            Transform hammer = new GameObject("Hammer").transform;
            hammer.SetParent(toolArm, false);
            Part("Handle", hammer, new Vector3(0f, 0.10f, 0.37f), new Vector3(0.022f, 0.22f, 0.022f), matWood, Vector3.zero);
            Part("Head", hammer, new Vector3(0f, 0.21f, 0.39f), new Vector3(0.04f, 0.045f, 0.10f), matGun, Vector3.zero);
            Transform crowbar = new GameObject("Crowbar").transform;
            crowbar.SetParent(toolArm, false);
            Part("Bar", crowbar, new Vector3(0f, 0.02f, 0.52f), new Vector3(0.022f, 0.022f, 0.50f), matGun, Vector3.zero);
            Part("Claw", crowbar, new Vector3(0f, 0.045f, 0.78f), new Vector3(0.03f, 0.07f, 0.022f), matGun, new Vector3(25f, 0f, 0f));
            crowbar.gameObject.SetActive(false);
            SetLayerRecursive(hands.gameObject, PlayerController.PlayerLayer);

            Interactor interactor = root.AddComponent<Interactor>();
            interactor.player = pc;
            interactor.hands = hands;
            interactor.toolArm = toolArm;
            interactor.hammer = hammer.gameObject;
            interactor.crowbar = crowbar.gameObject;

            Hud hud = new GameObject("HUD").AddComponent<Hud>();
            hud.player = pc;
            hud.weapon = weapon;
            hud.interactor = interactor;
        }
    }
}
