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
    /// squad, the opponent's under his - and each strip has two places: the blessing on the left and
    /// the ability on the right. Something still working is something the player has to keep in
    /// mind, and a name that went up over a man once and faded is not remembered two rounds later.
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

        /// <summary>The two places in a side's strip.</summary>
        public enum Slot { Blessing = 0, Ability = 1 }

        private sealed class Callout
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Text Name;
            public Text Description;
            public Vector2 From;
            public float Age;

            /// <summary>Where it flies once it has been read; null for one that fades where it is.</summary>
            public Dock Target;
            public bool HandedOver;
            public string DockDescription;
            public Func<int> RoundsLeft;
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

            for (int i = 0; i < Capacity; i++)
            {
                var callout = HudFactory.CreateRect($"Callout_{i}", root);
                callout.anchorMin = callout.anchorMax = new Vector2(0.5f, 0.5f);
                callout.sizeDelta = new Vector2(360f, 70f);
                var group = callout.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;

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
                view._callouts.Add(new Callout { Root = callout, Group = group, Name = name, Description = description });
            }

            return view;
        }

        private static int DockIndex(PlayerSide side, Slot slot) => (side == PlayerSide.P1 ? 0 : 2) + (int)slot;

        /// <summary>
        /// One place in a strip: a dark plate, the rounds left in a disc on the left, the name beside
        /// it and what it does underneath. The blessing's half is on the left of the screen and the
        /// ability's on the right, the same way round for both sides.
        /// </summary>
        private static Dock BuildDock(RectTransform root, PlayerSide side, Slot slot, ViewPalette palette)
        {
            bool top = side == PlayerSide.Bot;
            bool left = slot == Slot.Blessing;

            var rect = HudFactory.CreateRect($"Dock_{side}_{slot}", root);
            rect.anchorMin = new Vector2(left ? 0f : 0.5f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(left ? 0.5f : 1f, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            float bottom = top ? -(BotDockTop + DockHeight) : PlayerDockBottom;
            rect.offsetMin = new Vector2(left ? 12f : 4f, bottom);
            rect.offsetMax = new Vector2(left ? -4f : -12f, bottom + DockHeight);

            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // A plate behind it: the strip lies over the wall, the sand and the torches, and white
            // type is unreadable on at least one of them.
            var back = HudFactory.CreatePanel("Back", rect, DockBackColor);
            back.raycastTarget = false;
            HudFactory.Stretch(back.rectTransform);

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

            var name = HudFactory.CreateLabel("Name", rect, "", DockNameSize, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            name.rectTransform.anchorMin = new Vector2(0f, 1f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.pivot = new Vector2(0.5f, 1f);
            name.rectTransform.offsetMin = new Vector2(40f, -33f);
            name.rectTransform.offsetMax = new Vector2(-6f, -3f);
            Outline(name, 1.5f);

            var description = HudFactory.CreateLabel("Description", rect, "", DockDescriptionSize, TextAnchor.UpperLeft);
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.rectTransform.anchorMin = Vector2.zero;
            description.rectTransform.anchorMax = Vector2.one;
            description.rectTransform.offsetMin = new Vector2(8f, 3f);
            description.rectTransform.offsetMax = new Vector2(-6f, -34f);
            Outline(description, 1f);

            rect.gameObject.SetActive(false);
            return new Dock { Root = rect, Group = group, Badge = badge, Timer = timer, Name = name, Description = description };
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

        private void Put(Vector2 virtualPos, PlayerSide side, string name, string description,
            string dockDescription, Dock target, Func<int> roundsLeft)
        {
            if (_arena == null) return;
            var camera = _arena.ArenaCamera;
            if (camera == null) return;

            var screen = camera.WorldToScreenPoint(_arena.ToWorld(virtualPos, SpawnHeight));
            if (screen.z <= 0f) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera(), out var local))
                return;

            var color = SideColor(side);
            var slot = Take();

            // Two off the same man at once - his ability and the blessing he is standing on go off on
            // the same step - printed one over the other as a single unreadable word. The later one
            // goes up above the one still being read.
            foreach (var other in _callouts)
                if (other != slot && other.Root.gameObject.activeSelf && other.Age < RiseSeconds
                    && Vector2.Distance(other.From, local) < StackSpacing)
                    local.y += StackSpacing;

            slot.From = local;
            slot.Age = 0f;
            slot.Target = target;
            slot.HandedOver = false;
            slot.RoundsLeft = roundsLeft;
            slot.DockDescription = dockDescription;
            slot.Name.text = name;
            slot.Name.color = color;
            slot.Description.text = description;
            slot.Description.color = HudFactory.TextColor;
            slot.Group.alpha = 1f;
            slot.Root.localScale = Vector3.one;
            slot.Root.anchoredPosition = local;
            slot.Root.SetAsLastSibling();
            slot.Root.gameObject.SetActive(true);
        }

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

            if (callout.Target == null || callout.Age < RiseSeconds)
            {
                if (callout.Target == null && callout.Age >= Seconds)
                {
                    callout.Root.gameObject.SetActive(false);
                    return;
                }
                callout.Root.anchoredPosition = over;

                // Solid for most of its time and gone over the last third: a name that starts
                // fading as it appears is hardest to read exactly when it is being read.
                callout.Group.alpha = callout.Target != null ? 1f : Mathf.Clamp01((1f - rise) * 3f);
                SetAlpha(callout.Description, 1f);
                return;
            }

            // Down into the strip, shrinking to the size its name is set at there. What it does
            // goes first - the strip says it again - and the name hands over to the strip's own as
            // it arrives, so the two are never both there at full strength.
            float f = Mathf.Clamp01((callout.Age - RiseSeconds) / FlySeconds);
            float eased = f * f * (3f - 2f * f);
            var start = callout.From + new Vector2(0f, RiseDistance * (1f - (1f - RiseSeconds / Seconds) * (1f - RiseSeconds / Seconds)));
            callout.Root.anchoredPosition = Vector2.Lerp(start, DockCentre(callout.Target), eased);
            callout.Root.localScale = Vector3.one * Mathf.Lerp(1f, DockNameSize / (float)NameSize, eased);
            SetAlpha(callout.Description, 1f - Mathf.Clamp01(f * 2.5f));
            callout.Group.alpha = f < HandOver ? 1f : 1f - (f - HandOver) / (1f - HandOver);

            if (!callout.HandedOver && f >= HandOver)
            {
                callout.HandedOver = true;
                Fill(callout.Target, callout);
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
