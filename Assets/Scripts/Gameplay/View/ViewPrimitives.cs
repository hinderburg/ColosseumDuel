using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>Small helpers shared by the runtime-built views.</summary>
    public static class ViewPrimitives
    {
        /// <summary>
        /// A renderer-only primitive: mesh plus material, no collider.
        ///
        /// Deliberately not GameObject.CreatePrimitive - that always attaches a collider, and engine
        /// stripping drops collider classes a build never references from a scene, so every such call
        /// failed at runtime in WebGL. The simulation resolves collisions itself in virtual space
        /// (GameManager.StepActionSub), so physics colliders would be dead weight and a second source
        /// of truth besides.
        /// </summary>
        public static GameObject Create(Mesh mesh, string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// A flat quad lying on the XZ plane, visible from above.
        /// Unity's built-in quad faces -Z, so +90 degrees about X turns it face-up; -90 would point
        /// it at the floor and the top-down camera would cull it away entirely.
        /// </summary>
        public static GameObject CreateGroundQuad(Mesh quadMesh, string name, Transform parent, Material material)
        {
            var go = Create(quadMesh, name, parent, material);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return go;
        }

        /// <summary>
        /// A flat ring on the XZ plane. Used for the arena's shrinking danger zones - a ring is the
        /// shape the design actually calls for, and drawing it as real geometry keeps it readable
        /// under an orthographic top-down camera.
        /// </summary>
        public static Mesh CreateAnnulus(float innerRadius, float outerRadius, int segments = 96)
        {
            var vertices = new Vector3[segments * 2];
            var uvs = new Vector2[segments * 2];
            var normals = new Vector3[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);

                vertices[i * 2] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
                vertices[i * 2 + 1] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                uvs[i * 2] = new Vector2(i / (float)segments, 0f);
                uvs[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;

                int inner = i * 2;
                int outer = i * 2 + 1;
                int nextInner = (i * 2 + 2) % (segments * 2);
                int nextOuter = (i * 2 + 3) % (segments * 2);

                // Wound so the front face points up at the camera. Unity treats
                // cross(v1 - v0, v2 - v0) as the front-face normal; the opposite order builds a ring
                // that is silently invisible from above.
                triangles[i * 6] = inner;
                triangles[i * 6 + 1] = nextOuter;
                triangles[i * 6 + 2] = outer;
                triangles[i * 6 + 3] = inner;
                triangles[i * 6 + 4] = nextInner;
                triangles[i * 6 + 5] = nextOuter;
            }

            var mesh = new Mesh { name = $"Annulus_{innerRadius:0.00}_{outerRadius:0.00}" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
