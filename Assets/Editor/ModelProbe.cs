using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Reports what an imported character model actually contains - renderers, shaders and size.
    ///
    /// Worth having as a tool rather than a one-off: the tinting and scaling of a gladiator both
    /// depend on these answers, and guessing at them produces the quiet kind of failure where the
    /// model renders but ignores its colour.
    /// </summary>
    public static class ModelProbe
    {
        [MenuItem("Tools/Colosseum/Probe built gladiator prefab", priority = 61)]
        public static void ProbePrefab()
        {
            const string path = "Assets/Prefabs/Gladiator_Brutius.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning($"[Probe] No prefab at {path}."); return; }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Debug.Log($"[Probe] {renderer.name}: type={renderer.GetType().Name}, enabled={renderer.enabled}, " +
                              $"bounds={renderer.bounds.size}, materials={renderer.sharedMaterials.Length}, " +
                              $"shadows={renderer.shadowCastingMode}");

                    if (renderer is SkinnedMeshRenderer skinned)
                        Debug.Log($"[Probe]   skinned: mesh={(skinned.sharedMesh == null ? "NULL" : skinned.sharedMesh.name)}, " +
                                  $"bones={skinned.bones.Length}, rootBone={(skinned.rootBone == null ? "NULL" : skinned.rootBone.name)}, " +
                                  $"localBounds={skinned.localBounds.size}, updateWhenOffscreen={skinned.updateWhenOffscreen}");
                }

                Debug.Log($"[Probe] root scale={instance.transform.localScale}, children={instance.transform.childCount}");
                foreach (Transform child in instance.transform)
                    Debug.Log($"[Probe]   child '{child.name}' scale={child.localScale} active={child.gameObject.activeSelf}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [MenuItem("Tools/Colosseum/Probe gladiator model", priority = 60)]
        public static void Probe()
        {
            const string path = "Assets/DoubleL/Model/Armature.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Debug.LogWarning($"[Probe] No model at {path}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                Debug.Log($"[Probe] renderers: {renderers.Length}");

                var bounds = new Bounds(instance.transform.position, Vector3.zero);
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null) { Debug.Log($"[Probe] {renderer.name}: NULL material"); continue; }
                        Debug.Log($"[Probe] {renderer.name}: material '{material.name}', shader '{material.shader.name}', " +
                                  $"_BaseColor={material.HasProperty("_BaseColor")}, _Color={material.HasProperty("_Color")}");
                    }
                }

                Debug.Log($"[Probe] bounds size={bounds.size}, centre={bounds.center}");

                var animator = instance.GetComponentInChildren<Animator>();
                Debug.Log($"[Probe] animator: {(animator == null ? "none" : animator.avatar != null ? animator.avatar.name : "no avatar")}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
