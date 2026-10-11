using DG.Tweening;
using RTLTMPro;
using TMPro;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// The small card that explains a refusal, and then gets out of the way.
    ///
    /// Deliberately not one of the panels. It asks nothing, needs no answer and never blocks the game: it
    /// exists because a button that does nothing is worse than a button that says why. Pressing water with
    /// an empty drop counter used to be silent, and silence reads as a broken button rather than as a rule.
    ///
    /// Built like every other view - find first, create second - so it can be baked into the scene later and
    /// still works at runtime today without a re-bake.
    /// </summary>
    public sealed class NoticeView
    {
        /// <summary>Anchor for the card itself. Below centre, clear of the buttons that raise it.</summary>
        private static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        /// <summary>Anchor for the lines laid out downwards from the top edge of the card.</summary>
        private static readonly Vector2 Top = new Vector2(0.5f, 1f);

        /// <summary>How long the card stays before it leaves on its own.</summary>
        private const float StaySeconds = 3f;

        private readonly StringsStore _strings;

        private RectTransform _root;
        private RectTransform _card;
        private PanelJuice _juice;
        private RTLTextMeshPro _message;
        private RTLTextMeshPro _hint;

        /// <summary>
        /// The pending fade, kept so that a second notice cancels the first one's timer. Two notices must
        /// never race to hide each other, and the one shown last is the one that decides when the card goes.
        ///
        /// Fully qualified because this project has a Tween of its own for waiting, and an unqualified name
        /// here would resolve to that one instead of to DOTween's.
        /// </summary>
        private DG.Tweening.Tween _timer;

        public NoticeView(StringsStore strings)
        {
            _strings = strings;
        }

        /// <summary>True while the card is on screen. The root outlives it, so this asks about the card.</summary>
        public bool Visible => _card != null && _card.gameObject.activeSelf;

        /// <summary>Builds the card under the canvas, or refreshes the one already there.</summary>
        public void Build(Transform canvas)
        {
            _root = UiKit.Node("Notice", canvas);
            UiKit.Stretch(_root);

            // No scrim, on purpose. That is the whole difference between this and the update card: the
            // update card is a question and blocks the game, this one is a remark and must not.
            //
            // The card is a button so that a tap dismisses it early. The label is empty because the two
            // lines below are its real content, and a button without one still takes the click.
            LabeledButton card = UiKit.Button(
                "Card", _root, string.Empty, 1f, Palette.Panel, Palette.Ink, Hide);

            _card = card.Rect;
            UiKit.Place(_card, Middle, new Vector2(0f, -190f), new Vector2(620f, 150f));
            UiKit.AddShadow(_card, 10f, 0.2f);

            _message = UiKit.Text(
                "Message", _card, Text("water.no_drops"), 30f, Palette.Ink, UiKit.Centred, true,
                FontStyles.Bold);
            UiKit.Place(_message.rectTransform, Top, new Vector2(0f, -34f), new Vector2(560f, 48f));

            _hint = UiKit.Text(
                "Hint", _card, Text("water.no_drops_hint"), 22f, Palette.InkSoft, UiKit.Centred);
            UiKit.Place(_hint.rectTransform, Top, new Vector2(0f, -86f), new Vector2(560f, 40f));

            // Applied after the labels exist, and deliberately outside any authored check, so the value
            // reaches a card generated from code and a card placed by hand alike. It is a font property and
            // the text is never rewritten here: an RTL label re-shapes on assignment, so reading or writing
            // the string at this point would shape the sentence a second time.
            SetSpacing(_message);
            SetSpacing(_hint);

            // The component captures the card's own scale as its home, so a card re-skinned by hand at some
            // other size keeps that size rather than being flattened to one on its first appearance.
            _juice = UiKit.GetOrAdd<PanelJuice>(_card.gameObject);

            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows the notice, or replaces the one already up and restarts its timer.
        ///
        /// Replacing rather than stacking is the point: the player can press the button repeatedly, and a
        /// column of identical cards would be the loudest possible way to say something small.
        /// </summary>
        public void Show()
        {
            if (_root == null)
            {
                return;
            }

            _root.gameObject.SetActive(true);

            // Re-read every time, because the strings can be reloaded from the server while the game runs.
            Assign(_message, "water.no_drops");
            Assign(_hint, "water.no_drops_hint");

            // Toggled off first so the pop plays again when it is already up: that little movement is what
            // tells the player this is a new remark rather than the old one still sitting there.
            if (_card.gameObject.activeSelf)
            {
                _card.gameObject.SetActive(false);
            }

            _card.gameObject.SetActive(true);

            if (_timer != null)
            {
                _timer.Kill();
            }

            // Unscaled, like every other interface tween here: the card can be up while the game is paused.
            _timer = DOVirtual.DelayedCall(StaySeconds, Hide).SetUpdate(true);
        }

        /// <summary>Leaves early. Safe to call when it is already gone, and safe to call twice.</summary>
        public void Hide()
        {
            if (_timer != null)
            {
                _timer.Kill();
                _timer = null;
            }

            if (_card == null || !_card.gameObject.activeSelf)
            {
                return;
            }

            _juice.Leave();
        }

        /// <summary>
        /// Sets one line from the strings store.
        ///
        /// The source string is assigned, and never read back out of a label: RTLTMPro's text getter returns
        /// the shaped result, and assigning that again shapes it a second time and produces glyphs that no
        /// longer read as words. A missing key hides its line rather than printing the key at the player.
        /// </summary>
        private void Assign(RTLTextMeshPro label, string key)
        {
            if (label == null)
            {
                return;
            }

            string text = Text(key);
            bool has = !string.IsNullOrEmpty(text);
            label.gameObject.SetActive(has);

            if (has)
            {
                label.text = text;
            }
        }

        /// <summary>
        /// Applies the data-driven character spacing to one label.
        ///
        /// Null-safe at both steps on purpose: this card is also built by an editor tool that runs before any
        /// data has been read, and it still has to come out the right shape. The fallback is the value the
        /// settings class carries, which is the one the data file ships with.
        /// </summary>
        private static void SetSpacing(RTLTextMeshPro label)
        {
            if (label == null)
            {
                return;
            }

            DevSettings settings = DevSettings.Current;
            DevSettings.UiSettings ui = settings != null ? settings.Ui : null;
            label.characterSpacing = ui != null
                ? ui.CharacterSpacing
                : DevSettings.UiSettings.Default.CharacterSpacing;
        }

        /// <summary>The source text for a key, or empty when the store does not have it.</summary>
        private string Text(string key)
        {
            return _strings != null && _strings.Has(key) ? _strings.Get(key) : string.Empty;
        }
    }
}
