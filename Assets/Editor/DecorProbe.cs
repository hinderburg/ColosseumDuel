using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>Reports the real size and colour of the modular arena pieces, so placement and
    /// tinting are measured rather than guessed.</summary>
    public static class DecorProbe
    {
        [MenuItem("Tools/Colosseum/Probe arena decor", priority = 62)]
        public static void Probe()
        {
            string[] names =
            {
                "wall/Wall_A_1x1", "wall/Wall_B_1x1", "wall/Wall_A_2x1", "wall/Wall_B_2x1",
                "wall/Wall_Post_B_1m", "wall/Wall_Post_B_2m", "wall/Wall_Crnr_A_1m",
                "floor/Ground_A_1x1", "floor/Ground_B_1x1", "floor/Ground_C_1x1",
                "floor/Ground_Edge_1m", "stair/Rail_A_1m", "stair/Rail_B_1m",
            };

            foreach (var name in names)
            {
                string path = $"Assets/LoafbrrAssets/ModularArena/Prefabs/{name}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.Log($"[Decor] MISSING {name}"); continue; }

                var filter = prefab.GetComponentInChildren<MeshFilter>(true);
                var renderer = prefab.GetComponentInChildren<Renderer>(true);
                var mesh = filter != null ? filter.sharedMesh : null;
                var material = renderer != null ? renderer.sharedMaterial : null;

                string colours = "none";
                if (mesh != null && mesh.colors32 != null && mesh.colors32.Length > 0)
                    colours = string.Join(" ", mesh.colors32.Take(4).Select(c => $"({c.r},{c.g},{c.b})"));

                string props = "none";
                if (material != null)
                {
                    var shader = material.shader;
                    props = string.Join(" ", Enumerable.Range(0, shader.GetPropertyCount())
                        .Select(i => $"{shader.GetPropertyName(i)}:{shader.GetPropertyType(i)}"));
                }

                Debug.Log($"[Decor] {name}: size={(renderer != null ? renderer.bounds.size.ToString() : "?")} " +
                          $"mat='{material?.name}' vcol={colours}");
                Debug.Log($"[Decor]   props {props}");
            }
        }
    }
}
