using System.Collections.Generic;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public enum ObstacleKind
    {
        Column,
        Crate,
    }

    /// <summary>
    /// One solid thing standing on the sand. Convex, so everything that has to reason about it -
    /// running round it, being pushed out of it, drawing it - can treat it as a set of half-planes.
    /// </summary>
    public sealed class Obstacle
    {
        public readonly ObstacleKind Kind;
        public readonly Vector2 Centre;

        /// <summary>A column's radius, or half a crate's side.</summary>
        public readonly float Size;

        /// <summary>The footprint, convex and anticlockwise.</summary>
        public readonly Vector2[] Outline;

        /// <summary>
        /// Sides on the polygon a column is stood in for. Eight, drawn round the outside of the
        /// circle rather than inside it, so the shape the simulation blocks with is never smaller
        /// than the pillar drawn on screen - a man should not be able to clip through the stone.
        /// </summary>
        private const int ColumnSides = 8;

        public Obstacle(ObstacleKind kind, Vector2 centre, float size)
        {
            Kind = kind;
            Centre = centre;
            Size = size;
            Outline = kind == ObstacleKind.Column ? Circumscribed(centre, size, ColumnSides) : Square(centre, size);
        }

        private static Vector2[] Circumscribed(Vector2 centre, float radius, int sides)
        {
            float outer = radius / Mathf.Cos(Mathf.PI / sides);
            var points = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) / sides * Mathf.PI * 2f;
                points[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * outer;
            }
            return points;
        }

        private static Vector2[] Square(Vector2 centre, float half)
            => new[]
            {
                centre + new Vector2(-half, -half),
                centre + new Vector2(half, -half),
                centre + new Vector2(half, half),
                centre + new Vector2(-half, half),
            };
    }

    /// <summary>
    /// The obstacles on the sand, and the three questions the game asks about them: can a man stand
    /// here, can he run straight from here to there, and if not, what is the shortest way round.
    ///
    /// Everything is asked for a body of a given radius rather than for a point. The obstacles are
    /// grown by that radius - a Minkowski sum, done as a mitred offset of each polygon - and the
    /// gladiator is then treated as the point at his own centre. That is the whole trick that
    /// keeps the pathfinder simple: in the grown field, a path that keeps a point out of the
    /// polygons keeps a body of that radius off the stone.
    ///
    /// The shortest way round a set of convex polygons runs from corner to corner, so the path is
    /// found on a visibility graph: the corners of every grown obstacle, joined wherever one can see
    /// the next. The corners and the lines between them never change during a match, so that graph
    /// is built once per radius and cached; a query only has to connect its own two ends to it.
    /// That is what makes it cheap enough to rebuild every frame under a moving finger.
    /// </summary>
    public sealed class ObstacleField
    {
        public readonly IReadOnlyList<Obstacle> Obstacles;

        /// <summary>
        /// How much further out than the grown obstacle the graph's corners are placed.
        ///
        /// A path that ran exactly along a grown edge would be tested against that same edge and
        /// come back blocked or clear on floating-point luck. Pushing the corners a unit clear makes
        /// every leg between them unambiguously outside.
        /// </summary>
        private const float CornerMargin = 1f;

        /// <summary>
        /// How long a stretch of a segment has to lie inside a grown obstacle before it counts as
        /// going through it, in virtual units.
        ///
        /// Not zero, because a run that starts pressed against a column - which is where the
        /// collision response leaves a man - begins exactly on its grown edge, and a zero tolerance
        /// would call every way out of there blocked.
        /// </summary>
        private const float OverlapTolerance = 0.5f;

        private readonly Dictionary<int, Graph> _graphs = new Dictionary<int, Graph>();

        public ObstacleField(IEnumerable<Obstacle> obstacles)
        {
            Obstacles = new List<Obstacle>(obstacles);
        }

        public static readonly ObstacleField Empty = new ObstacleField(new Obstacle[0]);

        /// <summary>
        /// The arena everybody fights in: four columns round the middle and four crates.
        ///
        /// Point-symmetric through the centre, so whatever the layout does for one end it does for
        /// the other, and neither side starts a round with more cover than the man opposite. The
        /// middle is left open on purpose - the straight charge down it is still there to be taken,
        /// and the columns either side of it are what make taking it a choice rather than the
        /// only line there is.
        ///
        /// Every gap is either plainly wide enough for a man or plainly shut. A gap a hair wider
        /// than a body is a gap a shove wedges him into.
        /// </summary>
        public static ObstacleField Standard()
        {
            float column = GameConstants.ColumnRadius;
            float crate = GameConstants.CrateHalfSize;

            return new ObstacleField(new[]
            {
                new Obstacle(ObstacleKind.Column, new Vector2(-110f, -180f), column),
                new Obstacle(ObstacleKind.Column, new Vector2(110f, -180f), column),
                new Obstacle(ObstacleKind.Column, new Vector2(-110f, 180f), column),
                new Obstacle(ObstacleKind.Column, new Vector2(110f, 180f), column),

                new Obstacle(ObstacleKind.Crate, new Vector2(-215f, 50f), crate),
                new Obstacle(ObstacleKind.Crate, new Vector2(215f, -50f), crate),
                new Obstacle(ObstacleKind.Crate, new Vector2(-150f, -300f), crate),
                new Obstacle(ObstacleKind.Crate, new Vector2(150f, 300f), crate),
            });
        }

        // ------------------------------------------------------------------
        // standing, and being pushed out
        // ------------------------------------------------------------------

        /// <summary>Whether a body of this radius can stand here: inside the wall and off the stone.</summary>
        public bool IsFree(Vector2 point, float radius)
        {
            if (!ArenaShape.Contains(point, radius)) return false;

            foreach (var obstacle in Obstacles)
                if (Depth(Grown(obstacle, radius), point) > 0.01f) return false;

            return true;
        }

        /// <summary>
        /// The nearest place a body of this radius can stand, to the point asked for.
        ///
        /// A tap on a crate is not a mistake to be thrown away: it is a request to go to the crate,
        /// and the nearest ground beside it is the answer. Pushed out along the nearest face of the
        /// grown obstacle, which for a convex shape is the shortest way off it.
        /// </summary>
        public Vector2 NearestFree(Vector2 point, float radius)
        {
            float clear = radius + CornerMargin;

            // Twice round: pushing off one obstacle can in principle land on another, and pulling
            // in from the wall can land on either. The layout keeps these apart, so a second pass
            // is a guard rather than a loop that has to converge.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var obstacle in Obstacles)
                    PushOutOf(Grown(obstacle, clear), ref point);

                var still = Vector2.zero;
                ArenaShape.Bounce(ref point, ref still, clear);
            }

            return point;
        }

        /// <summary>
        /// Moves a body of this radius off any obstacle it has ended up inside.
        ///
        /// A path keeps a runner clear on its own; this is for everything that moves a man without
        /// asking - the recoil of a collision, a mace throwing him back - and would otherwise leave
        /// him standing in a crate.
        /// </summary>
        public void PushOut(ref Vector2 position, float radius)
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var obstacle in Obstacles)
                    PushOutOf(Grown(obstacle, radius), ref position);
        }

        // ------------------------------------------------------------------
        // running
        // ------------------------------------------------------------------

        /// <summary>Whether a body of this radius can run straight from one point to the other.</summary>
        public bool IsClear(Vector2 from, Vector2 to, float radius)
        {
            foreach (var obstacle in Obstacles)
                if (OverlapLength(Grown(obstacle, radius), from, to) > OverlapTolerance) return false;
            return true;
        }

        /// <summary>
        /// The shortest way for a body of this radius from one point to another, as the corners it
        /// runs through - starting at <paramref name="from"/> and ending at the free point nearest
        /// <paramref name="to"/>.
        ///
        /// If there is no way at all, the answer is to stay put: a single point. The layout is built
        /// so that never happens, and standing still is a better failure than a straight line into
        /// a wall.
        /// </summary>
        public List<Vector2> FindPath(Vector2 from, Vector2 to, float radius)
        {
            to = NearestFree(to, radius);
            var path = new List<Vector2> { from };

            if (IsClear(from, to, radius))
            {
                path.Add(to);
                return path;
            }

            var graph = GraphFor(radius);
            int n = graph.Corners.Count;
            int start = n, goal = n + 1;

            // Dijkstra over the cached corners plus this query's two ends. Few enough nodes - a
            // few dozen - that the plain quadratic version is faster than anything with a heap.
            var cost = new float[n + 2];
            var previous = new int[n + 2];
            var done = new bool[n + 2];
            for (int i = 0; i < cost.Length; i++)
            {
                cost[i] = float.MaxValue;
                previous[i] = -1;
            }
            cost[start] = 0f;

            Vector2 PointOf(int i) => i == start ? from : i == goal ? to : graph.Corners[i];

            bool Sees(int a, int b)
            {
                if (a < n && b < n) return graph.Visible[a, b];
                return IsClear(PointOf(a), PointOf(b), radius);
            }

            for (int round = 0; round < n + 2; round++)
            {
                int current = -1;
                for (int i = 0; i < cost.Length; i++)
                    if (!done[i] && cost[i] < float.MaxValue && (current < 0 || cost[i] < cost[current]))
                        current = i;

                if (current < 0 || current == goal) break;
                done[current] = true;

                for (int next = 0; next < cost.Length; next++)
                {
                    if (done[next] || next == current || next == start) continue;
                    if (!Sees(current, next)) continue;

                    float through = cost[current] + Vector2.Distance(PointOf(current), PointOf(next));
                    if (through < cost[next])
                    {
                        cost[next] = through;
                        previous[next] = current;
                    }
                }
            }

            if (previous[goal] < 0) return path;   // nowhere to go: stand still

            var corners = new List<Vector2>();
            for (int i = previous[goal]; i != start; i = previous[i])
                corners.Add(PointOf(i));
            corners.Reverse();

            path.AddRange(corners);
            path.Add(to);
            return path;
        }

        /// <summary>The length of a path, corner to corner.</summary>
        public static float Length(IReadOnlyList<Vector2> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
            return length;
        }

        /// <summary>
        /// The same path cut off after the given distance, for drawing how far a run actually gets
        /// inside one phase.
        /// </summary>
        public static List<Vector2> Truncate(IReadOnlyList<Vector2> path, float distance)
        {
            var cut = new List<Vector2>();
            if (path.Count == 0) return cut;

            cut.Add(path[0]);
            float left = distance;
            for (int i = 1; i < path.Count; i++)
            {
                float leg = Vector2.Distance(path[i - 1], path[i]);
                if (leg >= left)
                {
                    cut.Add(leg > 0.0001f ? Vector2.Lerp(path[i - 1], path[i], left / leg) : path[i]);
                    return cut;
                }
                cut.Add(path[i]);
                left -= leg;
            }
            return cut;
        }

        // ------------------------------------------------------------------
        // the visibility graph, cached per radius
        // ------------------------------------------------------------------

        private sealed class Graph
        {
            public readonly List<Vector2> Corners = new List<Vector2>();
            public bool[,] Visible;
        }

        private Graph GraphFor(float radius)
        {
            int key = Mathf.RoundToInt(radius * 100f);
            if (_graphs.TryGetValue(key, out var cached)) return cached;

            var graph = new Graph();

            // A corner that is outside the wall, or tucked inside some other grown obstacle, is not
            // somewhere he can be - it would be a node the search could route him through.
            foreach (var obstacle in Obstacles)
                foreach (var corner in Grown(obstacle, radius + CornerMargin))
                    if (IsFree(corner, radius))
                        graph.Corners.Add(corner);

            int n = graph.Corners.Count;
            graph.Visible = new bool[n, n];
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    graph.Visible[i, j] = graph.Visible[j, i]
                        = IsClear(graph.Corners[i], graph.Corners[j], radius);

            _graphs[key] = graph;
            return graph;
        }

        // ------------------------------------------------------------------
        // convex polygon geometry
        // ------------------------------------------------------------------

        private readonly Dictionary<(Obstacle, int), Vector2[]> _grown = new Dictionary<(Obstacle, int), Vector2[]>();

        /// <summary>
        /// The obstacle grown outwards by a radius, as a polygon with sharp corners.
        ///
        /// Mitred rather than rounded, which makes the grown shape a little larger than the exact
        /// Minkowski sum at each corner - so a runner gives a corner a slightly wider berth than
        /// he strictly has to. That is the conservative direction to be wrong in, and it keeps the
        /// grown shape a polygon, which is what everything else here needs.
        /// </summary>
        private Vector2[] Grown(Obstacle obstacle, float radius)
        {
            var key = (obstacle, Mathf.RoundToInt(radius * 100f));
            if (_grown.TryGetValue(key, out var cached)) return cached;

            var outline = obstacle.Outline;
            int count = outline.Length;
            var grown = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                var before = OutwardNormal(outline[(i + count - 1) % count], outline[i]);
                var after = OutwardNormal(outline[i], outline[(i + 1) % count]);
                grown[i] = outline[i] + (before + after) * (radius / (1f + Vector2.Dot(before, after)));
            }

            _grown[key] = grown;
            return grown;
        }

        /// <summary>The outward normal of an edge of an anticlockwise polygon.</summary>
        private static Vector2 OutwardNormal(Vector2 a, Vector2 b)
        {
            var edge = b - a;
            return new Vector2(edge.y, -edge.x).normalized;
        }

        /// <summary>
        /// How far inside a convex polygon a point is: positive inside, negative outside, measured
        /// to the nearest face.
        /// </summary>
        private static float Depth(Vector2[] polygon, Vector2 point)
        {
            float outside = float.MinValue;
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var normal = OutwardNormal(a, polygon[(i + 1) % polygon.Length]);
                outside = Mathf.Max(outside, Vector2.Dot(normal, point - a));
            }
            return -outside;
        }

        /// <summary>Moves a point that is inside a convex polygon onto its nearest face.</summary>
        private static void PushOutOf(Vector2[] polygon, ref Vector2 point)
        {
            float nearest = float.MinValue;
            var push = Vector2.zero;

            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var normal = OutwardNormal(a, polygon[(i + 1) % polygon.Length]);
                float signed = Vector2.Dot(normal, point - a);
                if (signed >= 0f) return;   // outside this face, so outside the polygon

                if (signed > nearest)
                {
                    nearest = signed;
                    push = normal * -signed;
                }
            }

            point += push;
        }

        /// <summary>
        /// How much of a segment lies inside a convex polygon, in virtual units.
        ///
        /// Cyrus-Beck: each face of a convex shape is a half-plane, and the part of a line inside
        /// all of them is one interval. Measured as a length rather than answered yes or no, so a
        /// segment that only grazes a face - which is what a run along the edge of a grown obstacle
        /// does - can be told apart from one that actually goes through.
        /// </summary>
        private static float OverlapLength(Vector2[] polygon, Vector2 from, Vector2 to)
        {
            var direction = to - from;
            float enter = 0f, leave = 1f;

            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var normal = OutwardNormal(a, polygon[(i + 1) % polygon.Length]);

                float start = Vector2.Dot(normal, from - a);
                float along = Vector2.Dot(normal, direction);

                if (Mathf.Abs(along) < 1e-9f)
                {
                    if (start > 0f) return 0f;   // parallel to this face and on the outside of it
                    continue;
                }

                float t = -start / along;
                if (along < 0f) enter = Mathf.Max(enter, t);
                else leave = Mathf.Min(leave, t);

                if (enter > leave) return 0f;
            }

            return (leave - enter) * direction.magnitude;
        }
    }
}
