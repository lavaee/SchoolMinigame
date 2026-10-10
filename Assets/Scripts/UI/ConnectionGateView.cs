using System;
using System.Collections.Generic;
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

        private readonly StringsStore _strings;
        private readonly Action<string> _onRetry;

        private RectTransform _root;
        private RTLTextMeshPro _tried;
        private RTLTextMeshPro _status;
        private TMP_InputField _address;

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

            RTLTextMeshPro title = UiKit.Text(
                "Title", panel.transform, _strings.Get("gate.title"), 40f, Palette.Ink, UiKit.Centred, true,
                FontStyles.Bold);
            UiKit.Place(title.rectTransform, Top, new Vector2(0f, -54f), new Vector2(580f, 56f));

            RTLTextMeshPro body = UiKit.Text(
                "Body", panel.transform, _strings.Get("gate.body"), 24f, Palette.InkSoft, UiKit.Centred);
            UiKit.Place(body.rectTransform, Top, new Vector2(0f, -158f), new Vector2(560f, 96f));

            _tried = UiKit.Text(
                "Tried", panel.transform, string.Empty, 22f, Palette.InkSoft, UiKit.Centred, false);
            UiKit.Place(_tried.rectTransform, Top, new Vector2(0f, -252f), new Vector2(560f, 76f));

            _address = UiKit.Input(
                "Address",
                panel.transform,
                _strings.Get("settings.server_url_hint"),
                24f,
                Palette.PanelEdge,
                Palette.Ink,
                Palette.InkSoft);
            UiKit.Place(_address.GetComponent<RectTransform>(), Top, new Vector2(0f, -358f), new Vector2(560f, 78f));

            LabeledButton retry = UiKit.Button(
                "Retry",
                panel.transform,
                _strings.Get("gate.retry"),
                30f,
                Palette.Accent,
                Palette.Panel,
                () => _onRetry?.Invoke(Address));
            UiKit.Place(retry.Rect, Top, new Vector2(0f, -462f), new Vector2(360f, 84f));

            _status = UiKit.Text(
                "Status", panel.transform, string.Empty, 22f, Palette.Ink, UiKit.Centred);
            UiKit.Place(_status.rectTransform, Top, new Vector2(0f, -546f), new Vector2(560f, 52f));

            RTLTextMeshPro hint = UiKit.Text(
                "Hint", panel.transform, _strings.Get("gate.hint"), 20f, Palette.InkSoft, UiKit.Centred);
            UiKit.Place(hint.rectTransform, Top, new Vector2(0f, -602f), new Vector2(580f, 96f));

            Hide();
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

            _root.gameObject.SetActive(true);

            // Last child draws on top, so the gate covers the HUD no matter what order things were built.
            _root.SetAsLastSibling();
        }

        public void Hide()
        {
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }
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

        public void SetStatus(string message)
        {
            if (_status != null)
            {
                _status.text = message ?? string.Empty;
            }
        }
    }
}
