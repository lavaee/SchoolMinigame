using System;
using System.Collections;
using RTLTMPro;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmallPot
{
    /// <summary>
    /// The always-visible layer: drop counter, the action buttons, the pot's caption and the morning
    /// report card.
    ///
    /// Every string is fetched by key, and every optional string is probed with <c>Has</c> before it is
    /// used, so replacing the data files with a set that lacks this project's extra keys degrades to a
    /// smaller HUD instead of showing placeholder markers.
    /// </summary>
    public sealed class HudView
    {
        private readonly MonoBehaviour _host;
        private readonly StringsStore _strings;
        private readonly MotionPrefs _motion;
        private readonly Func<GameConfig> _config;

        private RTLTextMeshPro _dropNumber;
        private RTLTextMeshPro _dropLabel;
        private RTLTextMeshPro _stateCaption;
        private RTLTextMeshPro _growthLine;
        private RTLTextMeshPro _waterLabel;
        private RTLTextMeshPro _waterCost;
        private LabeledButton _waterButton;
        private LabeledButton _playButton;
        private LabeledButton _morningButton;
        private LabeledButton _settingsButton;
        private Image _statusChip;
        private RTLTextMeshPro _statusChipText;

        /// <summary>
        /// What the chip was asked to say, before RTLTMPro shaped it.
        ///
        /// The label's own text cannot be used as the "previous" value: it has already been through the
        /// shaper, and assigning it back shapes it a second time, which turns a Persian sentence into
        /// isolated presentation forms.
        /// </summary>
        private string _chipText = string.Empty;

        private RectTransform _reportCard;
        private CanvasGroup _reportGroup;
        private RTLTextMeshPro _reportTitle;
        private RTLTextMeshPro _reportBody;
        private LabeledButton _reportContinue;

        private int _shownDrops = -1;
        private Coroutine _countUp;
        private Coroutine _chipFlash;

        public event Action WaterClicked;
        public event Action PlayClicked;
        public event Action MorningClicked;
        public event Action SettingsClicked;

        public HudView(MonoBehaviour host, StringsStore strings, MotionPrefs motion, Func<GameConfig> config)
        {
            _host = host;
            _strings = strings;
            _motion = motion;
            _config = config;
        }

        public RectTransform Root { get; private set; }

        public void Build(Transform parent)
        {
            Root = UiKit.Stretch(UiKit.Node("HUD", parent));

            BuildDropCounter();
            BuildTopButtons();
            BuildCaptions();
            BuildActions();
            BuildReportCard();
        }

        private void BuildDropCounter()
        {
            Image pill = UiKit.Panel("Drop Counter", Root, Palette.Panel, 30);
            UiKit.Place(pill.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(268f, 120f));
            UiKit.AddShadow(pill.rectTransform);

            Image icon = UiKit.Icon("Droplet", pill.transform, TextureFactory.Droplet(), Palette.Water);
            UiKit.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(62f, 62f));

            RectTransform numberRect = UiKit.Stretch(UiKit.Node("Number", pill.transform), 108f, 46f, 24f, 12f);
            _dropNumber = UiKit.Text(numberRect, string.Empty, 54f, Palette.Ink, TextAlignmentOptions.Left, true, FontStyles.Bold);

            RectTransform labelRect = UiKit.Stretch(UiKit.Node("Label", pill.transform), 108f, 12f, 24f, 74f);
            _dropLabel = UiKit.Text(labelRect, _strings.Get("hud.drops"), 24f, Palette.InkSoft, TextAlignmentOptions.Left);
        }

        private void BuildTopButtons()
        {
            _settingsButton = UiKit.Button(
                "Settings", Root, string.Empty, 24f, Palette.Panel, Palette.Ink,
                () => SettingsClicked?.Invoke(), TextureFactory.Gear());
            UiKit.Place(_settingsButton.Rect, new Vector2(0f, 1f), new Vector2(26f, -26f), new Vector2(96f, 96f));
            _settingsButton.Label.gameObject.SetActive(false);

            _statusChip = UiKit.Panel("Data Source Chip", Root, Palette.AccentSoft, 24);
            UiKit.Place(_statusChip.rectTransform, new Vector2(1f, 1f), new Vector2(-26f, -34f), new Vector2(200f, 72f));
            RectTransform chipText = UiKit.Stretch(UiKit.Node("Label", _statusChip.transform), 14f, 6f, 14f, 6f);
            _statusChipText = UiKit.Text(chipText, string.Empty, 22f, Palette.Accent, TextAlignmentOptions.Center);
            _statusChip.gameObject.SetActive(false);
        }

        private void BuildCaptions()
        {
            _stateCaption = UiKit.Text("State Caption", Root, string.Empty, 34f, Palette.Ink, UiKit.Centred, true, FontStyles.Bold);
            UiKit.Band(_stateCaption.rectTransform, true, 168f, 48f);

            _growthLine = UiKit.Text("Growth Line", Root, string.Empty, 24f, Palette.InkSoft);
            UiKit.Band(_growthLine.rectTransform, true, 216f, 34f);
        }

        private void BuildActions()
        {
            // Primary action: water. The cost sits inside the button so the price is never a surprise.
            _waterButton = UiKit.Button(
                "Water Button", Root, string.Empty, 36f, Palette.Accent, Color.white,
                () => WaterClicked?.Invoke());
            UiKit.Band(_waterButton.Rect, false, 30f, 112f);
            UiKit.AddShadow(_waterButton.Rect);

            _waterLabel = _waterButton.Label;
            _waterLabel.alignment = TextAlignmentOptions.Center;
            UiKit.Stretch(_waterLabel.rectTransform, 16f, 48f, 16f, 10f);

            _waterCost = UiKit.Text(
                UiKit.Stretch(UiKit.Node("Cost", _waterButton.Rect), 16f, 8f, 16f, 66f),
                string.Empty, 24f, new Color(1f, 1f, 1f, 0.85f));
            _waterCost.raycastTarget = false;

            _playButton = UiKit.Button(
                "Play Button", Root, _strings.Get("action.play"), 34f, Palette.Panel, Palette.Ink,
                () => PlayClicked?.Invoke());
            UiKit.Band(_playButton.Rect, false, 156f, 100f);
            UiKit.AddShadow(_playButton.Rect);

            // The debug control from the brief. Deliberately small and off to the side: it is a tool for
            // demonstrating the morning step, not a step a player should be pushed towards.
            _morningButton = UiKit.Button(
                "Next Morning", Root, _strings.Get("action.next_morning"), 24f, Palette.PanelEdge, Palette.Ink,
                () => MorningClicked?.Invoke());
            UiKit.Place(_morningButton.Rect, new Vector2(1f, 0f), new Vector2(-30f, 274f), new Vector2(300f, 68f));
        }

        private void BuildReportCard()
        {
            Image card = UiKit.Panel("Report Card", Root, Palette.Panel, 32);
            _reportCard = card.rectTransform;
            _reportCard.anchorMin = new Vector2(0f, 0f);
            _reportCard.anchorMax = new Vector2(1f, 0f);
            _reportCard.pivot = new Vector2(0.5f, 0f);
            _reportCard.sizeDelta = new Vector2(0f, 360f);
            _reportCard.anchoredPosition = Vector2.zero;
            _reportGroup = UiKit.Group(card.gameObject);
            UiKit.AddShadow(_reportCard);

            RectTransform titleRect = UiKit.Stretch(UiKit.Node("Title", card.transform), 40f, 268f, 40f, 28f);
            _reportTitle = UiKit.Text(titleRect, string.Empty, 38f, Palette.Ink, UiKit.Centred, true, FontStyles.Bold);

            RectTransform bodyRect = UiKit.Stretch(UiKit.Node("Body", card.transform), 40f, 128f, 40f, 90f);
            _reportBody = UiKit.Text(bodyRect, string.Empty, 30f, Palette.Ink, UiKit.Paragraph);

            _reportContinue = UiKit.Button(
                "Continue", card.transform, _strings.Get("common.continue"), 30f, Palette.Accent, Color.white,
                HideReport);
            UiKit.Place(_reportContinue.Rect, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(260f, 86f));

            card.gameObject.SetActive(false);
        }

        // ---- State ------------------------------------------------------------------------------

        public void SetDrops(int previous, int current, bool animate)
        {
            int from = previous < 0 ? current : previous;
            if (_countUp != null)
            {
                _host.StopCoroutine(_countUp);
                _countUp = null;
            }

            float duration = animate ? _motion.Duration(Configuration.NumberCountUpSeconds) : 0f;
            if (duration <= 0f)
            {
                _shownDrops = current;
                _dropNumber.text = _strings.DisplayNumber(current);
                return;
            }

            _countUp = _host.StartCoroutine(CountUp(from, current, duration));
        }

        private IEnumerator CountUp(int from, int to, float duration)
        {
            yield return Tween.Value(from, to, duration, value =>
            {
                int shown = Mathf.RoundToInt(value);
                if (shown != _shownDrops)
                {
                    _shownDrops = shown;
                    _dropNumber.text = _strings.DisplayNumber(shown);
                }
            });

            _shownDrops = to;
            _dropNumber.text = _strings.DisplayNumber(to);
            _countUp = null;
        }

        /// <summary>Refreshes the button states for the current plant state.</summary>
        public void RefreshActions(PlantState state, bool allowWateringAtFullBloom)
        {
            GameConfig config = Configuration;
            WaterBlock block = state.CanWater(config, allowWateringAtFullBloom);

            if (state.Watered)
            {
                // Already watered: not merely disabled, but visibly a different statement.
                _waterLabel.text = _strings.Get("pot.watered");
                _waterLabel.fontSize = 28f;
                _waterButton.Background.color = Palette.AccentSoft;
                _waterLabel.color = Palette.Accent;
                _waterCost.text = string.Empty;
                _waterButton.SetInteractable(false);
                return;
            }

            _waterLabel.text = _strings.Get("action.water");
            _waterLabel.fontSize = 36f;
            _waterButton.Background.color = block == WaterBlock.Allowed ? Palette.Accent : Palette.Disabled;
            _waterLabel.color = Color.white;
            _waterButton.SetInteractable(block == WaterBlock.Allowed);

            if (block == WaterBlock.NotEnoughDrops && _strings.Has("water.not_enough"))
            {
                _waterCost.text = _strings.Display(_strings.Get("water.not_enough"));
                _waterCost.color = new Color(1f, 1f, 1f, 0.95f);
            }
            else if (block == WaterBlock.AtFullBloom && _strings.Has("water.not_needed"))
            {
                _waterCost.text = _strings.Display(_strings.Get("water.not_needed"));
                _waterCost.color = new Color(1f, 1f, 1f, 0.85f);
            }
            else
            {
                _waterCost.text = _strings.Display(_strings.FormatNumber("water.cost", config.Watering.CostDrops));
                _waterCost.color = new Color(1f, 1f, 1f, 0.85f);
            }
        }

        public void RefreshPot(PlantState state, PotVisual visual)
        {
            GameConfig config = Configuration;
            _stateCaption.text = _strings.Display(_strings.StateLabel(state.DisplayStateId(config)));

            if (_strings.Has("pot.growth_progress"))
            {
                int required = state.RequiredMorningsForNext(config);
                if (required > 1 && !state.AtFullBloom(config))
                {
                    _growthLine.text = _strings.Display(_strings.Format(
                        "pot.growth_progress",
                        new System.Collections.Generic.Dictionary<string, string>(2, StringComparer.Ordinal)
                        {
                            { "done", _strings.DisplayNumber(state.ProgressMornings) },
                            { "total", _strings.DisplayNumber(required) }
                        }));
                }
                else
                {
                    _growthLine.text = string.Empty;
                }
            }
            else
            {
                _growthLine.gameObject.SetActive(false);
            }
        }

        public void SetStatusChip(string text)
        {
            if (_statusChip == null)
            {
                return;
            }

            bool show = !string.IsNullOrEmpty(text);
            _chipText = show ? text : string.Empty;
            _statusChip.gameObject.SetActive(show);
            if (show)
            {
                _statusChipText.text = _strings.Display(text);
            }
        }

        /// <summary>
        /// Shows a short note in the status chip and hands the chip back to its normal content.
        ///
        /// Used for the "content was updated" moment: worth telling the player about, not worth stopping
        /// the game for, and never a dialog they have to dismiss.
        /// </summary>
        public void FlashStatusChip(string text, float seconds)
        {
            if (_statusChip == null)
            {
                return;
            }

            if (_chipFlash != null)
            {
                _host.StopCoroutine(_chipFlash);
            }

            _chipFlash = _host.StartCoroutine(FlashRoutine(text, seconds));
        }

        private IEnumerator FlashRoutine(string text, float seconds)
        {
            // The chip's own source string, never the label's rendered text: handing RTLTMPro a string it
            // has already shaped is what produced glyph soup when the flash handed the chip back.
            string restored = _chipText;

            SetStatusChip(text);
            yield return Tween.WaitUnscaled(Mathf.Max(0.5f, seconds));

            if (_statusChip == null)
            {
                yield break;
            }

            SetStatusChip(restored);
            _chipFlash = null;
        }

        public void ShowReport(string title, string body)
        {
            _reportTitle.text = _strings.Display(title);
            _reportBody.text = _strings.Display(body);
            _reportCard.gameObject.SetActive(true);
            _host.StartCoroutine(Tween.FadeCanvasGroup(_reportGroup, 0f, 1f, _motion.FadeDuration(0.18f)));
        }

        public void HideReport()
        {
            if (_reportCard != null)
            {
                _reportCard.gameObject.SetActive(false);
            }
        }

        public bool ReportVisible => _reportCard != null && _reportCard.gameObject.activeSelf;

        private GameConfig Configuration => _config() ?? GameConfig.Defaults();
    }
}
