using System;
using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The name of an ability - or of the blessing - going up over the man who took it: the name
    /// large, what it does in smaller type underneath. Then, for anything that lasts, the name flies
    /// down into its side's strip, a size smaller, and stays there for as long as it is working,
    /// with what it does beside it and the rounds it has left counting down.
    ///
    /// Each side has a strip by its own squad - the player's in the band between the arena and his
    /// squad, the opponent's under his - and each strip has three places: the blessing on the left,
    /// the ability in the middle, and on the right a shelf with the pictures of the boons the side
    /// has taken, which stays up for the whole match. Something still working is something the
    /// player has to keep in mind, and a name that went up over a man once and faded is not
    /// remembered two rounds later.
    ///
    /// Both sides, because the moment the bot's Bulwark goes up is the moment the player has to stop
    /// charging it head on - and a ring on the sand in a colour he has not learned says nothing.
    ///
    /// Drawn on the HUD canvas and projected from the arena, like the damage numbers and for the
    /// same reasons: the font is proven in a build, and a screen-space label stays the same size
    /// whichever end of the arena it comes off.
    /// </summary>
    public sealed class AbilityCalloutView : MonoBehaviour
    {
        /// <summary>Both sides can fire on the same phase; two more for a restart landing on top.</summary>
        private const int Capacity = 4;

        /// <summary>How long one stays over the man, in real seconds, when it has nowhere to go.</summary>
        public const float Seconds = 1.8f;

        /// <summary>How long one that lasts stays over the man before it flies down: long enough to read.</summary>
        public const float RiseSeconds = 1.15f;

        /// <summary>How long the flight down into the strip takes.</summary>
        public const float FlySeconds = 0.45f;

        /// <summary>How far one climbs while it is up, in canvas units.</summary>
        private const float RiseDistance = 44f;

        /// <summary>Where it starts: above the head and the bars over it.</summary>
        private const float SpawnHeight = 3.4f;

        /// <summary>How far above another one off the same man a second one is put: a name and its line.</summary>
        private const float StackSpacing = 72f;

        public const int NameSize = 34;
        public const int DescriptionSize = 17;

        /// <summary>
        /// The name once it is in the strip: smaller than over the man, and still a name that reads
        /// at a glance - the strip is looked at in the corner of the eye, mid-decision.
        /// </summary>
        public const int DockNameSize = 21;
        public const int DockDescriptionSize = 13;
        public const int DockTimerSize = 18;

        /// <summary>
        /// The player's strip, in canvas units from the bottom: clear above his squad, and under the
        /// control hint, which sits at 190.
        /// </summary>
        public const float PlayerDockBottom = 121f;

        /// <summary>The opponent's strip, in canvas units from the top: clear below his squad.</summary>
        public const float BotDockTop = 124f;

        public const float DockHeight = 66f;

        /// <summary>How long a place in the strip takes to come up or go, in real seconds.</summary>
        private const float DockFadeSeconds = 0.25f;

        /// <summary>How far into the flight the strip starts to take over from the name flying into it.</summary>
        private const float HandOver = 0.65f;

        private static readonly Color DockBackColor = new Color(0.04f, 0.04f, 0.06f, 0.62f);

        /// <summary>
        /// How the strip is divided, as shares of its width: the blessing and the ability get a place
        /// each and the boon shelf the rest - three pictures' worth, which is as many boons as a side
        /// can take in a match of three men.
        /// </summary>
        private const float BlessingShare = 0.36f, AbilityShare = 0.36f;

        /// <summary>The pictures on the shelf, and the room round them.</summary>
        private const float ShelfPictureWidth = 40f, ShelfPictureHeight = 27f, ShelfPadding = 6f;

        /// <summary>The picture on a callout, beside the name: the boon's painting, for the boons.</summary>
        private const float CalloutArtWidth = 66f, CalloutArtHeight = 44f;

        /// <summary>The two counted places in a side's strip. The shelf is the third, and has no count.</summary>
        public enum Slot { Blessing = 0, Ability = 1 }

        private sealed class Callout
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Art;
            public Text Name;
            public Text Description;
            public Vector2 From;
            public float Age;

            /// <summary>Where it flies once it has been read; null for one that fades where it is.</summary>
            public Dock Target;

            /// <summary>Or the shelf it flies to, carrying this boon's picture.</summary>
            public Shelf ShelfTarget;
            public BoonKey Boon;

            public bool HandedOver;
            public string DockDescription;
            public Func<int> RoundsLeft;
        }

        /// <summary>
        /// The pictures of the boons a side has taken, in the order it took them. Filled as each
        /// picture flies in, and kept true to the match by <see cref="SyncBoons"/> - a picture whose
        /// flight was never seen still arrives, and a restart clears the shelf.
        /// </summary>
        private sealed class Shelf
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image[] Pictures;
            public readonly List<BoonKey> Keys = new List<BoonKey>();
            public readonly HashSet<BoonKey> InFlight = new HashSet<BoonKey>();
            public bool Showing;
            public float Alpha;
        }

        private sealed class Dock
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Badge;
            public Text Timer;
            public Text Name;
            public Text Description;
            public Color SideColor;
            public Func<int> RoundsLeft;
            public bool Showing;
            public float Alpha;
        }

        private readonly List<Callout> _callouts = new List<Callout>();
        private readonly Dock[] _docks = new Dock[4];
        private readonly Shelf[] _shelves = new Shelf[2];
        private RectTransform _root;
        private ArenaView _arena;
        private int _next;

        public static AbilityCalloutView Create(Transform canvas, ArenaView arena)
        {
            var root = HudFactory.CreateRect("AbilityCallouts", canvas);
            HudFactory.Stretch(root);

            var view = root.gameObject.AddComponent<AbilityCalloutView>();
            view._root = root;
            view._arena = arena;
            var palette = arena != null ? arena.Palette : null;

            // The strips first, so a name flying into one draws over it.
            foreach (PlayerSide side in new[] { PlayerSide.P1, PlayerSide.Bot })
            foreach (Slot slot in new[] { Slot.Blessing, Slot.Ability })
                view._docks[DockIndex(side, slot)] = BuildDock(root, side, slot, palette);
            foreach (PlayerSide side in new[] { PlayerSide.P1, PlayerSide.Bot })
                view._shelves[ShelfIndex(side)] = BuildShelf(root, side);

            for (int i = 0; i < Capacity; i++)
            {
                var callout = HudFactory.CreateRect($"Callout_{i}", root);
                callout.anchorMin = callout.anchorMax = new Vector2(0.5f, 0.5f);
                callout.sizeDelta = new Vector2(360f, 70f);
                var group = callout.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;

                // A picture on the left, for the callouts that have one; the words take the whole
                // width when there is none (see Put).
                var art = HudFactory.CreatePanel("Art", callout, Color.white);
                art.raycastTarget = false;
                art.preserveAspect = true;
                art.rectTransform.anchorMin = art.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                art.rectTransform.pivot = new Vector2(0f, 0.5f);
                art.rectTransform.sizeDelta = new Vector2(CalloutArtWidth, CalloutArtHeight);
                art.rectTransform.anchoredPosition = new Vector2(8f, 0f);
                art.enabled = false;

                var name = HudFactory.CreateLabel("Name", callout, "", NameSize);
                name.fontStyle = FontStyle.Bold;
                name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                name.rectTransform.anchorMax = new Vector2(1f, 1f);
                name.rectTransform.offsetMin = Vector2.zero;
                name.rectTransform.offsetMax = Vector2.zero;
                Outline(name, 2f);

                var description = HudFactory.CreateLabel("Description", callout, "", DescriptionSize);
                description.rectTransform.anchorMin = new Vector2(0f, 0f);
                description.rectTransform.anchorMax = new Vector2(1f, 0.5f);
                description.rectTransform.offsetMin = Vector2.zero;
                description.rectTransform.offsetMax = Vector2.zero;
                Outline(description, 1.5f);

                callout.gameObject.SetActive(false);
                view._callouts.Add(new Callout { Root = callout, Group = group, Art = art, Name = name, Description = description });
            }

            return view;
        }

        private static int DockIndex(PlayerSide side, Slot slot) => (side == PlayerSide.P1 ? 0 : 2) + (int)slot;

        private static int ShelfIndex(PlayerSide side) => side == PlayerSide.P1 ? 0 : 1;

        /// <summary>
        /// The frame of one place in a side's strip: across the share of the width it gets, in the
        /// band by that side's squad, with a plate behind it - the strip lies over the wall, the
        /// sand and the torches, and white type is unreadable on at least one of them.
        /// </summary>
        private static RectTransform StripPlace(RectTransform root, string name, PlayerSide side,
            float fromShare, float toShare, out CanvasGroup group)
        {
            bool top = side == PlayerSide.Bot;
            var rect = HudFactory.CreateRect(name, root);
            rect.anchorMin = new Vector2(fromShare, top ? 1f : 0f);
            rect.anchorMax = new Vector2(toShare, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            float bottom = top ? -(BotDockTop + DockHeight) : PlayerDockBottom;
            rect.offsetMin = new Vector2(fromShare <= 0f ? 12f : 3f, bottom);
            rect.offsetMax = new Vector2(toShare >= 1f ? -12f : -3f, bottom + DockHeight);

            group = rect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var back = HudFactory.CreatePanel("Back", rect, DockBackColor);
            back.raycastTarget = false;
            HudFactory.Stretch(back.rectTransform);
            return rect;
        }

        /// <summary>
        /// One counted place in a strip: the rounds left in a disc on the left, the name beside it
        /// and what it does underneath. The blessing's place is on the left of the screen and the
        /// ability's in the middle, the same way round for both sides.
        /// </summary>
        private static Dock BuildDock(RectTransform root, PlayerSide side, Slot slot, ViewPalette palette)
        {
            bool left = slot == Slot.Blessing;
            var rect = StripPlace(root, $"Dock_{side}_{slot}", side,
                left ? 0f : BlessingShare, left ? BlessingShare : BlessingShare + AbilityShare, out var group);

            var badge = HudFactory.CreatePanel("TimerBadge", rect, Color.white);
            HudFactory.UseSprite(badge, palette != null && palette.Disc != null ? palette.Disc : HudFactory.RoundedSprite);
            badge.raycastTarget = false;
            var badgeRect = badge.rectTransform;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.sizeDelta = new Vector2(28f, 28f);
            badgeRect.anchoredPosition = new Vector2(6f, -5f);

            var timer = HudFactory.CreateLabel("Timer", badge.transform, "", DockTimerSize, TextAnchor.MiddleCenter, Color.white);
            timer.fontStyle = FontStyle.Bold;
            HudFactory.Stretch(timer.rectTransform);
            Outline(timer, 1f);

            // Both set to shrink to fit: the place is a third of the screen wide now that the boons
            // have the rest, and the longest descriptions ran to three lines at their full size.
            var name = HudFactory.CreateLabel("Name", rect, "", DockNameSize, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 14;
            name.resizeTextMaxSize = DockNameSize;
            name.rectTransform.anchorMin = new Vector2(0f, 1f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.pivot = new Vector2(0.5f, 1f);
            name.rectTransform.offsetMin = new Vector2(40f, -33f);
            name.rectTransform.offsetMax = new Vector2(-6f, -3f);
            Outline(name, 1.5f);

            var description = HudFactory.CreateLabel("Description", rect, "", DockDescriptionSize, TextAnchor.UpperLeft);
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.resizeTextForBestFit = true;
            description.resizeTextMinSize = 9;
            description.resizeTextMaxSize = DockDescriptionSize;
            description.rectTransform.anchorMin = Vector2.zero;
            description.rectTransform.anchorMax = Vector2.one;
            description.rectTransform.offsetMin = new Vector2(8f, 3f);
            description.rectTransform.offsetMax = new Vector2(-6f, -34f);
            Outline(description, 1f);

            rect.gameObject.SetActive(false);
            return new Dock { Root = rect, Group = group, Badge = badge, Timer = timer, Name = name, Description = description };
        }

        /// <summary>
        /// The shelf: the boons' pictures in a row, on the right of the strip. Three places, which is
        /// as many boons as a side can take; one picture per boon, in the order they were taken.
        /// </summary>
        private static Shelf BuildShelf(RectTransform root, PlayerSide side)
        {
            var rect = StripPlace(root, $"Shelf_{side}", side, BlessingShare + AbilityShare, 1f, out var group);

            var pictures = new Image[BoonDef.OfferSize];
            for (int i = 0; i < pictures.Length; i++)
            {
                var picture = HudFactory.CreatePanel($"Boon_{i}", rect, Color.white);
                picture.raycastTarget = false;
                picture.preserveAspect = true;
                picture.rectTransform.anchorMin = picture.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                picture.rectTransform.pivot = new Vector2(0f, 0.5f);
                picture.rectTransform.sizeDelta = new Vector2(ShelfPictureWidth, ShelfPictureHeight);
                picture.rectTransform.anchoredPosition = new Vector2(ShelfPadding + i * (ShelfPictureWidth + ShelfPadding), 0f);
                picture.enabled = false;
                pictures[i] = picture;
            }

            rect.gameObject.SetActive(false);
            return new Shelf { Root = rect, Group = group, Pictures = pictures };
        }

        /// <summary>
        /// A dark edge round the letters: they go up over sand, blood and gold, and plain text on
        /// all three is unreadable on at least one of them.
        /// </summary>
        private static void Outline(Text label, float width)
        {
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(width, -width);
        }

        /// <summary>
        /// Puts one ability's name up over the point he is standing on, in his side's colour - blue
        /// for the player's men, red for the opponent's - so whose ability it was reads before the
        /// name. Given the rounds it has left, one that lasts then goes down into its side's strip
        /// and stays there until that reaches nothing; one spent as it fires fades where it is.
        /// </summary>
        public void Show(Vector2 virtualPos, AbilityKey ability, PlayerSide side, Func<int> roundsLeft = null)
        {
            var def = AbilityDef.Get(ability);
            var target = roundsLeft != null && def.Rounds > 0 ? _docks[DockIndex(side, Slot.Ability)] : null;
            Put(virtualPos, side, def.Name, Capitalised(def.Summary), Capitalised(def.Description), target, roundsLeft);
        }

        /// <summary>The blessing, taken up off the sand: the same, into the left of his side's strip.</summary>
        public void ShowBlessing(Vector2 virtualPos, PlayerSide side, Func<int> roundsLeft)
        {
            int percent = Mathf.RoundToInt((GameConstants.WeaponBuffDamageMult - 1f) * 100f);
            string effect = $"+{percent}% damage with his weapon";
            Put(virtualPos, side, BlessingName, $"{effect} ({GameConstants.WeaponBuffRounds} rounds)", effect,
                _docks[DockIndex(side, Slot.Blessing)], roundsLeft);
        }

        public const string BlessingName = "Blessing";

        /// <summary>
        /// Something taken up off the sand that is spent the moment it is taken - the apple, the horn:
        /// its name over the man who took it, in his side's colour, and what it gave under it. It
        /// fades where it is; there is nothing still working for the strip to keep.
        /// </summary>
        public void ShowPickup(Vector2 virtualPos, PlayerSide side, string name, string description)
            => Put(virtualPos, side, name, description, description, null, null);

        /// <summary>
        /// A boon a side has just taken, over the man it sent out to earn it: its painting beside its
        /// name, what it does underneath, and then down onto the shelf in his side's strip, where the
        /// picture stays for the rest of the match.
        /// </summary>
        public void ShowBoon(Vector2 virtualPos, BoonKey key, PlayerSide side, Sprite picture)
        {
            var def = BoonDef.Get(key);
            var shelf = _shelves[ShelfIndex(side)];
            if (shelf.Keys.Contains(key)) return;
            shelf.InFlight.Add(key);
            var callout = Put(virtualPos, side, def.Name, Capitalised(def.Description), def.Description, null, null, picture);
            if (callout == null)
            {
                shelf.InFlight.Remove(key);
                return;
            }
            callout.ShelfTarget = shelf;
            callout.Boon = key;
        }

        private Callout Put(Vector2 virtualPos, PlayerSide side, string name, string description,
            string dockDescription, Dock target, Func<int> roundsLeft, Sprite picture = null)
        {
            if (_arena == null) return null;
            var camera = _arena.ArenaCamera;
            if (camera == null) return null;

            var screen = camera.WorldToScreenPoint(_arena.ToWorld(virtualPos, SpawnHeight));
            if (screen.z <= 0f) return null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera(), out var local))
                return null;

            var color = SideColor(side);
            var slot = Take();

            // Two off the same man at once - his ability and the blessing he is standing on go off on
            // the same step - printed one over the other as a single unreadable word. The later one
            // goes up above the one still being read.
            foreach (var other in _callouts)
                if (other != slot && other.Root.gameObject.activeSelf && other.Age < RiseSeconds
                    && Vector2.Distance(other.From, local) < StackSpacing)
                    local.y += StackSpacing;

            // Shifted over when it carries a picture, so the picture and the words together hang about

            // centred over the man rather than the words alone.

            if (picture != null) local.x += CalloutArtWidth * 0.6f;


            slot.From = local;
            slot.Age = 0f;
            slot.Target = target;
            slot.ShelfTarget = null;
            slot.HandedOver = false;
            slot.RoundsLeft = roundsLeft;
            slot.DockDescription = dockDescription;
            slot.Name.text = name;
            slot.Name.color = color;
            slot.Description.text = description;
            slot.Description.color = HudFactory.TextColor;

            // With a picture the words sit against it, and the pair is shifted so it hangs about
            // centred over the man; without one the words are centred and take the width.
            HudFactory.UseSprite(slot.Art, picture);
            slot.Art.enabled = picture != null;
            float textLeft = picture != null ? CalloutArtWidth + 16f : 0f;
            slot.Name.rectTransform.offsetMin = new Vector2(textLeft, 0f);
            slot.Description.rectTransform.offsetMin = new Vector2(textLeft, 0f);
            slot.Name.alignment = picture != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            slot.Description.alignment = picture != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;

            slot.Group.alpha = 1f;
            slot.Root.localScale = Vector3.one;
            slot.Root.anchoredPosition = local;
            slot.Root.SetAsLastSibling();
            slot.Root.gameObject.SetActive(true);
            return slot;
        }

        /// <summary>
        /// Keeps a side's shelf true to the boons it has: one that arrived without its flight being
        /// seen goes up at once, and one the match no longer has - a restart - comes down. A boon
        /// whose picture is on its way is left to arrive.
        /// </summary>
        public void SyncBoons(PlayerSide side, IEnumerable<BoonKey> boons, Func<BoonKey, Sprite> pictureOf)
        {
            var shelf = _shelves[ShelfIndex(side)];
            if (shelf == null) return;

            var wanted = new List<BoonKey>(boons ?? System.Linq.Enumerable.Empty<BoonKey>());
            bool changed = shelf.Keys.RemoveAll(k => !wanted.Contains(k)) > 0;
            foreach (var key in wanted)
                if (!shelf.Keys.Contains(key) && !shelf.InFlight.Contains(key) && shelf.Keys.Count < shelf.Pictures.Length)
                {
                    shelf.Keys.Add(key);
                    changed = true;
                }
            shelf.InFlight.RemoveWhere(k => !wanted.Contains(k));

            if (changed) LayOut(shelf, pictureOf);
            shelf.Showing = shelf.Keys.Count > 0;
            if (shelf.Showing && !shelf.Root.gameObject.activeSelf) shelf.Root.gameObject.SetActive(true);
        }

        private static void LayOut(Shelf shelf, Func<BoonKey, Sprite> pictureOf)
        {
            for (int i = 0; i < shelf.Pictures.Length; i++)
            {
                bool present = i < shelf.Keys.Count;
                var sprite = present && pictureOf != null ? pictureOf(shelf.Keys[i]) : null;
                HudFactory.UseSprite(shelf.Pictures[i], sprite);
                shelf.Pictures[i].enabled = present && sprite != null;
            }
        }

        /// <summary>Whether this boon's picture is on this side's shelf. For tests.</summary>
        public bool IsShelved(PlayerSide side, BoonKey key)
        {
            var shelf = _shelves[ShelfIndex(side)];
            return shelf != null && shelf.Keys.Contains(key) && shelf.Root.gameObject.activeSelf;
        }

        /// <summary>The shelf itself, for tests that read what is on it.</summary>
        public RectTransform ShelfFor(PlayerSide side) => _shelves[ShelfIndex(side)]?.Root;

        private static Color SideColor(PlayerSide side) => side == PlayerSide.P1 ? HudFactory.PlayerColor : HudFactory.BotColor;

        private static string Capitalised(string text)
            => string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);

        /// <summary>Null for the Overlay canvas the game ships with; the canvas camera otherwise.</summary>
        private Camera UiCamera()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        private Callout Take()
        {
            foreach (var callout in _callouts)
                if (!callout.Root.gameObject.activeSelf) return callout;

            var reused = _callouts[_next];
            _next = (_next + 1) % _callouts.Count;
            return reused;
        }

        /// <summary>Whether something is showing in this place of this side's strip. For tests.</summary>
        public bool IsDocked(PlayerSide side, Slot slot)
        {
            var dock = _docks[DockIndex(side, slot)];
            return dock != null && dock.Showing && dock.Root.gameObject.activeSelf;
        }

        /// <summary>The strip's place itself, for tests that read what it says.</summary>
        public RectTransform DockFor(PlayerSide side, Slot slot) => _docks[DockIndex(side, slot)]?.Root;

        /// <summary>
        /// Unscaled: an ability fires at the top of the action phase and the next planning phase
        /// slows the world to a fifth - on scaled time the name would hang over the next decision.
        /// </summary>
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var callout in _callouts)
                if (callout.Root.gameObject.activeSelf) AdvanceCallout(callout, dt);
            foreach (var dock in _docks)
                if (dock != null && dock.Root.gameObject.activeSelf) AdvanceDock(dock, dt);
            foreach (var shelf in _shelves)
                if (shelf != null && shelf.Root.gameObject.activeSelf) AdvanceShelf(shelf, dt);
        }

        private static void AdvanceShelf(Shelf shelf, float dt)
        {
            shelf.Alpha = Mathf.MoveTowards(shelf.Alpha, shelf.Showing ? 1f : 0f, dt / DockFadeSeconds);
            shelf.Group.alpha = shelf.Alpha;
            if (!shelf.Showing && shelf.Alpha <= 0f) shelf.Root.gameObject.SetActive(false);
        }

        /// <summary>Where the next picture on the shelf will sit, in the space the callouts are placed in.</summary>
        private Vector2 ShelfSlotCentre(Shelf shelf)
        {
            int index = Mathf.Min(shelf.Keys.Count, shelf.Pictures.Length - 1);
            var rect = shelf.Pictures[index].rectTransform;
            var world = rect.TransformPoint(rect.rect.center);
            return _root.InverseTransformPoint(world);
        }

        /// <summary>The picture lands: onto the shelf, in the next free place, and the shelf comes up if it was empty.</summary>
        private static void Arrive(Shelf shelf, Callout from)
        {
            shelf.InFlight.Remove(from.Boon);
            if (shelf.Keys.Contains(from.Boon) || shelf.Keys.Count >= shelf.Pictures.Length) return;
            int index = shelf.Keys.Count;
            shelf.Keys.Add(from.Boon);
            HudFactory.UseSprite(shelf.Pictures[index], from.Art.sprite);
            shelf.Pictures[index].enabled = from.Art.sprite != null;
            shelf.Showing = true;
            shelf.Root.gameObject.SetActive(true);
        }

        private void AdvanceCallout(Callout callout, float dt)
        {
            callout.Age += dt;

            // Rising over the man, whether or not it is going anywhere after.
            float rise = Mathf.Clamp01(callout.Age / Seconds);
            var over = callout.From + new Vector2(0f, RiseDistance * (1f - (1f - rise) * (1f - rise)));

            // Something that has already stopped working - a clash over while it was being read -
            // has no place to go to, and fades where it is like anything else.
            if (callout.Target != null && !callout.HandedOver && StillWorking(callout.RoundsLeft) <= 0)
                callout.Target = null;

            bool flies = callout.Target != null || callout.ShelfTarget != null;
            if (!flies || callout.Age < RiseSeconds)
            {
                if (!flies && callout.Age >= Seconds)
                {
                    callout.Root.gameObject.SetActive(false);
                    return;
                }
                callout.Root.anchoredPosition = over;

                // Solid for most of its time and gone over the last third: a name that starts
                // fading as it appears is hardest to read exactly when it is being read.
                callout.Group.alpha = flies ? 1f : Mathf.Clamp01((1f - rise) * 3f);
                SetAlpha(callout.Description, 1f);
                return;
            }

            // Down into the strip, shrinking to the size its name is set at there - or, for a boon,
            // to the size its picture is on the shelf. What it does goes first - the strip says it
            // again - and the name hands over to the strip's own as it arrives, so the two are never
            // both there at full strength.
            float f = Mathf.Clamp01((callout.Age - RiseSeconds) / FlySeconds);
            float eased = f * f * (3f - 2f * f);
            var start = callout.From + new Vector2(0f, RiseDistance * (1f - (1f - RiseSeconds / Seconds) * (1f - RiseSeconds / Seconds)));
            bool toShelf = callout.Target == null;
            var destination = toShelf ? ShelfSlotCentre(callout.ShelfTarget) : DockCentre(callout.Target);
            float endScale = toShelf ? ShelfPictureWidth / CalloutArtWidth : DockNameSize / (float)NameSize;
            callout.Root.anchoredPosition = Vector2.Lerp(start, destination, eased);
            callout.Root.localScale = Vector3.one * Mathf.Lerp(1f, endScale, eased);
            SetAlpha(callout.Description, 1f - Mathf.Clamp01(f * 2.5f));
            if (toShelf) SetAlpha(callout.Name, 1f - Mathf.Clamp01(f * 2.5f));
            callout.Group.alpha = f < HandOver ? 1f : 1f - (f - HandOver) / (1f - HandOver);

            if (!callout.HandedOver && f >= HandOver)
            {
                callout.HandedOver = true;
                if (toShelf) Arrive(callout.ShelfTarget, callout);
                else Fill(callout.Target, callout);
            }
            if (f >= 1f) callout.Root.gameObject.SetActive(false);
        }

        private static int StillWorking(Func<int> roundsLeft) => roundsLeft != null ? roundsLeft() : 0;

        /// <summary>Where a strip's place is, in the same space the callouts are placed in.</summary>
        private Vector2 DockCentre(Dock dock)
        {
            var world = dock.Root.TransformPoint(dock.Root.rect.center);
            return _root.InverseTransformPoint(world);
        }

        private static void Fill(Dock dock, Callout from)
        {
            dock.Name.text = from.Name.text;
            dock.SideColor = from.Name.color;
            dock.Name.color = from.Name.color;
            dock.Description.text = from.DockDescription;
            dock.RoundsLeft = from.RoundsLeft;
            dock.Showing = true;
            dock.Root.gameObject.SetActive(true);
            ShowRounds(dock, StillWorking(dock.RoundsLeft));
        }

        /// <summary>
        /// The count, and the disc it sits in: the side's colour while there is more to come, and
        /// yellow on the last round - the colour the blessed weapon turns on its last round, for the
        /// same reason.
        /// </summary>
        private static void ShowRounds(Dock dock, int rounds)
        {
            if (rounds <= 0) return;
            dock.Timer.text = rounds.ToString();
            dock.Badge.color = rounds == 1 ? HudFactory.RageColor : dock.SideColor;
        }

        private static void AdvanceDock(Dock dock, float dt)
        {
            int rounds = dock.Showing ? StillWorking(dock.RoundsLeft) : 0;
            if (rounds <= 0) dock.Showing = false;
            else ShowRounds(dock, rounds);

            dock.Alpha = Mathf.MoveTowards(dock.Alpha, dock.Showing ? 1f : 0f, dt / DockFadeSeconds);
            dock.Group.alpha = dock.Alpha;
            if (!dock.Showing && dock.Alpha <= 0f) dock.Root.gameObject.SetActive(false);
        }

        private static void SetAlpha(Text label, float alpha)
        {
            var color = label.color;
            color.a = alpha;
            label.color = color;
        }
    }
}
