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
        /// <summary>
        /// How much bigger a two-handed weapon is than a one-handed one.
        ///
        /// The same sword model at a larger scale rather than a second model, so the two read as
        /// the same kind of object and the difference between them is legible as size alone.
        /// </summary>
        public const float TwoHandedScale = 1.5f;

        /// <summary>
        /// How a weapon lies on the sand: blade along the arena's long axis, flat face upwards.
        ///
        /// Built from where the axes have to end up rather than from Euler angles, because the two
        /// that matter here pull in different directions. The blade is the model's +Y and its flat
        /// face is its +Z, so simply tipping it over about X lays the blade down but stands the flat
        /// face on edge - and a blade six hundredths of a unit thick, seen edge-on from above, is a
        /// scratch on the sand. Mapping +Z to world X puts the flat of the blade under the camera.
        /// </summary>
        private static readonly Quaternion LyingDown =
            Quaternion.LookRotation(Vector3.right, Vector3.forward);

        /// <summary>
        /// Half the blade's length, to shift the model back onto the pickup point.
        ///
        /// The model is pivoted at the grip, so laid down as-is the whole sword trails off to one
        /// side of the spot the simulation will actually let a gladiator pick it up from.
        /// </summary>
        private const float BladeHalfLength = 0.59f;

        private ArenaView _arena;
        private GameObject _weapon;
        private GameObject _shield;
        private GameObject _random;
        private Transform _weaponModel;

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

                // The model is nested under a holder so the lie-down rotation and the one/two-handed
                // scale can be set independently of each other.
                var sword = Instantiate(palette.SwordModel, view._weapon.transform);
                sword.name = "Model";
                sword.transform.localRotation = LyingDown;
                sword.transform.localPosition = new Vector3(-BladeHalfLength, 0f, 0f);
                view._weaponModel = view._weapon.transform;
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
                view._shield = Instantiate(palette.ShieldModel, root.transform);
                view._shield.name = "Shield";
                // Face up, so what is seen from above is the face of the shield rather than its rim.
                view._shield.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                view._shield.transform.localPosition = new Vector3(0f, radius * 0.25f, 0f);
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

            // A two-hander is the same sword, larger. That size is the only thing on the floor that
            // tells the player which of the two they are running at, and it decides whether they
            // get to keep their shield.
            if (_weaponModel != null && armsHim)
                _weaponModel.localScale = item.WeaponType == WeaponType.TwoHanded
                    ? Vector3.one * TwoHandedScale
                    : Vector3.one;
        }
    }
}
