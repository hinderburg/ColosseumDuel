using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// One pickup on the arena floor. ItemSystem keeps a fixed-size list and replaces entries in
    /// place, so a fixed pool of these views maps one-to-one onto it by index - no spawning or
    /// destroying during a match.
    /// </summary>
    public sealed class ItemView : MonoBehaviour
    {
        private ArenaView _arena;
        private GameObject _weapon;
        private GameObject _shield;
        private GameObject _random;

        public static ItemView Create(string name, Transform parent, ArenaView arena)
        {
            var palette = arena.Palette;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var view = root.AddComponent<ItemView>();
            view._arena = arena;

            float radius = arena.ScaleLength(GameConstants.ItemRadius);

            // One shape per kind, all pre-built and toggled - swapping meshes at runtime would
            // allocate, and there are only three kinds.
            view._weapon = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cube), "Weapon", root.transform, palette.Weapon);
            view._weapon.transform.localScale = new Vector3(radius * 0.8f, radius * 1.5f, radius * 0.5f);
            view._weapon.transform.localPosition = new Vector3(0f, radius * 0.9f, 0f);

            view._shield = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cylinder), "Shield", root.transform, palette.Shield);
            view._shield.transform.localScale = new Vector3(radius * 1.8f, radius * 0.2f, radius * 1.8f);
            view._shield.transform.localPosition = new Vector3(0f, radius * 0.3f, 0f);

            view._random = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Sphere), "Random", root.transform, palette.RandomItem);
            view._random.transform.localScale = Vector3.one * (radius * 1.4f);
            view._random.transform.localPosition = new Vector3(0f, radius * 0.7f, 0f);

            root.SetActive(false);
            return view;
        }

        public void Sync(ArenaItem item)
        {
            bool visible = item != null;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            transform.localPosition = _arena.ToWorld(item.Pos);

            _weapon.SetActive(item.Kind == ItemKind.Weapon);
            _shield.SetActive(item.Kind == ItemKind.Shield);
            _random.SetActive(item.Kind == ItemKind.Random);
        }
    }
}
