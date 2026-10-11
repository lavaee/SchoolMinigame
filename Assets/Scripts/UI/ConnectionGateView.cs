using System;
using System.Collections.Generic;
using DG.Tweening;
using RTLTMPro;
using TMPro;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// The screen that stands between the player and the game when the content server cannot be reached.
    ///
    /// A missing server is usually a shrug - the bundled files are enough to play - but a build can insist
    /// on one, which is what makes "the app picks up what I pushed" a promise instead of a hope. This is
    /// that insistence: it says what went wrong, shows the addresses that were tried, and lets the address
    /// be corrected on the device.
    ///
    /// It is built the same way every other panel is, so it can be baked into the scene and refreshed in
    /// place: nothing here replaces art it finds already assigned.
    /// </summary>
    public sealed class ConnectionGateView
    {
        /// <summary>Anchor for children laid out downwards from the top edge of the panel.</summary>
        private static readonly Vector2 Top = new Vector2(0.5f, 1f);

        /// <summary>Anchor for the panel itself.</summary>
        private static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// How long the headline waits before it starts arriving: long enough for the panel's own pop to
        /// have settled, so the reveal reads as a second beat rather than as part of the same blur.
        /// </summary>
        private const float RevealDelaySeconds = 0.25f;

        private readonly StringsStore _strings;
        private readonly Action<string> _onRetry;

        private RectTransform _root;
        private PanelJuice _rootJuice;
        private RTLTextMeshPro _title;
        private RTLTextMeshPro _body;
        private RTLTextMeshPro _tried;
        private RTLTextMeshPro _status;
        private TMP_InputField _address;

        /// <summary>
        /// Whether the gate has actually been put in front of the player.
        ///
        /// The two sounds on this screen belong to the transition, not to the state: the gate is hidden at
        /// build time before anything has gone wrong, and it is closed after the player has got past it.
        /// Without this flag the first of those would sound like a recovery, and every retry would sound
        /// like a fresh failure.
        /// </summary>
        private bool _shown;

        public ConnectionGateView(StringsStore strings, Action<string> onRetry)
        {
            _strings = strings;
            _onRetry = onRetry;
        }

        public bool Visible => _root != null && _root.gameObject.activeSelf;

        /// <summary>Builds the panel under the canvas, or refreshes the one already there.</summary>
        public void Build(Transform canvas)
        {
            _root = UiKit.Node("Connection Gate", canvas);

            // The root has to cover the canvas, because the scrim below stretches to its parent. A modal
            // whose root is a default-sized rect blocks clicks over a square in the middle of the screen
            // and lets every other tap through to the game behind it.
            UiKit.Stretch(_root);

            // Clicks stop here: with no handler the scrim swallows them, which is what keeps the game
            // behind it from being played while there is no server.
            UiKit.Scrim("Scrim", _root, null);

            var panel = UiKit.Panel("Panel", _root, Palette.Panel, 36);
            UiKit.Place(panel.rectTransform, Middle, new Vector2(0f, 0f), new Vector2(660f, 700f));
            UiKit.AddShadow(panel.rectTransform, 12f, 0.22f);

            _title = UiKit.Text(
                "Title", panel.transform, Words("gate.title", panel.transform, "Title"), 40f, Palette.Ink,
                UiKit.Centred, true, FontStyles.Bold);
            UiKit.Place(_title.rectTransform, Top, new Vector2(0f, -54f), new Vector2(580f, 56f));

            _body = UiKit.Text(
                "Body", panel.transform, Words("gate.body", panel.transform, "Body"), 24f, Palette.InkSoft,
                UiKit.Centred);
            UiKit.Place(_body.rectTransform, Top, new Vector2(0f, -158f), new Vector2(560f, 96f));

            _tried = UiKit.Text(
                "Tried", panel.transform, string.Empty, 22f, Palette.InkSoft, UiKit.Centred, false);
            UiKit.Place(_tried.rectTransform, Top, new Vector2(0f, -252f), new Vector2(560f, 76f));

            _address = UiKit.Input(
                "Address",
                panel.transform,
                Words("settings.server_url_hint", panel.transform, "Address"),
                24f,
                Palette.PanelEdge,
                Palette.Ink,
                Palette.InkSoft);
            UiKit.Place(_address.GetComponent<RectTransform>(), Top, new Vector2(0f, -358f), new Vector2(560f, 78f));

            LabeledButton retry = UiKit.Button(
                "Retry",
                panel.transform,
                Words("gate.retry", panel.transform, "Retry"),
                30f,
                Palette.Accent,
                Palette.Panel,
                () => _onRetry?.Invoke(Address));
            UiKit.Place(retry.Rect, Top, new Vector2(0f, -462f), new Vector2(360f, 84f));

            _status = UiKit.Text(
                "Status", panel.transform, string.Empty, 22f, Palette.Ink, UiKit.Centred);
            UiKit.Place(_status.rectTransform, Top, new Vector2(0f, -546f), new Vector2(560f, 52f));

            RTLTextMeshPro hint = UiKit.Text(
                "Hint", panel.transform, Words("gate.hint", panel.transform, "Hint"), 20f, Palette.InkSoft,
                UiKit.Centred);
            UiKit.Place(hint.rectTransform, Top, new Vector2(0f, -602f), new Vector2(580f, 96f));

            Hide();

            // Added after Hide, so both components wake on an inactive object: the gate must not play an
            // entrance during startup, before anything has gone wrong. The root carries the scrim and so
            // fades rather than being resized; the panel on top of it pops.
            _rootJuice = UiKit.GetOrAdd<PanelJuice>(_root.gameObject);
            _rootJuice.Scale = false;
            UiKit.GetOrAdd<PanelJuice>(panel.gameObject);
        }

        /// <summary>Shows the gate and states which addresses were just tried.</summary>
        public void Show(IReadOnlyList<string> tried)
        {
            if (_root == null)
            {
                return;
            }

            if (_tried != null)
            {
                _tried.text = tried != null && tried.Count > 0 ? string.Join("\n", tried) : string.Empty;
            }

            // The gate is shown again on every retry while the server stays away, so the entrance and the
            // reveal belong to the first showing only: a headline that re-reveals every few seconds is a
            // stutter, and it would draw the eye to the one thing the player cannot fix by watching it.
            bool wasVisible = Visible;

            _root.gameObject.SetActive(true);

            // Last child draws on top, so the gate covers the HUD no matter what order things were built.
            _root.SetAsLastSibling();

            if (!wasVisible)
            {
                // The bad news belongs to the arrival and to nothing else: shown again on every retry while
                // the server stays away, an alarm that repeated every few seconds would stop reading as a
                // warning. The bed steps back for as long as the gate is up, so the failure has the room.
                _shown = true;
                AudioDirector.Play(Sound.ConnectionLost);
                MusicDirector.Instance?.Duck(true);

                RevealAfterEntrance(_title);
            }
        }

        public void Hide()
        {
            if (_root == null)
            {
                return;
            }

            // Being closed after a real showing means the player got past the gate, which is the only piece
            // of good news this screen can carry. The call from Build is deliberately silent: it arrives
            // before anything was ever shown, and the game should not sound relieved about a connection that
            // was never tested.
            if (_shown)
            {
                _shown = false;
                AudioDirector.Play(Sound.ConnectionUp);
                MusicDirector.Instance?.Duck(false);
            }

            // Faded rather than switched off in the same frame: the gate was animated in, and the fade also
            // keeps it covering the game for the moment it takes to leave. The null branch is the call from
            // Build, where there is no juice yet and an immediate hide is the correct answer.
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
        /// Starts the headline's reveal once the panel has finished arriving.
        ///
        /// Unscaled, and this is the panel where that matters most: the gate is shown while the game is
        /// paused, so a reveal on scaled time would never begin at all.
        /// </summary>
        private static void RevealAfterEntrance(TMP_Text label)
        {
            DOTween.Sequence()
                .SetUpdate(true)
                .AppendInterval(RevealDelaySeconds)
                .OnComplete(() => Juice.RevealText(label));
        }

        /// <summary>The address in the field, or empty when the player has not touched it.</summary>
        public string Address => _address != null ? (_address.text ?? string.Empty).Trim() : string.Empty;

        /// <summary>Puts a starting address in the field without overwriting a typed one.</summary>
        public void SuggestAddress(string address)
        {
            if (_address != null && string.IsNullOrEmpty(_address.text))
            {
                _address.text = address ?? string.Empty;
            }
        }

        /// <summary>
        /// Replaces the headline and the explanation.
        ///
        /// The gate is not always the first thing the player saw: a server can answer for a while and then
        /// stop, and "the connection dropped" is a different sentence from "it was never there".
        /// </summary>
        public void SetHeadline(string titleKey, string bodyKey)
        {
            Set(_title, titleKey);
            Set(_body, bodyKey);

            void Set(RTLTextMeshPro label, string key)
            {
                // No words for this in the data: keep what is already on screen rather than putting a
                // placeholder in front of the player.
                if (label != null && _strings != null && _strings.Has(key))
                {
                    label.text = _strings.Get(key);
                }
            }
        }

        /// <summary>
        /// The words for a label: the supplied string when there is one, and otherwise whatever the scene
        /// already has there.
        ///
        /// This is the one panel that may have to be shown before the data has arrived, or after a server
        /// has answered with a copy that predates the strings this build expects. In both cases the scene
        /// holds real words, and a placeholder would be a worse answer than the words already there.
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

            // Nothing in the data and nothing in the scene: the key itself is the honest answer, and it is
            // what the missing-key report lists.
            return string.IsNullOrEmpty(existing) ? (_strings != null ? _strings.Get(key) : key) : existing;
        }

        public void SetStatus(string message)
        {
            if (_status != null)
            {
                _status.text = message ?? string.Empty;
            }
        }
    }
}
