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
            => CreateAnnulusSector(_ => innerRadius, _ => outerRadius, arcDegrees, segments);

        public static Mesh CreateAnnulusSector(float innerRadius, System.Func<float, float> outerAt,
            float arcDegrees, int segments = 64)
            => CreateAnnulusSector(_ => innerRadius, outerAt, arcDegrees, segments);

        /// <summary>
        /// The same wedge with edges that move: each function is asked, for a turn in degrees off
        /// the middle, how far out its edge sits there.
        ///
        /// The green zone needs this and the red one does not. A weapon reaches the same distance
        /// whichever way it is swung, but ground bent round towards costs more of a dash than
        /// ground straight ahead, so the edge of what a gladiator can reach draws in towards the
        /// sides - a leaf rather than a wedge. Drawn with a flat edge it would offer corners he
        /// cannot get to, which is the one thing the zone exists to stop the player believing.
        /// </summary>
        public static Mesh CreateAnnulusSector(System.Func<float, float> innerAt,
            System.Func<float, float> outerAt, float arcDegrees, int segments = 64)
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

                float degrees = angle * Mathf.Rad2Deg;
                float innerRadius = innerAt(degrees);
                float outerRadius = outerAt(degrees);

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

            var mesh = new Mesh { name = $"Sector_{innerAt(0f):0.0}_{outerAt(0f):0.0}_{arcDegrees:0}" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A notched arrow head lying on the XZ plane, its point at the origin and its body back
        /// along -Z: the rim as one mesh and the inside as another, so the two can be drawn in two
        /// strengths of the same white.
        ///
        /// Both are double-sided. It is only ever seen from above, and a winding slip that culls a
        /// mesh away leaves nothing on screen to say so - a second set of triangles costs nothing
        /// next to that.
        /// </summary>
        public static void CreateArrowHead(float length, float width, float notch, float inset,
            out Mesh rim, out Mesh fill)
        {
            var outer = new[]
            {
                Vector3.zero,                               // the point
                new Vector3(width * 0.5f, 0f, -length),     // right barb
                new Vector3(0f, 0f, -length + notch),       // the notch in the back
                new Vector3(-width * 0.5f, 0f, -length),    // left barb
            };

            // The inside is the outline shrunk towards a point well within it, which leaves the rim
            // as a band round the edge. Not an exact inset of constant width, but at this size the
            // eye does not measure it.
            var centre = new Vector3(0f, 0f, -length * 0.55f);
            var inner = new Vector3[4];
            for (int i = 0; i < 4; i++) inner[i] = centre + (outer[i] - centre) * inset;

            fill = DoubleSided("ArrowHeadFill", inner, new[] { 0, 1, 2, 0, 2, 3 });

            var ring = new Vector3[8];
            for (int i = 0; i < 4; i++)
            {
                ring[i] = outer[i];
                ring[i + 4] = inner[i];
            }

            var band = new int[24];
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4, k = i * 6;
                band[k] = i; band[k + 1] = j; band[k + 2] = j + 4;
                band[k + 3] = i; band[k + 4] = j + 4; band[k + 5] = i + 4;
            }
            rim = DoubleSided("ArrowHeadRim", ring, band);
        }

        /// <summary>A flat mesh with every triangle in both windings, so it shows from either side.</summary>
        private static Mesh DoubleSided(string name, Vector3[] vertices, int[] triangles)
        {
            var both = new int[triangles.Length * 2];
            for (int t = 0; t < triangles.Length; t += 3)
            {
                both[t] = triangles[t];
                both[t + 1] = triangles[t + 1];
                both[t + 2] = triangles[t + 2];
                both[triangles.Length + t] = triangles[t];
                both[triangles.Length + t + 1] = triangles[t + 2];
                both[triangles.Length + t + 2] = triangles[t + 1];
            }

            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = both;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// An upright band round an ellipse on the XZ plane, from the floor to a height, facing in:
        /// the lining of a round wall. U runs along the band in world units times uPerUnit, so a
        /// tiled texture keeps its proportions round the whole ring; V runs from floor to top.
        /// </summary>
        public static Mesh CreateEllipseBand(float radiusX, float radiusZ, float height, int segments, float uPerUnit)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];

            float along = 0f;
            var previous = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(t) * radiusX, 0f, Mathf.Sin(t) * radiusZ);
                if (i > 0) along += Vector3.Distance(previous, p);
                previous = p;

                var inward = -new Vector3(p.x / (radiusX * radiusX), 0f, p.z / (radiusZ * radiusZ)).normalized;
                vertices[i * 2] = p;
                vertices[i * 2 + 1] = p + Vector3.up * height;
                normals[i * 2] = inward;
                normals[i * 2 + 1] = inward;
                uvs[i * 2] = new Vector2(along * uPerUnit, 0f);
                uvs[i * 2 + 1] = new Vector2(along * uPerUnit, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int b = i * 2, k = i * 6;

                // Wound to face the middle of the ellipse: the angle runs anticlockwise seen from
                // above, so bottom, next bottom, top is clockwise seen from inside. The material is
                // double-sided as well, since a band culled away from the wrong side just vanishes.
                triangles[k] = b;
                triangles[k + 1] = b + 2;
                triangles[k + 2] = b + 1;
                triangles[k + 3] = b + 1;
                triangles[k + 4] = b + 2;
                triangles[k + 5] = b + 3;
            }

            var mesh = new Mesh { name = "EllipseBand" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
