using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The visual for one gladiator on the arena floor: a body, a coloured helmet (blue = player,
    /// red = opponent, per the design doc), a facing marker, and HP/rage bars.
    ///
    /// Built from primitives in code rather than from a prefab so the whole scene stays
    /// regenerable by ProjectBootstrap - there is no binary asset to hand-wire or to lose.
    /// </summary>
    public sealed class GladiatorView : MonoBehaviour
    {
        private const float BarWidth = 0.95f;
        private const float BarHeight = 0.11f;
        private const float BarGap = 0.05f;

        /// <summary>Seconds a hit reaction lasts.</summary>
        private const float HitPunchTime = 0.22f;

        private ArenaView _arena;
        private Transform _model;
        private Transform _bars;

        /// <summary>Where the bars sit above a figure of ordinary height.</summary>
        private float _barHeight;
        private Transform _burst;
        private MeshRenderer _burstRenderer;
        private MaterialPropertyBlock _burstProperties;
        private float _hitPunchLeft;
        private float _burstLeft;
        private float _burstDuration;
        private float _burstMaxRadius;
        private Color _burstColor;
        private Transform _hpFill;
        private Transform _rageFill;
        private Transform _weaponMarker;
        private Transform _shieldMarker;
        private Transform _abilityMarker;
        private ViewPalette _palette;
        private GameObject[] _figures;
        private Renderer[] _figureRenderers;
        private Animator[] _figureAnimators;
        private Animator _animator;
        private GladiatorId? _shownFigure;

        public static GladiatorView Create(string name, Transform parent, ArenaView arena,
            Material bodyMaterial, Material helmetMaterial)
        {
            var palette = arena.Palette;

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<GladiatorView>();
            view._arena = arena;

            // World size derived from the simulation so the visual can never drift from the radius
            // collisions are actually resolved against.
            float radius = arena.ScaleLength(GameConstants.GladiatorRadius);
            float bodyHeight = radius * 2.2f;

            // The model is a separate child so it can spin to face a direction without dragging the
            // bars above the head around with it - those answer to the camera, not to the fight.
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            view._model = model.transform;

            view.BuildFigures(model.transform, palette, helmetMaterial, radius, bodyHeight);

            // Carried gear, in the hands. Falls back to tags beside the head when the model pack is
            // not imported and there are no hands to put anything in.
            view.BuildCarriedGear(palette);
            if (view._mainHand == null)
            {
                view._weaponMarker = MakeMarker(palette, "WeaponMarker", model.transform, palette.Weapon,
                    new Vector3(radius * 1.3f, bodyHeight * 0.75f, 0f), radius * 0.55f).transform;
                view._shieldMarker = MakeMarker(palette, "ShieldMarker", model.transform, palette.Shield,
                    new Vector3(-radius * 1.3f, bodyHeight * 0.75f, 0f), radius * 0.55f).transform;
            }

            // A ring at the feet while an ability buff is running.
            var ability = new GameObject("AbilityRing");
            ability.transform.SetParent(model.transform, false);
            ability.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            ability.AddComponent<MeshFilter>().sharedMesh =
                ViewPrimitives.CreateAnnulus(radius * 1.25f, radius * 1.7f, 48);
            var abilityRenderer = ability.AddComponent<MeshRenderer>();
            abilityRenderer.sharedMaterial = palette.BarRage;
            abilityRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view._abilityMarker = ability.transform;

            // --- bars, standing upright above the head and turned to face the camera ---
            // They used to lie flat on the ground, which only reads under a straight top-down view;
            // with the camera tilted they would be seen edge-on and squashed.
            var bars = new GameObject("Bars");
            bars.transform.SetParent(root.transform, false);
            // Generous, because a tilted camera foreshortens vertical offsets by roughly half.
            view._barHeight = bodyHeight * 3.4f;
            bars.transform.localPosition = new Vector3(0f, view._barHeight, 0f);
            view._bars = bars.transform;

            // An expanding ring for one-shot moments (a hit landing, an ability firing). Kept as a
            // single reusable object rather than spawned per event - at two gladiators there is
            // never more than one in flight, and nothing has to be allocated mid-match.
            var burst = new GameObject("Burst");
            burst.transform.SetParent(root.transform, false);
            burst.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            burst.AddComponent<MeshFilter>().sharedMesh = ViewPrimitives.CreateAnnulus(0.62f, 1f, 48); // thick enough to read at a glance
            view._burstRenderer = burst.AddComponent<MeshRenderer>();
            view._burstRenderer.sharedMaterial = palette.Burst;
            view._burstRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view._burstRenderer.receiveShadows = false;
            view._burstRenderer.enabled = false;
            view._burst = burst.transform;
            view._burstProperties = new MaterialPropertyBlock();

            view._hpFill = MakeBar(palette, "Hp", bars.transform, palette.BarBackground, palette.BarHp, 0f);
            view._rageFill = MakeBar(palette, "Rage", bars.transform, palette.BarBackground, palette.BarRage,
                -(BarHeight + BarGap));

            root.SetActive(false);
            return view;
        }

        /// <summary>
        /// Builds one figure per archetype and keeps them all, showing whichever is on the arena.
        ///
        /// All three up front rather than instantiating on each pick: a side swaps gladiator every
        /// round, and building a skinned hierarchy mid-match would hitch exactly at the moment the
        /// player is watching the reveal. Three idle skinned meshes cost nothing while disabled.
        /// </summary>
        private void BuildFigures(Transform parent, ViewPalette palette, Material helmetMaterial,
            float radius, float bodyHeight)
        {
            _palette = palette;
            _figures = new GameObject[GladiatorDef.All.Count];
            _figureRenderers = new Renderer[GladiatorDef.All.Count];
            _figureAnimators = new Animator[GladiatorDef.All.Count];

            for (int i = 0; i < GladiatorDef.All.Count; i++)
            {
                var def = GladiatorDef.All[i];
                var prefab = palette != null ? palette.FigureFor(def.Id) : null;

                var figure = prefab != null
                    ? Instantiate(prefab, parent)
                    : BuildPrimitiveFigure(palette, parent, helmetMaterial, radius, bodyHeight);

                figure.name = $"Figure_{def.Id}";

                // Build, so the three read apart by shape and not only by colour. Colour is also
                // what the danger rings, the hazard and the two side helmets are using; a broad
                // figure and a small thin one are legible past all of that, and from further away.
                // Width goes on both ground axes, since the camera can see him from any angle he
                // happens to be facing.
                figure.transform.localScale =
                    new Vector3(def.BuildWidth, def.BuildHeight, def.BuildWidth);

                // Seated here rather than when the prefab was built, and after the build scale, so
                // it is measured against the figure that will actually be drawn. Seated once at
                // scale 1 it slid off both the broad archetype and the small one.
                SeatHelmet(figure);

                var animator = figure.GetComponentInChildren<Animator>(true);
                _figureAnimators[i] = animator;
                WearHelmet(figure, animator);

                // Hidden only after it has been measured: a renderer reports the bounds it was
                // authored with either way, but measuring what is on screen means having it there.
                figure.SetActive(false);

                // Replace the imported materials outright rather than tinting them. Tinting left
                // three figures that cast shadows and drew nothing: whatever the model ships with
                // does not survive being recoloured, and chasing that is not worth it when the
                // archetype colour is the whole point. A flat opaque material per archetype also
                // matches how the rest of the arena is drawn.
                var renderer = figure.GetComponentInChildren<Renderer>(true);
                var body = palette != null ? palette.BodyMaterialFor(def.Id) : null;
                if (renderer != null && body != null)
                {
                    var slots = new Material[renderer.sharedMaterials.Length];
                    for (int slot = 0; slot < slots.Length; slot++) slots[slot] = body;
                    renderer.sharedMaterials = slots;
                }
                _figureRenderers[i] = renderer;

                // The helmet carries the owning side's colour, so the same archetype on opposite
                // sides is still tellable apart at a glance.
                // Through the children, not off the object itself: the helmet is now a model with
                // its renderer one level down, and reading only the top level left every helmet in
                // the game wearing the pack's own material - which is to say, neither side's colour.
                var helmet = FindHelmet(figure);
                if (helmet != null && helmetMaterial != null)
                {
                    foreach (var helmetRenderer in helmet.GetComponentsInChildren<Renderer>(true))
                    {
                        var slots = new Material[Mathf.Max(1, helmetRenderer.sharedMaterials.Length)];
                        for (int slot = 0; slot < slots.Length; slot++) slots[slot] = helmetMaterial;
                        helmetRenderer.sharedMaterials = slots;
                    }
                }


                _figures[i] = figure;
            }
        }

        /// <summary>
        /// How far the crown of the helm stands over the crown of the bare head, as a share of the
        /// helm's own height.
        ///
        /// Positive, and it has to be. At -0.04 the helm sat a shade inside the skull, and the
        /// camera looks down at sixty-six degrees - so the top of a gladiator, which is most of
        /// what he shows from up there, was hair with a metal ring around it.
        /// </summary>
        private const float HelmetProudOfCrown = 0.14f;

        /// <summary>
        /// Drops the helmet onto the head of the figure as it will actually be drawn.
        ///
        /// The one fact available about where a skull is: the top of a gladiator IS the top of his
        /// head, because nothing on him is higher. Lining the crown of the helm up with it seats the
        /// helm whatever the rig calls its bones and wherever it decided to put them.
        ///
        /// Done here, after the archetype has been scaled, rather than once when the prefab was
        /// built. The prefab is shared by three builds - a fifth broader, or fifteen percent shorter
        /// - and a seat measured at scale 1 is only correct at scale 1: on the others the helm rode
        /// clear of the head with the whole face in the open.
        /// </summary>
        /// <summary>
        /// Puts the seated helmet on the head bone, so it goes wherever the head goes.
        ///
        /// It was a child of the figure root, which meant it never followed the animation at all:
        /// it hung in the air at the spot the head occupies in the bind pose while the model bobbed
        /// through a run underneath it. The death clip made that unmissable - the body falls over
        /// and the helmet stays exactly where it was, in mid-air, above nobody.
        ///
        /// Reparented keeping its world pose rather than by working out an offset in bone space.
        /// A bone's axes have nothing to do with the figure's - this rig's right hand has -X running
        /// up the body - so any offset written by hand is a guess to be corrected by eye. Seating it
        /// against the figure first and letting the reparent preserve that is the same answer with
        /// no guess in it.
        /// </summary>
        private static void WearHelmet(GameObject figure, Animator animator)
        {
            if (animator == null || !animator.isHuman) return;

            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var helmet = FindHelmet(figure);
            if (head == null || helmet == null) return;

            helmet.SetParent(head, worldPositionStays: true);
        }

        /// <summary>
        /// The helmet, wherever it has ended up.
        ///
        /// By name through the whole figure rather than as a direct child, because once it is worn
        /// it is several bones deep. A direct-child lookup silently returned nothing, which is the
        /// quiet kind of wrong: the helmet simply stopped being painted the side's colour.
        /// </summary>
        private static Transform FindHelmet(GameObject figure)
        {
            foreach (var t in figure.GetComponentsInChildren<Transform>(true))
                if (t.name == "Helmet") return t;
            return null;
        }

        private static void SeatHelmet(GameObject figure)
        {
            var helmet = FindHelmet(figure);
            if (helmet == null) return;

            var head = WorldBounds(helmet.gameObject, null);
            var body = WorldBounds(figure, helmet);
            if (head.size.y < 0.0001f || body.size.y < 0.0001f) return;

            // Through the parent's scale, because the correction is measured in world units and
            // applied to a local offset - and the parent's scale is the entire reason this has to
            // happen at all.
            float lift = body.max.y + head.size.y * HelmetProudOfCrown - head.max.y;
            float parentScale = Mathf.Abs(figure.transform.lossyScale.y);
            if (parentScale < 0.0001f) return;

            helmet.localPosition += new Vector3(0f, lift / parentScale, 0f);
        }

        /// <summary>World bounds of every renderer under <paramref name="root"/>, minus one subtree.</summary>
        private static Bounds WorldBounds(GameObject root, Transform excluding)
        {
            var result = new Bounds();
            bool any = false;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (excluding != null && renderer.transform.IsChildOf(excluding)) continue;
                if (!any) { result = renderer.bounds; any = true; }
                else result.Encapsulate(renderer.bounds);
            }

            return any ? result : new Bounds();
        }

        // --- carried gear ---

        /// <summary>Where along its own length a weapon is held. See GearSizes.GripAlong.</summary>
        private static float GripAlongBlade => GearSizes.GripAlong(WeaponKind.DualSwords);

        private Transform _mainHand;      // holder, on the right hand
        private Transform _mainSword;
        private Transform _mainMace;
        private Transform _offHand;       // holder, on the left hand
        private Transform _offSword;
        private Transform _offShield;
        private WeaponKind? _shownWeapon;
        private bool _shownGilded;

        /// <summary>
        /// The gear a gladiator carries, built once and re-parented to whichever archetype is
        /// currently on the arena.
        ///
        /// Re-parented rather than built per figure: there are three figures and a side only ever
        /// fields one at a time, so a few objects moved on a pick beats a pile sitting disabled -
        /// and it keeps the carried gear from having three copies of its state to keep in step.
        ///
        /// Two hands, and everything a hand can hold is built into it. Which of them shows is the
        /// whole of how the three weapons read apart on the arena: a blade in each fist, a blade and
        /// a shield, or one hammer.
        ///
        /// Each piece sits in a holder rather than being parented to the hand itself, so the grip
        /// offset and the world size are set on the holder and the model inside it is left exactly
        /// as GearPrefabs made it.
        /// </summary>
        private void BuildCarriedGear(ViewPalette palette)
        {
            if (palette == null || palette.SwordModel == null) return;

            var main = new GameObject("HeldWeapon");
            main.transform.SetParent(_model, false);
            _mainHand = main.transform;
            _mainSword = Grip(palette.SwordModel, _mainHand, "Sword");
            _mainMace = palette.MaceModel != null
                ? Grip(palette.MaceModel, _mainHand, "Mace", GearSizes.GripAlong(WeaponKind.TwoHandedMace))
                : null;
            _mainHand.gameObject.SetActive(false);

            var off = new GameObject("HeldOffHand");
            off.transform.SetParent(_model, false);
            _offHand = off.transform;
            _offSword = Grip(palette.SwordModel, _offHand, "Sword");
            _offShield = palette.ShieldModel != null ? Grip(palette.ShieldModel, _offHand, "Shield", 0f) : null;
            _offHand.gameObject.SetActive(false);
        }

        /// <summary>
        /// One piece inside its holder, pushed along its length so the fist is at the grip.
        ///
        /// Carried gear is washed cool steel, against the gold of the copies lying on the sand: the
        /// two are the same models, and the colour is the only thing that says which of them is the
        /// better one worth crossing the arena for.
        ///
        /// It also gets a red shell, hidden until he is holding something he was never trained in.
        /// Built here rather than switched on demand, because building a mesh copy at the moment a
        /// gladiator runs over the wrong weapon is a hitch exactly when the player is watching.
        /// </summary>
        private Transform Grip(GameObject model, Transform holder, string name,
            float? alongLength = null)
        {
            var instance = Instantiate(model, holder).transform;
            instance.name = name;
            instance.localPosition = new Vector3(0f, alongLength ?? GripAlongBlade, 0f);
            ItemView.Tint(instance.gameObject, GearSizes.CarriedTint);

            var untrained = _palette != null ? _palette.GearUntrained : null;
            if (untrained != null)
            {
                var shell = Instantiate(model, instance).transform;
                shell.name = ItemView.ShellName;
                shell.localPosition = Vector3.zero;
                shell.localScale = GearSizes.UntrainedShell;
                foreach (var renderer in shell.GetComponentsInChildren<Renderer>(true))
                {
                    var slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                    for (int i = 0; i < slots.Length; i++) slots[i] = untrained;
                    renderer.sharedMaterials = slots;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                shell.gameObject.SetActive(false);
                _untrainedShells.Add(shell.gameObject);
            }

            instance.gameObject.SetActive(false);
            return instance;
        }

        /// <summary>Every red shell, shown together whenever he is carrying the wrong weapon.</summary>
        private readonly System.Collections.Generic.List<GameObject> _untrainedShells =
            new System.Collections.Generic.List<GameObject>();

        /// <summary>
        /// Hangs the carried gear off the current figure's hand bones: weapon in the right, whatever
        /// the off hand carries in the left.
        ///
        /// Reached through the humanoid rig rather than by looking for bones by name - the avatar is
        /// what maps a rig's own naming onto LeftHand and RightHand, and hunting for "Hand_L" works
        /// right up until a model names it something else.
        /// </summary>
        private void AttachGearTo(Animator animator)
        {
            if (animator == null || !animator.isHuman) return;

            Reparent(_mainHand, animator.GetBoneTransform(HumanBodyBones.RightHand), WeaponGrip);
            Reparent(_offHand, animator.GetBoneTransform(HumanBodyBones.LeftHand), OffHandGrip);

            // A new figure's bones carry a different scale, so the sizes are recomputed against
            // them rather than settled once at build time.
            _shownWeapon = null;
        }

        /// <summary>Shows what the gladiator is actually carrying, at the size it was on the sand.</summary>
        private void SyncCarriedGear(GladiatorInstance g)
        {
            if (_mainHand == null) return;

            bool armed = g.Weapon != WeaponKind.None;
            if (_mainHand.gameObject.activeSelf != armed) _mainHand.gameObject.SetActive(armed);

            bool offHanded = g.Weapon == WeaponKind.DualSwords || g.Weapon == WeaponKind.SwordAndShield;
            if (_offHand != null && _offHand.gameObject.activeSelf != offHanded)
                _offHand.gameObject.SetActive(offHanded);

            if (armed && (_shownWeapon != g.Weapon || _shownGilded != g.WeaponIsGilded))
            {
                _shownWeapon = g.Weapon;
                _shownGilded = g.WeaponIsGilded;

                // Steel for the one he walked in with, gold for the one he took off the sand. The
                // brief says weapons in hand are silver, and that is what the loadout he starts a
                // round with looks like - but a gilded weapon that turned back to steel the moment
                // it was picked up would hide the one thing crossing a mined arena bought him.
                var tint = g.WeaponIsGilded ? GearSizes.GildedTint : GearSizes.CarriedTint;
                ItemView.Tint(_mainHand.gameObject, tint);
                if (_offHand != null) ItemView.Tint(_offHand.gameObject, tint);

                bool mace = GearSizes.UsesMace(g.Weapon) && _mainMace != null;
                SetActive(_mainSword, !mace);
                SetActive(_mainMace, mace);

                bool shield = g.Weapon == WeaponKind.SwordAndShield && _offShield != null;
                SetActive(_offSword, offHanded && !shield);
                SetActive(_offShield, shield);

                // The same length it had lying on the sand. It used to be shortened in the fist
                // by a factor of its own, so the weapon the player crossed the arena for arrived
                // visibly smaller than the one they had been looking at.
                SetWorldSize(_mainHand, GearSizes.MainHandLength(g.Weapon));
                SetWorldSize(_offHand, shield ? GearSizes.ShieldHeight : GearSizes.SwordLength);
            }

            if (armed) TurnBladeFlatUpwards(_mainHand);
            if (offHanded && !g.HasShield) TurnBladeFlatUpwards(_offHand);

            // Ringed in red when it is not his weapon. The simulation lets him pick up anything -
            // the warning is the HUD's job, and a pickup that silently refused to happen would read
            // as a bug rather than as a mistake he made.
            bool wrongWeapon = armed && g.IsUntrained;
            foreach (var shell in _untrainedShells)
                if (shell.activeSelf != wrongWeapon) shell.SetActive(wrongWeapon);
        }

        /// <summary>
        /// Rolls the carried weapon about its own blade so the flat of it faces the sky.
        ///
        /// A blade is two hundredths of a unit thick and a sixth wide, and which of those the camera
        /// sees is the difference between a sword and a scratch. Left to the rig it was the scratch:
        /// the hand's roll is whatever the animator felt like, and from a camera looking down at
        /// sixty-six degrees the weapon read as a wire. This keeps the direction the hand is
        /// pointing - so it still swings with the arm - and settles only the roll, which nothing
        /// else has an opinion about.
        ///
        /// The same reasoning as a weapon lying on the sand, and it comes out the same way up, so a
        /// sword looks like the same object before and after it is picked up.
        /// </summary>
        private static void TurnBladeFlatUpwards(Transform holder)
        {
            var hand = holder.parent;
            if (hand == null) return;

            var blade = hand.rotation * WeaponGrip * Vector3.up;
            var across = Vector3.Cross(Vector3.up, blade);

            // Pointing at the sky, where "flat side up" means nothing. Rare - it would take a
            // gladiator holding the sword straight overhead - and the rig's own roll will do.
            if (across.sqrMagnitude < 0.0004f)
            {
                holder.localRotation = WeaponGrip;
                return;
            }

            holder.rotation = Quaternion.LookRotation(across.normalized, blade.normalized);
        }

        /// <summary>
        /// Sizes a holder so what it carries comes out the given number of world units long.
        ///
        /// Divided by the parent's scale rather than set outright: a hand bone carries the whole
        /// figure's scale, so gear parented to it inherits that on top of anything set here. Read
        /// off the bone rather than hard-coded, because the three figures are scaled to a common
        /// height from three different model heights and no single number is right for all of them.
        /// </summary>
        private static void SetWorldSize(Transform holder, float worldSize)
        {
            if (holder == null) return;

            var parent = holder.parent;
            float inherited = parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f;
            if (inherited < 0.0001f) inherited = 1f;
            holder.localScale = Vector3.one * (worldSize / inherited);
        }

        // How the gear sits in a fist, measured against this rig rather than guessed.
        //
        // A hand bone's axes have nothing to do with the figure's: on this rig the right hand's -X
        // runs up the body and the left hand's +Y runs forward. Every gear model runs along its own
        // +Y with the flat of it facing its own +X, so the sword is turned to lie along the right
        // hand's -X, and the shield gets that same turn plus a quarter about its length to bring its
        // face round from +X to where the last pack's shield had it. Left at identity, both lay flat
        // across the chest, which is exactly what the first attempt rendered.
        private static readonly Quaternion WeaponGrip = Quaternion.Euler(0f, 0f, 90f);
        private static readonly Quaternion OffHandGrip =
            Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, -90f, 0f);

        private static void Reparent(Transform gear, Transform hand, Quaternion grip)
        {
            if (gear == null || hand == null || gear.parent == hand) return;
            gear.SetParent(hand, false);
            gear.localPosition = Vector3.zero;
            gear.localRotation = grip;
        }

        /// <summary>Stand-in used when the model pack is not imported: the old capsule and sphere.</summary>
        private static GameObject BuildPrimitiveFigure(ViewPalette palette, Transform parent,
            Material helmetMaterial, float radius, float bodyHeight)
        {
            var figure = new GameObject("PrimitiveFigure");
            figure.transform.SetParent(parent, false);

            var body = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Capsule), "Body",
                figure.transform, palette.PlayerBody);
            body.transform.localScale = new Vector3(radius * 2f, bodyHeight * 0.5f, radius * 2f);
            body.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);

            var helmet = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Sphere), "Helmet",
                figure.transform, helmetMaterial);
            helmet.transform.localScale = Vector3.one * (radius * 1.5f);
            helmet.transform.localPosition = new Vector3(0f, bodyHeight * 0.92f, 0f);

            return figure;
        }

        /// <summary>Shows the figure for whoever is currently fighting.</summary>
        private void ShowFigureFor(GladiatorId id)
        {
            if (_figures == null || _shownFigure == id) return;
            _shownFigure = id;

            for (int i = 0; i < _figures.Length; i++)
            {
                var def = GladiatorDef.All[i];
                bool isThisOne = def.Id == id;
                if (_figures[i].activeSelf != isThisOne) _figures[i].SetActive(isThisOne);
                if (!isThisOne) continue;

                _animator = _figureAnimators[i];

                // The bars ride on the root rather than on the figure, so they have to be told
                // about a shorter one - otherwise Hilius fights under a health bar floating a head
                // above where his head actually is.
                var barPos = _bars.localPosition;
                barPos.y = _barHeight * def.BuildHeight;
                _bars.localPosition = barPos;
            }

            AttachGearTo(_animator);
        }

        private static GameObject MakeMarker(ViewPalette palette, string name, Transform parent, Material material,
            Vector3 localPos, float size)
        {
            var go = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cube), name, parent, material);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * size;
            go.SetActive(false);
            return go;
        }

        /// <summary>Returns the fill transform; its local X scale is driven 0..1 by Sync.</summary>
        private static Transform MakeBar(ViewPalette palette, string name, Transform parent, Material background,
            Material fill, float verticalOffset)
        {
            var root = new GameObject(name + "Bar");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, verticalOffset, 0f);

            // Plain upright quads in the billboard's local XY plane; the parent turns them to camera.
            var bg = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Quad), "Background", root.transform, background);
            bg.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);

            // The fill is parented to a pivot sitting on the bar's left edge, so scaling the pivot
            // grows the bar rightwards instead of from the middle out.
            var pivot = new GameObject("FillPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(-BarWidth * 0.5f, 0f, -0.01f);

            var fillQuad = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Quad), "Fill", pivot.transform, fill);
            fillQuad.transform.localScale = new Vector3(BarWidth, BarHeight * 0.75f, 1f);
            fillQuad.transform.localPosition = new Vector3(BarWidth * 0.5f, 0f, 0f);

            return pivot.transform;
        }

        /// <summary>A blow just landed on this gladiator: squash the model and ring the impact.</summary>
        public void PlayHit() => TakeBlow(AnimatorParams.HitId);

        /// <summary>
        /// A blow that threw him back rather than one he only felt.
        ///
        /// The same path as a hit, down to the squash and the hold-off, because everything about
        /// taking a blow is the same - only the clip differs. Two copies of this method drifted
        /// apart the moment one of them was fixed.
        /// </summary>
        public void PlayKnockback() => TakeBlow(AnimatorParams.KnockbackId);

        private void TakeBlow(int trigger)
        {
            _blowTrigger = trigger;
            _hitPunchLeft = HitPunchTime;
            StartBurst(_arena.ScaleLength(GameConstants.GladiatorRadius) * 2.6f, 0.30f, Color.white);

            // The squash stays alongside the recoil animation rather than being replaced by it. The
            // clip reads at a standstill; at a sprint, with the two fighters crossing in a few
            // frames, the squash is what actually registers as a hit landing.
            //
            // The recoil itself waits if he is in the middle of his own swing. See _swingHoldLeft.
            if (_swingHoldLeft > 0f)
            {
                _hitWaiting = true;
                return;
            }

            if (_animator != null) _animator.SetTrigger(trigger);
            _hitThisFrame = true;
        }

        /// <summary>
        /// Seconds a swing is protected from being cut short by the recoil of the blow coming back.
        ///
        /// An exchange is simultaneous: both sides are struck and both sides swing, on the same
        /// frame. The controller enters Attack and Hit from Any State, and a transition consumes
        /// only its own trigger - so the attack was entered, the hit trigger was still standing, and
        /// the swing was replaced by the recoil one frame later. The visible result was two
        /// gladiators who flinched at each other and never appeared to attack at all.
        ///
        /// Long enough for the swing to read, short enough that the recoil still belongs to the
        /// blow that caused it.
        /// </summary>
        private const float SwingHoldsOffTheRecoil = 0.22f;

        private float _swingHoldLeft;
        private bool _hitWaiting;
        private bool _hitThisFrame;

        /// <summary>Which recoil the blow being taken calls for - a flinch, or a throw back.</summary>
        private int _blowTrigger = -1;

        /// <summary>This gladiator just dealt a blow.</summary>
        public void PlaySwing()
        {
            // A blow he was taking arrived earlier in this same frame - which half of the exchange
            // is announced first is an implementation detail of the loop, and without this the
            // fighter who happened to be struck first was the only one whose swing got cut off.
            // The trigger has not been consumed yet, so it can be taken back and re-fired after.
            if (_hitThisFrame && _animator != null)
            {
                if (_blowTrigger >= 0) _animator.ResetTrigger(_blowTrigger);
                _hitWaiting = true;
            }

            if (_animator != null) _animator.SetTrigger(AnimatorParams.AttackId);
            _swingHoldLeft = SwingHoldsOffTheRecoil;
        }

        /// <summary>Lets a held-back recoil through once the swing has had its moment.</summary>
        private void AdvanceSwingHold(float dt)
        {
            _hitThisFrame = false;
            if (_swingHoldLeft <= 0f) return;

            _swingHoldLeft = Mathf.Max(0f, _swingHoldLeft - dt);
            if (_swingHoldLeft > 0f || !_hitWaiting) return;

            _hitWaiting = false;
            if (_animator != null && _blowTrigger >= 0) _animator.SetTrigger(_blowTrigger);
        }

        /// <summary>This gladiator's ability just fired.</summary>
        public void PlayAbility(Color color)
        {
            StartBurst(_arena.ScaleLength(GameConstants.GladiatorRadius) * 6.5f, 0.55f, color);
        }

        private void StartBurst(float maxRadius, float duration, Color color)
        {
            _burstMaxRadius = maxRadius;
            _burstDuration = duration;
            _burstLeft = duration;
            _burstColor = color;
        }

        /// <summary>Pushes one frame of simulation state onto the visuals. Safe to call with null.</summary>
        public void Sync(GladiatorInstance g)
        {
            // A fallen gladiator stays on the sand rather than blinking out of existence. The round
            // holds for a moment after the killing blow, and that moment is the one the death
            // animation is for; the next round replaces him with whoever is picked.
            bool visible = g != null;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            ShowFigureFor(g.Def.Id);
            SyncAnimator(g);

            if (!g.Alive)
            {
                // Nothing above the head is worth reading on a body: an empty HP bar and the tags
                // for gear he is no longer carrying only clutter the end of the round.
                _bars.gameObject.SetActive(false);
                return;
            }

            if (!_bars.gameObject.activeSelf) _bars.gameObject.SetActive(true);
            transform.localPosition = _arena.ToWorld(g.Pos);

            var forward = new Vector3(g.Facing.x, 0f, g.Facing.y);
            if (forward.sqrMagnitude > 0.0001f)
                _model.localRotation = Quaternion.LookRotation(forward, Vector3.up);

            // Billboard the bars. The camera never moves, so this is the same rotation every frame -
            // but reading it from the camera keeps the two from drifting apart if the framing is
            // ever retuned, which it will be.
            if (_arena.ArenaCamera != null)
                _bars.rotation = _arena.ArenaCamera.transform.rotation;

            AdvanceEffects(Time.deltaTime);

            SetFill(_hpFill, g.Def.MaxHp > 0f ? g.Hp / g.Def.MaxHp : 0f);
            SetFill(_rageFill, g.Rage / GameConstants.RageMax);

            SetActive(_weaponMarker, g.Weapon != WeaponKind.None);
            SetActive(_shieldMarker, g.HasShield);
            SetActive(_abilityMarker, g.Buff.IsActive);
            SyncCarriedGear(g);
        }

        /// <summary>
        /// Translates one frame of simulation state into animator parameters.
        ///
        /// Speed is converted to world units per second because that is what the run threshold is
        /// expressed in - the simulation's own units are a different scale entirely, and mixing the
        /// two would put a walking gladiator into a sprint or leave a sprinting one standing still.
        /// </summary>
        private void SyncAnimator(GladiatorInstance g)
        {
            if (_animator == null) return;

            _animator.SetBool(AnimatorParams.DeadId, !g.Alive);
            if (!g.Alive) return;

            _animator.SetFloat(AnimatorParams.SpeedId, g.Vel.magnitude * _arena.VirtualToWorld);
            _animator.SetBool(AnimatorParams.DefendingId, g.IsDefending);
            _animator.SetBool(AnimatorParams.TwoHandedId, g.Weapon == WeaponKind.TwoHandedMace);

            SyncRunDirection(g);
        }

        /// <summary>
        /// Which way he is running, in his own frame, for the run blend.
        ///
        /// Worked out from Facing and Vel directly rather than by asking the figure's transform to
        /// convert a world vector. The figure is turned from Facing in this same frame, so the
        /// transform is either the same answer or last frame's - and the maths is two dot products.
        ///
        /// A unit vector, not a velocity: the blend picks which cycle to play and the separate
        /// Speed parameter decides whether to play one at all. Feeding it real speed would put the
        /// blend somewhere between the idle at the centre and a run at the rim, which reads as a
        /// man wading.
        /// </summary>
        private void SyncRunDirection(GladiatorInstance g)
        {
            var vel = g.Vel;
            if (vel.sqrMagnitude > 0.0001f) vel.Normalize();

            var facing = g.Facing;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector2.up;

            // His right is his facing turned a quarter clockwise: the model's forward is
            // (Facing.x, 0, Facing.y) in world terms, and Unity's right-handed-Y turn takes that to
            // (Facing.y, 0, -Facing.x).
            float ahead = Vector2.Dot(vel, facing);
            float across = vel.x * facing.y - vel.y * facing.x;

            _animator.SetFloat(AnimatorParams.MoveXId, across, RunBlendDamping, Time.deltaTime);
            _animator.SetFloat(AnimatorParams.MoveZId, ahead, RunBlendDamping, Time.deltaTime);
        }

        /// <summary>
        /// Seconds the run blend takes to follow a change of direction.
        ///
        /// Damped rather than snapped because the direction can reverse in a single frame - a bounce
        /// off another gladiator does exactly that - and an unsmoothed blend cuts from one cycle to
        /// the opposite one mid-stride.
        /// </summary>
        private const float RunBlendDamping = 0.12f;

        /// <summary>
        /// Seconds the red flicker of a bleed lasts. Longer than a hit reaction and much softer:
        /// nobody swung, so it should read as something happening to him rather than to him from
        /// someone else.
        /// </summary>
        private const float BleedFlashTime = 0.55f;

        private static readonly Color BleedColor = new Color(0.75f, 0.05f, 0.05f);
        private float _bleedFlashLeft;
        private MaterialPropertyBlock _figureProperties;

        /// <summary>A wound just cost this gladiator health at the top of a cycle.</summary>
        public void PlayBleed()
        {
            _bleedFlashLeft = BleedFlashTime;
        }

        /// <summary>
        /// Washes the figure towards red and back.
        ///
        /// Through a property block rather than by touching the material: the three archetype
        /// materials are shared assets, and tinting one would turn both sides' Brutius red at once -
        /// including the one who is not bleeding.
        /// </summary>
        private void AdvanceBleedFlash(float dt)
        {
            if (_shownFigure == null) return;

            Renderer renderer = null;
            for (int i = 0; i < GladiatorDef.All.Count; i++)
                if (GladiatorDef.All[i].Id == _shownFigure.Value) renderer = _figureRenderers[i];
            if (renderer == null) return;

            if (_bleedFlashLeft <= 0f)
            {
                if (_figureProperties != null)
                {
                    renderer.SetPropertyBlock(null);
                    _figureProperties = null;
                }
                return;
            }

            _bleedFlashLeft = Mathf.Max(0f, _bleedFlashLeft - dt);

            // In and back out over the life of the flash, so it pulses once rather than snapping on
            // and fading - a snap at this size reads as a rendering glitch.
            float t = Mathf.Sin((1f - _bleedFlashLeft / BleedFlashTime) * Mathf.PI);
            var baseColor = _palette != null ? _palette.ArchetypeColor(_shownFigure.Value) : Color.white;

            _figureProperties = _figureProperties ?? new MaterialPropertyBlock();
            _figureProperties.SetColor(BaseColorId, Color.Lerp(baseColor, BleedColor, t * 0.75f));
            renderer.SetPropertyBlock(_figureProperties);
        }

        private void AdvanceEffects(float dt)
        {
            AdvanceBleedFlash(dt);
            AdvanceSwingHold(dt);

            // Hit reaction: a quick squash-and-recover on the model only, so the bars above the head
            // stay put and readable while it plays.
            if (_hitPunchLeft > 0f)
            {
                _hitPunchLeft = Mathf.Max(0f, _hitPunchLeft - dt);
                float t = _hitPunchLeft / HitPunchTime;          // 1 at impact, 0 when recovered
                float punch = Mathf.Sin(t * Mathf.PI) * 0.28f;   // in and back out
                _model.localScale = new Vector3(1f + punch, 1f - punch * 0.6f, 1f + punch);
            }
            else if (_model.localScale != Vector3.one)
            {
                _model.localScale = Vector3.one;
            }

            if (_burstLeft <= 0f)
            {
                if (_burstRenderer.enabled) _burstRenderer.enabled = false;
                return;
            }

            _burstLeft = Mathf.Max(0f, _burstLeft - dt);
            float progress = 1f - _burstLeft / _burstDuration; // 0 -> 1 over the burst

            _burstRenderer.enabled = true;
            float radius = Mathf.Lerp(_burstMaxRadius * 0.25f, _burstMaxRadius, progress);
            _burst.localScale = new Vector3(radius, 1f, radius);

            // Per-instance alpha through a property block: the burst material is a shared asset, and
            // tinting it directly would fade both gladiators' rings at once.
            var color = _burstColor;
            // Hold the alpha up early and drop it late: a linear fade over sand spends most of its
            // life too faint to notice, which made the effect read as if it were not firing at all.
            color.a = Mathf.Pow(1f - progress, 0.55f);
            _burstProperties.SetColor(BaseColorId, color);
            _burstRenderer.SetPropertyBlock(_burstProperties);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static void SetFill(Transform pivot, float t)
        {
            t = Mathf.Clamp01(t);
            var scale = pivot.localScale;
            scale.x = t;
            pivot.localScale = scale;
            // A zero-width quad still renders a hairline; hide it outright instead.
            pivot.gameObject.SetActive(t > 0.001f);
        }

        private static void SetActive(Transform t, bool active)
        {
            if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
        }
    }
}
