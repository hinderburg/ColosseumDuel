using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>Reports the size and axis of the weapon models, and whether the rig's hands resolve.</summary>
    public static class GearProbe
    {
        [MenuItem("Tools/Colosseum/Probe gear", priority = 63)]
        public static void Probe()
        {
            foreach (var path in new[] { "Assets/DoubleL/Model/SM_Wep_Sword_03.fbx",
                                         "Assets/DoubleL/Model/SM_Wep_Shield_01.fbx" })
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) { Debug.Log($"[Gear] MISSING {path}"); continue; }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                try
                {
                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) { Debug.Log($"[Gear] {path}: no renderers"); continue; }

                    var bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                    Debug.Log($"[Gear] {path}: size={bounds.size} centre={bounds.center} " +
                              $"renderers={renderers.Length} mat='{renderers[0].sharedMaterial?.name}' " +
                              $"shader='{renderers[0].sharedMaterial?.shader.name}'");
                }
                finally { Object.DestroyImmediate(instance); }
            }

            var glad = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gladiator_Brutius.prefab");
            if (glad == null) { Debug.Log("[Gear] no gladiator prefab"); return; }

            var g = (GameObject)PrefabUtility.InstantiatePrefab(glad);
            try
            {
                var animator = g.GetComponentInChildren<Animator>(true);
                Debug.Log($"[Gear] animator={(animator == null ? "NULL" : "ok")} " +
                          $"humanoid={(animator != null && animator.isHuman)}");
                if (animator == null || !animator.isHuman) return;

                var root = g.transform;
                foreach (var bone in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                {
                    var t = animator.GetBoneTransform(bone);
                    if (t == null) { Debug.Log("[Gear] " + bone + ": NULL"); continue; }

                    Debug.Log("[Gear] " + bone + " " + t.name
                              + " right=" + root.InverseTransformDirection(t.right)
                              + " up=" + root.InverseTransformDirection(t.up)
                              + " fwd=" + root.InverseTransformDirection(t.forward)
                              + " lossy=" + t.lossyScale);
                }
            }
            finally { Object.DestroyImmediate(g); }
        }
    }
}
