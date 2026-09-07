using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// One weapon lying on the arena floor. ItemSystem keeps a fixed-size list and replaces entries
    /// in place, so a fixed pool of these views maps one-to-one onto it by index - no spawning or
    /// destroying during a match.
    ///
    /// All three kinds are built up front and toggled, because there are only three and swapping a
    /// mesh at runtime would allocate. Which one is showing is the whole of what the player reads
    /// off the sand: a pair of swords, a sword behind a shield, or a hammer.
    /// </summary>
    public sealed class ItemView : MonoBehaviour
    {
        /// <summary>How far above the sand a dropped weapon floats, so it is not in it.</summary>
        private const float LieHeight = 0.04f;

        private ArenaView _arena;
        private GameObject _dualSwords;
        private GameObject _swordAndShield;
        private GameObject _mace;

        public static ItemView Create(string name, Transform parent, ArenaView arena)
        {
            var palette = arena.Palette;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var view = root.AddComponent<ItemView>();
            view._arena = arena;

            float radius = arena.ScaleLength(GameConstants.ItemRadius);

            if (palette.SwordModel != null)
            {
                view._dualSwords = BuildDualSwords(palette, root.transform);
                view._swordAndShield = BuildSwordAndShield(palette, root.transform);
                view._mace = BuildMace(palette, root.transform);
            }
            else
            {
                // No model pack: three primitives that at least differ from each other.
                view._dualSwords = Primitive(palette, "DualSwords", root.transform, palette.Weapon,
                    new Vector3(radius * 1.6f, radius * 0.3f, radius * 0.4f), radius * 0.4f);
                view._swordAndShield = Primitive(palette, "SwordAndShield", root.transform, palette.Shield,
                    new Vector3(radius * 1.4f, radius * 0.3f, radius * 1.4f), radius * 0.4f);
                view._mace = Primitive(palette, "Mace", root.transform, palette.RandomItem,
                    new Vector3(radius * 0.8f, radius * 0.5f, radius * 2f), radius * 0.5f);
            }

            root.SetActive(false);
            return view;
        }

        /// <summary>Two blades side by side - the pair reads as a pair only if they are apart.</summary>
        private static GameObject BuildDualSwords(ViewPalette palette, Transform parent)
        {
            var holder = Holder("DualSwords", parent, GearSizes.SwordLength);
            var left = Lay(palette.SwordModel, holder.transform, "Left");
            var right = Lay(palette.SwordModel, holder.transform, "Right");

            // Offset across the blades rather than along them, in the holder's own unit space.
            left.transform.localPosition = new Vector3(0f, 0f, -GearSizes.PairSpread);
            right.transform.localPosition = new Vector3(0f, 0f, GearSizes.PairSpread);
            return holder;
        }

        private static GameObject BuildSwordAndShield(ViewPalette palette, Transform parent)
        {
            var holder = Holder("SwordAndShield", parent, GearSizes.SwordLength);
            var sword = Lay(palette.SwordModel, holder.transform, "Sword");
            sword.transform.localPosition = new Vector3(0f, 0f, -GearSizes.PairSpread);

            if (palette.ShieldModel != null)
            {
                var shield = Lay(palette.ShieldModel, holder.transform, "Shield");

                // The shield is sized against itself, not against the sword the holder is scaled to,
                // so the pair on the sand is the same pair the gladiator ends up carrying.
                float relative = GearSizes.ShieldHeight / GearSizes.SwordLength;
                shield.transform.localScale = Vector3.one * relative;
                shield.transform.localPosition = new Vector3(0f, 0f, GearSizes.PairSpread * 1.4f);
            }
            return holder;
        }

        private static GameObject BuildMace(ViewPalette palette, Transform parent)
        {
            var holder = Holder("Mace", parent, GearSizes.MaceLength);
            var model = palette.MaceModel != null ? palette.MaceModel : palette.SwordModel;
            Lay(model, holder.transform, "Model");
            return holder;
        }

        private static GameObject Holder(string name, Transform parent, float length)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = new Vector3(0f, LieHeight, 0f);
            holder.transform.localScale = Vector3.one * length;
            return holder;
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

        private static GameObject Primitive(ViewPalette palette, string name, Transform parent,
            Material material, Vector3 scale, float height)
        {
            var go = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cube), name, parent, material);
            go.transform.localScale = scale;
            go.transform.localPosition = new Vector3(0f, height, 0f);
            return go;
        }

        public void Sync(ArenaItem item)
        {
            bool visible = item != null;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            transform.localPosition = _arena.ToWorld(item.Pos);

            SetActive(_dualSwords, item.Kind == WeaponKind.DualSwords);
            SetActive(_swordAndShield, item.Kind == WeaponKind.SwordAndShield);
            SetActive(_mace, item.Kind == WeaponKind.TwoHandedMace);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }
    }
}
