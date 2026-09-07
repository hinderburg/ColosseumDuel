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
        /// <summary>How far above the sand a dropped weapon or shield floats, so it is not in it.</summary>
        private const float LieHeight = 0.04f;

        private ArenaView _arena;
        private GameObject _weapon;
        private Transform _weaponHolder;
        private GameObject _sword;
        private GameObject _greatsword;
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
            if (palette.SwordModel != null)
            {
                view._weapon = new GameObject("Weapon");
                view._weapon.transform.SetParent(root.transform, false);
                view._weapon.transform.localPosition = new Vector3(0f, LieHeight, 0f);
                view._weaponHolder = view._weapon.transform;

                // Both weapons under one holder, and the holder scaled to whichever is showing. The
                // pool never changes size, and the test that a two-hander lies there visibly bigger
                // has one number to read.
                view._sword = Lay(palette.SwordModel, view._weapon.transform, "Sword");
                view._greatsword = palette.GreatswordModel != null
                    ? Lay(palette.GreatswordModel, view._weapon.transform, "Greatsword")
                    : null;
            }
            else
            {
                view._weapon = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cube), "Weapon",
                    root.transform, palette.Weapon);
                view._weapon.transform.localScale = new Vector3(radius * 0.8f, radius * 1.5f, radius * 0.5f);
                view._weapon.transform.localPosition = new Vector3(0f, radius * 0.9f, 0f);
            }

            if (palette.ShieldModel != null)
            {
                view._shield = new GameObject("Shield");
                view._shield.transform.SetParent(root.transform, false);
                view._shield.transform.localPosition = new Vector3(0f, LieHeight, 0f);
                view._shield.transform.localScale = Vector3.one * GearSizes.ShieldHeight;
                Lay(palette.ShieldModel, view._shield.transform, "Model");
            }
            else
            {
                view._shield = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cylinder), "Shield",
                    root.transform, palette.Shield);
                view._shield.transform.localScale = new Vector3(radius * 1.8f, radius * 0.2f, radius * 1.8f);
                view._shield.transform.localPosition = new Vector3(0f, radius * 0.3f, 0f);
            }

            view._random = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Sphere), "Random",
                root.transform, palette.RandomItem);
            view._random.transform.localScale = Vector3.one * (radius * 1.4f);
            view._random.transform.localPosition = new Vector3(0f, radius * 0.7f, 0f);

            root.SetActive(false);
            return view;
        }

        /// <summary>Drops one model on the sand, broad side up. The prefab is unit-sized already.</summary>
        private static GameObject Lay(GameObject model, Transform parent, string name)
        {
            var instance = Instantiate(model, parent);
            instance.name = name;
            instance.transform.localRotation = GearSizes.LyingDown;
            instance.transform.localPosition = Vector3.zero;
            return instance;
        }

        public void Sync(ArenaItem item)
        {
            bool visible = item != null;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            transform.localPosition = _arena.ToWorld(item.Pos);

            // Anything that hands over a weapon is drawn as one, which includes the third "random"
            // slot: it rolls a weapon type and grants it on pickup, but used to sit there as a
            // featureless sphere. Half the two-handers on the arena were therefore invisible as
            // such, and running onto one destroyed the shield the player was carrying with no
            // warning that it would.
            bool armsHim = item.WeaponType != WeaponType.None
                           && (item.Kind == ItemKind.Weapon || item.Kind == ItemKind.Random);

            _weapon.SetActive(armsHim);
            _shield.SetActive(item.Kind == ItemKind.Shield);
            _random.SetActive(item.Kind == ItemKind.Random && !armsHim);

            // Which weapon is on the sand, and how big. Both are the same reading for the player:
            // the longer blade is the two-hander, and picking it up costs them their shield.
            if (_weaponHolder != null && armsHim)
            {
                bool twoHanded = item.WeaponType == WeaponType.TwoHanded && _greatsword != null;
                if (_sword.activeSelf == twoHanded) _sword.SetActive(!twoHanded);
                if (_greatsword != null && _greatsword.activeSelf != twoHanded)
                    _greatsword.SetActive(twoHanded);

                _weaponHolder.localScale = Vector3.one * GearSizes.WeaponLength(item.WeaponType);
            }
        }
    }
}
