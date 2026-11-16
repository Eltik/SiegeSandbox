using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>Small helpers for building greybox pieces and debris at runtime.</summary>
    public static class Prims
    {
        public const int IgnoreRaycastLayer = 2;

        static Mesh cubeMesh;

        public static Mesh CubeMesh()
        {
            if (cubeMesh == null)
            {
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tmp.GetComponent<Collider>().enabled = false;
                cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying) Object.Destroy(tmp);
                else Object.DestroyImmediate(tmp);
            }
            return cubeMesh;
        }

        /// <summary>A unit cube scaled to size, optionally with a box collider.</summary>
        public static GameObject Cube(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material, bool collider)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = CubeMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            if (collider) go.AddComponent<BoxCollider>();
            return go;
        }

        /// <summary>Throw a few splinters out of a broken piece. They tumble, then clean themselves up.</summary>
        public static void Debris(Vector3 center, Vector3 extents, Vector3 direction, int count, Material material)
        {
            Vector3 push = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = new Vector3(
                    Random.Range(-extents.x, extents.x),
                    Random.Range(-extents.y, extents.y),
                    Random.Range(-extents.z, extents.z));
                Vector3 size = new Vector3(Random.Range(0.05f, 0.12f), Random.Range(0.015f, 0.03f), Random.Range(0.03f, 0.07f));
                GameObject go = Cube("Debris", null, center + offset, size, material, true);
                go.layer = IgnoreRaycastLayer;
                go.transform.rotation = Random.rotation;
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                rb.AddForce(push * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 1.5f, ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * 6f, ForceMode.VelocityChange);
                Object.Destroy(go, 4f);
            }
        }
    }
}
