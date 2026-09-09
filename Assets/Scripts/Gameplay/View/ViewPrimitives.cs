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
        /// <summary>
        /// A cone standing on the XZ plane with its point up, for the spikes in the danger zone.
        ///
        /// Built here rather than taken from a primitive because Unity has no cone among them, and
        /// a squashed cylinder is not the same silhouette - what has to read from above is the point.
        /// Flat-shaded on purpose: each side face gets its own vertices, so the facets catch the
        /// light separately and a spike is legible as a spike at forty pixels.
        /// </summary>
        public static Mesh CreateCone(float radius, float height, int segments = 10)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            var apex = new Vector3(0f, height, 0f);

            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);

                int at = vertices.Count;
                vertices.Add(apex); vertices.Add(p1); vertices.Add(p0);
                triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);

                // The base too - a spike rising out of the floor shows its underside on the way up.
                int ab = vertices.Count;
                vertices.Add(Vector3.zero); vertices.Add(p0); vertices.Add(p1);
                triangles.Add(ab); triangles.Add(ab + 1); triangles.Add(ab + 2);
            }

            var mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

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

        /// <summary>
        /// A wedge of an annulus, lying flat, centred on +Z so the object's own forward points down
        /// the middle of it.
        ///
        /// The two zones on the control are both this shape at different sizes: the ground a
        /// gladiator may be sent onto, and the ground his weapon covers. Built about forward rather
        /// than about +X, which the full ring is, so pointing one at a heading is just setting the
        /// transform's rotation - there is no offset to remember at every call site.
        /// </summary>
        public static Mesh CreateAnnulusSector(float innerRadius, float outerRadius,
            float arcDegrees, int segments = 64)
        {
            segments = Mathf.Max(2, segments);
            float half = Mathf.Clamp(arcDegrees, 0f, 360f) * 0.5f * Mathf.Deg2Rad;

            int rings = segments + 1;   // one more than the gaps between them: an arc does not wrap
            var vertices = new Vector3[rings * 2];
            var uvs = new Vector2[rings * 2];
            var normals = new Vector3[rings * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(-half, half, t);
                float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);

                vertices[i * 2] = new Vector3(sin * innerRadius, 0f, cos * innerRadius);
                vertices[i * 2 + 1] = new Vector3(sin * outerRadius, 0f, cos * outerRadius);
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
            }

            for (int i = 0; i < segments; i++)
            {
                int inner = i * 2, outer = i * 2 + 1;
                int nextInner = inner + 2, nextOuter = outer + 2;

                // Wound so the front face looks up at the camera. Angles here run from +Z towards
                // +X, which seen from above is clockwise, so the order that works for the full ring
                // builds a sector that is silently invisible.
                triangles[i * 6] = inner;
                triangles[i * 6 + 1] = outer;
                triangles[i * 6 + 2] = nextInner;
                triangles[i * 6 + 3] = nextInner;
                triangles[i * 6 + 4] = outer;
                triangles[i * 6 + 5] = nextOuter;
            }

            var mesh = new Mesh { name = $"Sector_{innerRadius:0.0}_{outerRadius:0.0}_{arcDegrees:0}" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
