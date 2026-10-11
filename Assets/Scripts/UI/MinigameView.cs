using System;
using System.Collections.Generic;
using RTLTMPro;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmallPot
{
    /// <summary>
        /// The pairs minigame and the reward moment that closes it. Text comes from data by key.
    ///
    /// The timer belongs to the whole round set, not to a single question, so the bar the player sees is
    /// the same 60 seconds that the brief describes. Feedback is immediate and kind in both directions:
    /// a correct answer is celebrated where it happened, and a wrong one is answered with the reason
    /// plus the correct word, shown next to the option it belongs to rather than as a reprimand.
    /// </summary>
    public sealed class MinigameView
    {
        private readonly MonoBehaviour _host;
        private readonly StringsStore _strings;
        private readonly MotionPrefs _motion;
        private readonly Func<GameConfig> _config;

        private RectTransform _root;
        private RectTransform _gamePanel;
        private RectTransform _rewardPanel;
        private RTLTextMeshPro _roundLine;
        private RTLTextMeshPro _timeNumber;
        private RTLTextMeshPro _prompt;
        private Image _timeFill;
        private readonly List<LabeledButton> _options = new List<LabeledButton>();
        private RectTransform _feedbackRow;
        private Image _feedbackIcon;
        private RTLTextMeshPro _feedbackText;

        private Image _rewardIcon;
        private RTLTextMeshPro _rewardNumber;
        private RTLTextMeshPro _rewardText;
        private RTLTextMeshPro _rewardTitle;

        private Coroutine _rewardCountUp;

        public event Action<int> OptionChosen;
        public event Action Dismissed;

        public MinigameView(MonoBehaviour host, StringsStore strings, MotionPrefs motion, Func<GameConfig> config)
        {
            _host = host;
            _strings = strings;
            _motion = motion;
            _config = config;
        }

        public void Build(Transform parent)
        {
            _root = UiKit.Stretch(UiKit.Node("Minigame", parent));
            _root.gameObject.SetActive(false);

            UiKit.Scrim("Scrim", _root);

            Image panel = UiKit.Panel("Round Panel", _root, Palette.Panel, 36);
            _gamePanel = panel.rectTransform;
            UiKit.Place(_gamePanel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(648f, 940f));
            UiKit.AddShadow(_gamePanel, 12f, 0.25f);

            RTLTextMeshPro title = UiKit.Text("Title", panel.transform, _strings.Get("game.title"), 42f, Palette.Ink, UiKit.Centred, true, FontStyles.Bold);
            UiKit.Band(title.rectTransform, true, 26f, 56f);

            _roundLine = UiKit.Text("Round", panel.transform, string.Empty, 24f, Palette.InkSoft);
            UiKit.Band(_roundLine.rectTransform, true, 84f, 34f);

            Image track = UiKit.Panel("Time Track", panel.transform, Palette.PanelEdge, 10);
            UiKit.Place(track.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(540f, 16f));

            Image fill = UiKit.Solid("Time Fill", track.transform, Palette.Accent);
            UiKit.Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Right;
            fill.fillAmount = 1f;
            _timeFill = fill;

            RectTransform timeRow = UiKit.Band(UiKit.Node("Time Row", panel.transform), true, 154f, 40f);
            _timeNumber = UiKit.Text(timeRow, string.Empty, 30f, Palette.Ink, UiKit.Centred, true, FontStyles.Bold);

            _prompt = UiKit.Text("Prompt", panel.transform, string.Empty, 38f, Palette.Ink, UiKit.Centred);
            UiKit.Band(_prompt.rectTransform, true, 216f, 168f);

            for (int i = 0; i < 3; i++)
            {
                int index = i;
                LabeledButton option = UiKit.Button(
                    $"Option {i}", panel.transform, string.Empty, 36f, Palette.AccentSoft, Palette.Ink,
                    () => OptionChosen?.Invoke(index), sound: Sound.Select);
                UiKit.Band(option.Rect, true, 400f + (i * 116f), 100f, 48f);
                _options.Add(option);
            }

            _feedbackRow = UiKit.Band(UiKit.Node("Feedback", panel.transform), true, 762f, 150f);

            Image feedbackIcon = UiKit.Icon("Feedback Icon", _feedbackRow, TextureFactory.Check(), Palette.Accent);
            UiKit.Place(feedbackIcon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(84f, 84f));
            _feedbackIcon = feedbackIcon;

            _feedbackText = UiKit.Text("Feedback Text", panel.transform, string.Empty, 30f, Palette.Ink, UiKit.Centred);
            UiKit.Band(_feedbackText.rectTransform, true, 856f, 78f);

            _feedbackRow.gameObject.SetActive(false);

            LabeledButton close = UiKit.Button(
                "Close", panel.transform, string.Empty, 24f, Palette.PanelEdge, Palette.Ink,
                () => Dismissed?.Invoke());
            UiKit.Place(close.Rect, new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(64f, 64f));
            close.Label.text = _strings.Get("common.close");
            close.Label.fontSize = 20f;

            BuildRewardPanel();
        }

        private void BuildRewardPanel()
        {
            Image panel = UiKit.Panel("Reward Panel", _root, Palette.Panel, 36);
            _rewardPanel = panel.rectTransform;
            UiKit.Place(_rewardPanel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 620f));
            UiKit.AddShadow(_rewardPanel, 12f, 0.25f);

            _rewardTitle = UiKit.Text("Title", panel.transform, string.Empty, 38f, Palette.Ink, UiKit.Centred, true, FontStyles.Bold);
            UiKit.Band(_rewardTitle.rectTransform, true, 30f, 52f);

            _rewardIcon = UiKit.Icon("Icon", panel.transform, TextureFactory.Droplet(), Palette.Water);
            UiKit.Place(_rewardIcon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(150f, 150f));

            _rewardNumber = UiKit.Text("Number", panel.transform, string.Empty, 84f, Palette.Ink, UiKit.Centred, true, FontStyles.Bold);
            UiKit.Band(_rewardNumber.rectTransform, true, 280f, 110f);

            _rewardText = UiKit.Text("Text", panel.transform, string.Empty, 32f, Palette.Ink, UiKit.Centred);
            UiKit.Band(_rewardText.rectTransform, true, 400f, 120f, 56f);

            LabeledButton continueButton = UiKit.Button(
                "Continue", panel.transform, _strings.Get("common.continue"), 32f, Palette.Accent, Color.white,
                () => Dismissed?.Invoke());
            UiKit.Place(continueButton.Rect, new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(280f, 92f));

            _rewardPanel.gameObject.SetActive(false);
        }

        // ---- Round flow -------------------------------------------------------------------------

        public void BeginRoundSet()
        {
            _root.gameObject.SetActive(true);
            _rewardPanel.gameObject.SetActive(false);
            _gamePanel.gameObject.SetActive(true);
            ClearFeedback();
        }

        public void EndRoundSet()
        {
            _root.gameObject.SetActive(false);
        }

        public void SetRound(int round, int totalRounds)
        {
            if (_strings.Has("game.round"))
            {
                _roundLine.gameObject.SetActive(true);
                _roundLine.text = _strings.Display(_strings.Format(
                    "game.round",
                    new Dictionary<string, string>(2, StringComparer.Ordinal)
                    {
                        { "n", _strings.DisplayNumber(round) },
                        { "total", _strings.DisplayNumber(totalRounds) }
                    }));
            }
            else
            {
                _roundLine.gameObject.SetActive(false);
            }
        }

        public void SetPrompt(string word)
        {
            _prompt.text = _strings.Display(_strings.FormatWord("game.prompt", word));
        }

        public void SetTimeLeft(float secondsLeft, float totalSeconds)
        {
            _timeNumber.text = _strings.Display(
                _strings.Get("game.time_left") + " " + _strings.DisplayNumber(Mathf.CeilToInt(Mathf.Max(0f, secondsLeft))));

            if (_timeFill != null)
            {
                float fraction = totalSeconds <= 0f ? 0f : Mathf.Clamp01(secondsLeft / totalSeconds);
                _timeFill.fillAmount = fraction;
                _timeFill.color = fraction <= 0.25f ? Palette.Caution : Palette.Accent;
            }
        }

        public void SetChoices(IReadOnlyList<string> choices)
        {
            ClearFeedback();

            for (int i = 0; i < _options.Count; i++)
            {
                LabeledButton option = _options[i];
                bool used = choices != null && i < choices.Count;
                option.Root.SetActive(used);
                if (!used)
                {
                    continue;
                }

                option.Label.text = _strings.Display(choices[i]);
                option.Label.color = Palette.Ink;
                option.Label.fontSize = 36f;
                option.Background.color = Palette.AccentSoft;
                option.SetInteractable(true);

                var image = option.Background;
                image.color = Palette.AccentSoft;
            }
        }

        /// <summary>Correct answer: celebrate in place, at the option the player actually chose.</summary>
        public void ShowCorrect(int chosenIndex)
        {
            _feedbackRow.gameObject.SetActive(true);
            _feedbackIcon.sprite = TextureFactory.Check();
            _feedbackIcon.color = Palette.Accent;
            _feedbackText.text = _strings.Display(_strings.Get("game.correct"));
            _feedbackText.color = Palette.Accent;

            for (int i = 0; i < _options.Count; i++)
            {
                _options[i].SetInteractable(false);
                if (i == chosenIndex)
                {
                    _options[i].Background.color = Palette.Accent;
                    _options[i].Label.color = Color.white;
                }
            }
        }

        /// <summary>
        /// Wrong answer: a kind sentence plus the correct word. The correct option is marked separately
        /// from the player's choice, and nothing is taken away - the brief asks for exactly this.
        /// </summary>
        public void ShowWrong(int correctIndex)
        {
            ShowWrong(correctIndex, _strings.Get("game.wrong"));
        }

        private void ShowWrong(int correctIndex, string template)
        {
            string answer = correctIndex >= 0 && correctIndex < _options.Count
                ? _options[correctIndex].Label.text
                : string.Empty;

            _feedbackRow.gameObject.SetActive(true);
            _feedbackIcon.sprite = TextureFactory.Cross();
            _feedbackIcon.color = Palette.Caution;
            _feedbackText.text = _strings.Display(_strings.FormatAnswer("game.wrong", answer));
            _feedbackText.color = Palette.Ink;

            for (int i = 0; i < _options.Count; i++)
            {
                _options[i].SetInteractable(false);
                _options[i].Background.color = Palette.PanelEdge;
                _options[i].Label.color = Palette.InkSoft;

                if (i == correctIndex)
                {
                    _options[i].Background.color = Palette.AccentSoft;
                    _options[i].Label.color = Palette.Accent;
                }
            }
        }

        public void ClearFeedback()
        {
            if (_feedbackRow != null)
            {
                _feedbackRow.gameObject.SetActive(false);
            }
        }

        // ---- Reward moment ---------------------------------------------------------------------

        /// <summary>
        /// The reward moment. Reaching the threshold counts the drops up so the gain is visible as
        /// movement; falling short gets an encouraging line and no reprimand, exactly as the brief asks.
        /// </summary>
        public void ShowReward(bool metThreshold, int correct, int reward)
        {
            GameConfig config = Configuration;

            _gamePanel.gameObject.SetActive(false);
            _rewardPanel.gameObject.SetActive(true);

            if (_rewardTitle != null)
            {
                bool hasTitle = _strings.Has("game.reward_title");
                _rewardTitle.gameObject.SetActive(hasTitle);
                if (hasTitle)
                {
                    _rewardTitle.text = _strings.Display(_strings.Get("game.reward_title"));
                }
            }

            if (metThreshold)
            {
                _rewardIcon.sprite = TextureFactory.Droplet();
                _rewardIcon.color = Palette.Water;
                _rewardText.text = _strings.Display(_strings.FormatNumber("game.reward", reward));
                _rewardText.color = Palette.Ink;

                if (_rewardCountUp != null)
                {
                    _host.StopCoroutine(_rewardCountUp);
                }

                float duration = _motion.Duration(Mathf.Max(0.35f, config.NumberCountUpSeconds));
                _rewardCountUp = _host.StartCoroutine(Tween.Value(0f, reward, duration, value =>
                    _rewardNumber.text = _strings.DisplayNumber(Mathf.RoundToInt(value))));
            }
            else
            {
                // No reward is met with a sprout, not a cross: the player did nothing wrong.
                _rewardIcon.sprite = TextureFactory.Leaf();
                _rewardIcon.color = Palette.Leaf;
                _rewardNumber.text = string.Empty;
                _rewardText.text = _strings.Display(_strings.Get("game.no_reward"));
                _rewardText.color = Palette.Ink;
            }
        }

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        private GameConfig Configuration => _config() ?? GameConfig.Defaults();
    }
}