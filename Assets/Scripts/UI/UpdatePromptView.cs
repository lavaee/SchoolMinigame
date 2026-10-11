using System;
using DG.Tweening;
using RTLTMPro;
using TMPro;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// The card that says a newer revision is waiting on the server, and waits for the player.
    ///
    /// Content used to be applied the moment the version watch noticed it. That is convenient when the
    /// person pushing is the person playing, and wrong for everyone else: a round disappears mid-answer and
    /// the only trace is a chip that flashes for three seconds. The update is offered now, with what arrived
    /// and a button that takes it, and the game stands still until it is taken.
    ///
    /// Built like every other panel - find first, create second - so it can be baked into the scene later
    /// and can exist at runtime today without a re-bake.
    /// </summary>
    public sealed class UpdatePromptView
    {
        /// <summary>Anchor for children laid out downwards from the top edge of the panel.</summary>
        private static readonly Vector2 Top = new Vector2(0.5f, 1f);

        /// <summary>Anchor for the panel itself.</summary>
        private static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// How long the headline waits before it starts arriving: long enough for the card's own pop to
        /// have settled, so the reveal reads as a second beat rather than as part of the same blur.
        /// </summary>
        private const float RevealDelaySeconds = 0.25f;

        private readonly StringsStore _strings;
        private readonly Action _onApply;

        private RectTransform _root;
        private PanelJuice _rootJuice;
        private RTLTextMeshPro _title;
        private RTLTextMeshPro _body;
        private RTLTextMeshPro _detail;

        public UpdatePromptView(StringsStore strings, Action onApply)
        {
            _strings = strings;
            _onApply = onApply;
        }

        public bool Visible => _root != null && _root.gameObject.activeSelf;

        /// <summary>Builds the card under the canvas, or refreshes the one already there.</summary>
        public void Build(Transform canvas)
        {
            _root = UiKit.Node("Update Prompt", canvas);

            // The root covers the canvas because the scrim below stretches to its parent: a modal whose root
            // is a default-sized rect would let every tap outside a square through to the game behind it.
            UiKit.Stretch(_root);

            // Clicks stop here, so the game cannot be played while the card is asking its question.
            UiKit.Scrim("Scrim", _root, null);

            var panel = UiKit.Panel("Panel", _root, Palette.Panel, 32);
            UiKit.Place(panel.rectTransform, Middle, Vector2.zero, new Vector2(620f, 470f));
            UiKit.AddShadow(panel.rectTransform, 12f, 0.22f);

            _title = UiKit.Text(
                "Title", panel.transform, Words("update.title", panel.transform, "Title"), 36f, Palette.Ink,
                UiKit.Centred, true, FontStyles.Bold);
            UiKit.Place(_title.rectTransform, Top, new Vector2(0f, -46f), new Vector2(540f, 52f));

            _body = UiKit.Text(
                "Body", panel.transform, Words("update.body", panel.transform, "Body"), 24f, Palette.InkSoft,
                UiKit.Centred);
            UiKit.Place(_body.rectTransform, Top, new Vector2(0f, -146f), new Vector2(530f, 110f));

            _detail = UiKit.Text(
                "Detail", panel.transform, string.Empty, 22f, Palette.InkSoft, UiKit.Centred);
            UiKit.Place(_detail.rectTransform, Top, new Vector2(0f, -268f), new Vector2(530f, 46f));

            LabeledButton apply = UiKit.Button(
                "Apply",
                panel.transform,
                Words("update.apply", panel.transform, "Apply"),
                30f,
                Palette.Accent,
                Palette.Panel,
                () => _onApply?.Invoke());
            UiKit.Place(apply.Rect, Top, new Vector2(0f, -348f), new Vector2(360f, 86f));

            Hide();

            // Added after Hide, so both components wake on an inactive object: the card must not play an
            // entrance during startup, when nothing is waiting. The root carries the scrim and so fades
            // rather than being resized; the card on top of it pops.
            _rootJuice = UiKit.GetOrAdd<PanelJuice>(_root.gameObject);
            _rootJuice.Scale = false;
            UiKit.GetOrAdd<PanelJuice>(panel.gameObject);
        }

        /// <summary>Shows the card and names the revision that is waiting.</summary>
        public void Show(int revision)
        {
            if (_root == null)
            {
                return;
            }

            if (_detail != null)
            {
                // "{n}" rather than a number built here, so the sentence and the digit style both come from
                // the data files.
                _detail.text = _strings != null && _strings.Has("update.detail")
                    ? _strings.FormatNumber("update.detail", revision)
                    : string.Empty;
            }

            // The card can be offered again after a later push, so the entrance belongs to the showing that
            // actually happened rather than to every call.
            bool wasVisible = Visible;

            _root.gameObject.SetActive(true);

            // Last child draws on top: the card belongs above the settings panel and above the HUD.
            _root.SetAsLastSibling();

            if (!wasVisible)
            {
                RevealAfterEntrance(_title);
            }
        }

        public void Hide()
        {
            if (_root == null)
            {
                return;
            }

            // Faded rather than switched off in the same frame: the card was animated in, and the fade also
            // holds the scrim over the paused game until the card has actually gone. The null branch is the
            // call from Build, where there is no juice yet and an immediate hide is the correct answer.
            if (_rootJuice != null)
            {
                _rootJuice.Leave();
            }
            else
            {
                _root.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Starts the headline's reveal once the card has finished arriving.
        ///
        /// Unscaled, like everything else here: the card is shown while the game is paused, so a reveal on
        /// scaled time would sit at zero characters until the player answered it.
        /// </summary>
        private static void RevealAfterEntrance(TMP_Text label)
        {
            DOTween.Sequence()
                .SetUpdate(true)
                .AppendInterval(RevealDelaySeconds)
                .OnComplete(() => Juice.RevealText(label));
        }

        /// <summary>
        /// The words for a label: the string when the data has one, and otherwise whatever the scene already
        /// has there.
        ///
        /// The same rule the connection gate follows, and for the same reason: a data set that predates this
        /// panel should cost the player nothing, and a placeholder would cost them something.
        /// </summary>
        private string Words(string key, Transform parent, string widgetName)
        {
            string existing = null;
            Transform widget = parent != null ? parent.Find(widgetName) : null;
            if (widget != null)
            {
                // In children, because a button keeps its label in a child rather than on itself.
                // OriginalText, not text: text is the shaped result for RTL input, and re-assigning it
                // elsewhere would shape it a second time and produce glyphs that no longer read as words.
                RTLTextMeshPro label = widget.GetComponentInChildren<RTLTextMeshPro>(true);
                existing = label != null ? label.OriginalText : null;
            }

            if (_strings != null && _strings.Has(key))
            {
                return _strings.Get(key);
            }

            return string.IsNullOrEmpty(existing) ? (_strings != null ? _strings.Get(key) : key) : existing;
        }
    }
}
