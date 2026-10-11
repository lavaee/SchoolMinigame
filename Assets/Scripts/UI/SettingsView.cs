using System;
using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using RTLTMPro;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmallPot
{
    /// <summary>
    /// Settings, data-source information and the small debug section.
    ///
    /// The visible controls here are all optional extras: the only key this panel requires is the title,
    /// the reduce-motion label and the close button, which all come from the employer's strings-base.json.
    /// Everything else is probed for and simply omitted when the data files do not provide it.
    /// </summary>
    public sealed class SettingsView
    {
        /// <summary>
        /// How long the headline waits before it starts arriving.
        ///
        /// Long enough that the panel's own pop has settled and the reveal reads as a second beat rather
        /// than as part of the same blur.
        /// </summary>
        private const float RevealDelaySeconds = 0.25f;

        private readonly StringsStore _strings;
        private readonly MotionPrefs _motion;
        private readonly Func<DataBundle> _bundle;

        private RectTransform _root;
        private PanelJuice _rootJuice;
        private RTLTextMeshPro _title;
        private Image _motionCheck;
        private RTLTextMeshPro _motionHint;
        private RTLTextMeshPro _sourceLines;
        private RTLTextMeshPro _serverLine;
        private LabeledButton _reloadButton;
        private RectTransform _debugSection;
        private RTLTextMeshPro _missingKeys;
        private RectTransform _content;

        public event Action ReloadRequested;
        public event Action ResetSaveRequested;
        public event Action Closed;

        public SettingsView(StringsStore strings, MotionPrefs motion, Func<DataBundle> bundle)
        {
            _strings = strings;
            _motion = motion;
            _bundle = bundle;
        }

        public void Build(Transform parent)
        {
            _root = UiKit.Stretch(UiKit.Node("Settings", parent));
            _root.gameObject.SetActive(false);

            UiKit.Stretch(_root);
            UiKit.Scrim("Scrim", _root, Close);

            Image panel = UiKit.Panel("Panel", _root, Palette.Panel, 36);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(660f, 980f));
            UiKit.AddShadow(panel.rectTransform, 12f, 0.25f);
            _content = panel.rectTransform;

            // Two entrances, because they are two different things. The root carries the scrim, so it
            // fades and must not be resized; the panel on top of it pops. Leave() on the root fades the
            // whole overlay out and deactivates it, which is what actually closes this screen.
            _rootJuice = UiKit.GetOrAdd<PanelJuice>(_root.gameObject);
            _rootJuice.Scale = false;
            UiKit.GetOrAdd<PanelJuice>(panel.gameObject);

            _title = UiKit.Text(
                "Title", panel.transform, _strings.Get("settings.title"), 40f, Palette.Ink, UiKit.Centred, true, FontStyles.Bold);
            UiKit.Band(_title.rectTransform, true, 24f, 52f);

            BuildMotionRow(panel.transform);
            BuildDataSourceSection(panel.transform);
            BuildDebugSection(panel.transform);

            LabeledButton close = UiKit.Button(
                "Close", panel.transform, _strings.Get("common.close"), 30f, Palette.Accent, Color.white, Close);
            UiKit.Place(close.Rect, new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(300f, 88f));
        }

        private void BuildMotionRow(Transform parent)
        {
            // The toggle is a labelled checkbox: the check mark is the state, and colour only reinforces it.
            var row = UiKit.Band(UiKit.Node("Reduce Motion Row", parent), true, 88f, 84f, 40f);

            Image box = UiKit.Panel("Box", row, Palette.PanelEdge, 16);
            UiKit.Place(box.rectTransform, new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(72f, 72f));

            Image check = UiKit.Icon("Check", box.transform, TextureFactory.Check(), Palette.Accent);
            UiKit.Stretch(check.rectTransform, 12f, 12f, 12f, 12f);
            _motionCheck = check;

            var toggle = UiKit.GetOrAdd<Button>(box.gameObject);
            toggle.targetGraphic = box;
            // Cleared first: the settings panel is rebuilt whenever the data changes, and a stacked
            // handler would flip the setting twice per tap.
            toggle.onClick.RemoveAllListeners();
            toggle.onClick.AddListener(() =>
            {
                AudioDirector.Play(Sound.Click);
                _motion.ReduceMotion = !_motion.ReduceMotion;
                RefreshMotionState();
            });

            RTLTextMeshPro label = UiKit.Text(
                "Label", row, _strings.Get("settings.reduce_motion"), 30f, Palette.Ink, UiKit.Paragraph, true);
            UiKit.Stretch(label.rectTransform, 20f, 0f, 96f, 0f);
            label.alignment = TextAlignmentOptions.Right;

            if (_strings.Has("settings.reduce_motion_hint"))
            {
                _motionHint = UiKit.Text("Hint", parent, _strings.Get("settings.reduce_motion_hint"), 22f, Palette.InkSoft);
                UiKit.Band(_motionHint.rectTransform, true, 178f, 60f, 48f);
            }

            RefreshMotionState();
        }

        private void BuildDataSourceSection(Transform parent)
        {
            if (_strings.Has("settings.data_source_title"))
            {
                RTLTextMeshPro heading = UiKit.Text(
                    "Data Title", parent, _strings.Get("settings.data_source_title"), 26f, Palette.Ink, UiKit.Centred, true, FontStyles.Bold);
                UiKit.Band(heading.rectTransform, true, 252f, 40f);
            }

            _sourceLines = UiKit.Text("Sources", parent, string.Empty, 21f, Palette.InkSoft, UiKit.Centred, false);
            UiKit.Band(_sourceLines.rectTransform, true, 296f, 150f, 48f);

            _serverLine = UiKit.Text("Server", parent, string.Empty, 20f, Palette.InkSoft, UiKit.Centred, false);
            UiKit.Band(_serverLine.rectTransform, true, 452f, 52f, 48f);

            if (_strings.Has("settings.reload"))
            {
                _reloadButton = UiKit.Button(
                    "Reload", parent, _strings.Get("settings.reload"), 26f, Palette.AccentSoft, Palette.Accent,
                    () => ReloadRequested?.Invoke(), TextureFactory.Refresh());
                UiKit.Place(_reloadButton.Rect, new Vector2(0.5f, 1f), new Vector2(0f, -514f), new Vector2(360f, 76f));
            }
        }

        private void BuildDebugSection(Transform parent)
        {
            _debugSection = UiKit.Band(UiKit.Node("Debug", parent), true, 606f, 290f, 40f);

            if (_strings.Has("debug.title"))
            {
                RTLTextMeshPro heading = UiKit.Text(
                    "Debug Title", _debugSection, _strings.Get("debug.title"), 24f, Palette.InkSoft, UiKit.Centred);
                UiKit.Band(heading.rectTransform, true, 0f, 34f);
            }

            if (_strings.Has("debug.reset_save"))
            {
                LabeledButton reset = UiKit.Button(
                    "Reset Save", _debugSection, _strings.Get("debug.reset_save"), 24f, Palette.PanelEdge, Palette.Ink,
                    () => ResetSaveRequested?.Invoke());
                UiKit.Band(reset.Rect, true, 40f, 70f, 0f);
            }

            _missingKeys = UiKit.Text("Missing Keys", _debugSection, string.Empty, 20f, Palette.InkSoft, UiKit.Centred, false);
            UiKit.Band(_missingKeys.rectTransform, true, 120f, 150f, 0f);
        }

        private void RefreshMotionState()
        {
            if (_motionCheck != null)
            {
                _motionCheck.enabled = _motion.ReduceMotion;
            }

            if (_motionHint != null)
            {
                _motionHint.color = _motion.ReduceMotion ? Palette.Accent : Palette.InkSoft;
            }
        }

        public void Show()
        {
            // Read before the switch: Show is called again to refresh an open panel, and a headline that
            // re-reveals every time is a stutter rather than an entrance.
            bool wasOpen = IsOpen;

            _root.gameObject.SetActive(true);
            RefreshMotionState();
            RefreshDataSource();

            if (!wasOpen)
            {
                RevealAfterEntrance(_title);
            }
        }

        public void Close()
        {
            if (_root != null)
            {
                // Faded out rather than switched off in the same frame it was switched on: the panel was
                // animated in, and cutting it off mid-flight is what makes an entrance look like a bug.
                if (_rootJuice != null)
                {
                    _rootJuice.Leave();
                }
                else
                {
                    _root.gameObject.SetActive(false);
                }
            }

            Closed?.Invoke();
        }

        /// <summary>
        /// Starts the headline's reveal once the panel has finished arriving.
        ///
        /// Unscaled, like every other entrance here: the settings panel is routinely opened over a game
        /// that is paused, and a reveal that waits for a time scale that never leaves zero never happens.
        /// </summary>
        private static void RevealAfterEntrance(TMP_Text label)
        {
            DOTween.Sequence()
                .SetUpdate(true)
                .AppendInterval(RevealDelaySeconds)
                .OnComplete(() => Juice.RevealText(label));
        }

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        /// <summary>Shows which layer each data file came from, and the address used for the remote one.</summary>
        public void RefreshDataSource()
        {
            DataBundle bundle = _bundle();
            if (bundle == null)
            {
                return;
            }

            var builder = new StringBuilder();
            foreach (string fileName in DataLoader.AllFiles)
            {
                DataSource source = bundle.SourceOf(fileName);
                if (source == DataSource.None && !_strings.Has("settings.data_source_line"))
                {
                    continue;
                }

                string sourceLabel = _strings.Has("settings.data_source." + SourceKey(source))
                    ? _strings.Get("settings.data_source." + SourceKey(source))
                    : SourceKey(source);

                builder.Append(_strings.Format(
                    "settings.data_source_line",
                    new Dictionary<string, string>(2, StringComparer.Ordinal)
                    {
                        { "file", fileName },
                        { "source", sourceLabel }
                    }));
                builder.Append('\n');
            }

            _sourceLines.text = builder.ToString().TrimEnd('\n');

            // Every address the game will try, in the order it tries them: when a phone cannot reach the
            // server, this line is the first thing worth reading.
            List<string> addresses = DataLoader.CandidateUrls(string.Empty, bundle.Dev);
            bool hasUrl = addresses.Count > 0;

            if (_serverLine != null)
            {
                _serverLine.gameObject.SetActive(hasUrl);
                _serverLine.text = hasUrl ? string.Join("\n", addresses) : string.Empty;
            }

            if (_reloadButton != null)
            {
                _reloadButton.Root.SetActive(hasUrl);
            }

            RefreshMissingKeys();
        }

        private void RefreshMissingKeys()
        {
            if (_missingKeys == null)
            {
                return;
            }

            IReadOnlyList<string> missing = _strings.MissingKeys;
            if (missing == null || missing.Count == 0)
            {
                _missingKeys.text = _strings.Has("debug.missing_keys_none")
                    ? _strings.Display(_strings.Get("debug.missing_keys_none"))
                    : string.Empty;
                return;
            }

            var builder = new StringBuilder();
            if (_strings.Has("debug.missing_keys"))
            {
                builder.Append(_strings.Get("debug.missing_keys")).Append(' ');
            }

            for (int i = 0; i < missing.Count && i < 8; i++)
            {
                builder.Append(missing[i]).Append(i < missing.Count - 1 ? ", " : string.Empty);
            }

            _missingKeys.text = _strings.Display(builder.ToString());
        }

        private static string SourceKey(DataSource source)
        {
            switch (source)
            {
                case DataSource.Remote: return "remote";
                case DataSource.Cache: return "cache";
                case DataSource.Bundled: return "bundled";
                default: return "missing";
            }
        }
    }
}
