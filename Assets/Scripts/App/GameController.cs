using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Wires the data, the rules and the views together, and owns the only two moments when anything
    /// happens: watering the pot, and the morning that follows it.
    ///
    /// The controller is intentionally thin. All of the game's behaviour lives in the core classes that
    /// the tests exercise; this file decides order of operations, animation timing and when to save.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private sealed class PairsSession
        {
            public List<PairRound> Rounds;
            public int Index;
            public int Correct;
            public float TimeLeft;
            public bool Running;
            public bool TimedOut;
            public bool CancelRequested;
            public int PendingChoice = -1;
        }

        private readonly MotionPrefs _motion = new MotionPrefs();
        private readonly DataLoader _loader = new DataLoader();

        private DataBundle _bundle;
        private PlantState _state = new PlantState();
        private SaveState _save = new SaveState();

        private Camera _camera;
        private PotView _potView;
        private AudioDirector _audio;
        private Transform _uiRoot;

        private HudView _hud;
        private MinigameView _minigame;
        private SettingsView _settings;

        private PairsSession _session;
        private bool _rewardDismissed;
        private bool _morningBusy;
        private bool _deferredRebuild;
        private bool _busy;
        private int _pendingReward;

        /// <summary>Last content revision the server reported; -1 until the first successful check.</summary>
        private int _contentVersion = -1;

        private float _nextVersionCheck;

        /// <summary>
        /// How often the running game asks the server which revision it is serving. Often enough that a
        /// push is felt within a few seconds of the designer pressing the button, rare enough to be free.
        /// </summary>
        private const float VersionPollSeconds = 10f;

        private GameConfig Config => _bundle?.Config ?? GameConfig.Defaults();
        private StringsStore Strings => _bundle?.Strings ?? StringsStore.CreateEmpty();

        /// <summary>Shared so the pot view animates on the same terms as the interface.</summary>
        public MotionPrefs Motion => _motion;

        public void Initialize(Camera camera, PotView potView, AudioDirector audio)
        {
            _camera = camera;
            _potView = potView;
            _audio = audio;
        }

        /// <summary>The sound director, if the scene has one. Nothing here requires it to exist.</summary>
        public AudioDirector Audio => _audio;

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            LoadSave();

            // Phase one: everything that is already on the device. This is what makes the game start
            // instantly and work with no network at all - the bundled files inside the app are the floor.
            DataBundle local = null;
            yield return _loader.LoadFromDiskOnly(bundle => local = bundle);
            ApplyBundle(local);

            // Phase two: if a data server is configured, refresh in the background and rebuild only if
            // something actually changed. A dead server is not an error; it is simply ignored.
            if (!string.IsNullOrEmpty(_bundle?.Dev?.DataServerUrl))
            {
                DataBundle remote = null;
                yield return _loader.Load(_save.ServerUrlOverride, true, bundle => remote = bundle);
                if (remote != null && remote.AnyFileFromRemote)
                {
                    Log.Info("Remote data changed the active bundle; rebuilding from the server copy.");
                    ApplyBundle(remote);
                }
            }

            // Phase three: keep listening for a push. This is what makes the admin page's button reach a
            // phone that is already open, without anyone touching the device.
            if (!string.IsNullOrEmpty(_bundle?.Dev?.DataServerUrl))
            {
                StartCoroutine(VersionWatch());
            }
        }

        /// <summary>
        /// Watches the server's content revision and reloads when it changes.
        ///
        /// Deliberately quiet: it waits for a moment when nothing is in progress, never interrupts a round
        /// set or a report, and announces itself with a brief note rather than a dialog.
        /// </summary>
        private IEnumerator VersionWatch()
        {
            _nextVersionCheck = Time.realtimeSinceStartup + VersionPollSeconds;

            while (true)
            {
                yield return new WaitUntil(() => Time.realtimeSinceStartup >= _nextVersionCheck);
                _nextVersionCheck = Time.realtimeSinceStartup + VersionPollSeconds;

                if (_session != null && _session.Running)
                {
                    continue;
                }

                int version = -1;
                yield return _loader.CheckVersion(_save.ServerUrlOverride, reported => version = reported);

                if (version < 0)
                {
                    continue;
                }

                if (_contentVersion < 0)
                {
                    _contentVersion = version;
                    continue;
                }

                if (version == _contentVersion)
                {
                    continue;
                }

                _contentVersion = version;
                Log.Info($"Content revision {version} was pushed; reloading.");
                yield return ReloadRoutine();

                if (Strings.Has("content.updated"))
                {
                    _hud.FlashStatusChip(Strings.Get("content.updated"), 3.5f);
                }
            }
        }

        // ---- Data ------------------------------------------------------------------------------

        private void LoadSave()
        {
            if (SaveStore.TryLoad(out SaveState loaded, out string error))
            {
                _save = loaded;
            }
            else if (!string.IsNullOrEmpty(error))
            {
                Log.Warn($"Save could not be read ({error}); starting a new game.");
            }

            _motion.ReduceMotion = _save.ReduceMotion;
            _state = _save.Plant ?? PlantState.NewGame(GameConfig.Defaults());
        }

        private void ApplyBundle(DataBundle bundle)
        {
            if (bundle == null)
            {
                return;
            }

            _bundle = bundle;

            // A state saved against an older config might point past the end of the stage list.
            SaveStore.ClampToConfig(_save, bundle.Config);
            _state = _save.Plant ?? PlantState.NewGame(bundle.Config);
            _save.Plant = _state;
            _motion.ReduceMotion = _save.ReduceMotion;

            foreach (string warning in bundle.Warnings)
            {
                Log.Warn(warning);
            }

            if (_session != null && _session.Running)
            {
                // Rebuilding the interface mid-game would strand the round set; wait until it closes.
                _deferredRebuild = true;
            }
            else
            {
                BuildInterface();
            }
        }

        private void BuildInterface()
        {
            UiKit.EnsureEventSystem(transform);

            // The canvas is reused, never rebuilt: the interface lives in the scene, so destroying it
            // would throw away anything re-skinned by hand. The views resolve their widgets by name and
            // update them in place.
            Canvas canvas = UiKit.CreateCanvas("UI", transform);
            _uiRoot = canvas.transform;

            _hud = new HudView(this, Strings, _motion, () => Config);
            _hud.Build(canvas.transform);
            _hud.WaterClicked += OnWater;
            _hud.PlayClicked += OnPlay;
            _hud.MorningClicked += OnMorning;
            _hud.SettingsClicked += OnSettings;

            _minigame = new MinigameView(this, Strings, _motion, () => Config);
            _minigame.Build(canvas.transform);
            _minigame.OptionChosen += OnOptionChosen;
            _minigame.Dismissed += OnMinigameDismissed;

            _settings = new SettingsView(Strings, _motion, () => _bundle);
            _settings.Build(canvas.transform);
            _settings.ReloadRequested += OnReloadRequested;
            _settings.ResetSaveRequested += OnResetSaveRequested;

            _deferredRebuild = false;

            // The status chip is deliberately quiet: it only appears when the data did not come from the
            // app bundle, because that is the case worth knowing about during a live demo.
            _hud.SetStatusChip(StatusChipText());

            RefreshAll(animate: false);
        }

        /// <summary>
        /// Builds the interface for a bundle without touching the save file.
        ///
        /// Used by the scene baker: in the editor no game is running, so nothing may be written to disk,
        /// but the widgets still have to exist before the scene can be saved with them in it.
        /// </summary>
        public void BuildInterfaceFor(DataBundle bundle)
        {
            if (bundle == null)
            {
                return;
            }

            _bundle = bundle;
            _state = PlantState.NewGame(bundle.Config);
            BuildInterface();
        }

        private string StatusChipText()
        {
            if (_bundle == null || !_bundle.Dev.ShowDataSourceInfo)
            {
                return string.Empty;
            }

            int remote = 0;
            int cache = 0;
            foreach (string fileName in DataLoader.AllFiles)
            {
                DataSource source = _bundle.SourceOf(fileName);
                if (source == DataSource.Remote)
                {
                    remote++;
                }
                else if (source == DataSource.Cache)
                {
                    cache++;
                }
            }

            if (remote == 0 && cache == 0)
            {
                return string.Empty;
            }

            string key = remote > 0 ? "settings.data_source.remote" : "settings.data_source.cache";
            if (!Strings.Has(key))
            {
                return string.Empty;
            }

            return Strings.Get(key) + " " + Strings.DisplayNumber(remote > 0 ? remote : cache);
        }

        // ---- Core actions ----------------------------------------------------------------------

        private void OnWater()
        {
            if (_busy || _state.Watered)
            {
                return;
            }

            // The rule for watering at full bloom is data-driven: when the optional hint key is absent,
            // watering is allowed and the morning simply reports that nothing changed.
            bool allowAtFullBloom = !Strings.Has("water.not_needed");
            if (!_state.TryWater(Config, allowAtFullBloom))
            {
                _hud.RefreshActions(_state, allowAtFullBloom);
                return;
            }

            Log.Info($"Watered the pot for {Config.Watering.CostDrops} drops; {_state.Drops} left.");
            AudioDirector.Play(Sound.Water);
            _potView.PlayWaterPour();
            RefreshAll(animate: true);
            Save();
        }

        private void OnMorning()
        {
            if (_busy || _morningBusy || _hud.ReportVisible)
            {
                return;
            }

            StartCoroutine(MorningRoutine());
        }

        private IEnumerator MorningRoutine()
        {
            _morningBusy = true;
            GameConfig config = Config;

            int dropsBefore = _state.Drops;
            AudioDirector.Play(Sound.Morning);
            MorningOutcome outcome = _state.ApplyMorning(config);
            _save.MorningCount++;
            Save();

            // Show the growth first, then the report: the player should see the plant change before
            // reading about it.
            RefreshAll(animate: true, previousDrops: dropsBefore);
            float delay = _motion.Duration(config.TransitionSeconds);
            if (delay > 0f)
            {
                yield return Tween.WaitUnscaled(delay * 0.8f);
            }

            string body;
            switch (outcome)
            {
                case MorningOutcome.Grew:
                    body = Strings.FormatState("report.grew", Strings.StateLabel(_state.DisplayStateId(config)));
                    break;
                case MorningOutcome.Thirsty:
                    body = Strings.Get("report.thirsty");
                    break;
                case MorningOutcome.Recovered:
                    body = Strings.Has("report.recovered") ? Strings.Get("report.recovered") : Strings.Get("report.nothing");
                    break;
                default:
                    body = Strings.Get("report.nothing");
                    break;
            }

            // The verdict is heard before it is read: the report follows the sound by a beat.
            if (outcome == MorningOutcome.Grew)
            {
                AudioDirector.Play(Sound.Growth);
            }
            else if (outcome == MorningOutcome.Thirsty)
            {
                AudioDirector.Play(Sound.Thirst);
            }

            _hud.ShowReport(Strings.Get("report.title"), body);
            Log.Info($"Morning {_save.MorningCount}: {outcome}.");
            _morningBusy = false;
        }

        private void OnPlay()
        {
            if (_busy || _session != null || _hud.ReportVisible)
            {
                return;
            }

            if (_bundle == null || !_bundle.Pairs.HasUsablePairs)
            {
                Log.Warn("The minigame was started with no usable pairs; it was skipped.");
                return;
            }

            StartCoroutine(RunMinigame());
        }

        private IEnumerator RunMinigame()
        {
            _busy = true;
            GameConfig config = Config;

            var session = new PairsSession
            {
                Rounds = _bundle.Pairs.BuildRounds(
                    config.MiniGame.Rounds,
                    config.MiniGame.ChoicesShown,
                    new System.Random()),
                TimeLeft = config.MiniGame.TotalSeconds,
                Running = true
            };

            if (session.Rounds == null || session.Rounds.Count == 0)
            {
                _busy = false;
                yield break;
            }

            _session = session;
            _minigame.BeginRoundSet();

            for (session.Index = 0; session.Index < session.Rounds.Count; session.Index++)
            {
                if (session.TimedOut || session.CancelRequested)
                {
                    break;
                }

                PairRound round = session.Rounds[session.Index];
                _minigame.SetRound(session.Index + 1, session.Rounds.Count);
                _minigame.SetPrompt(round.Word);
                _minigame.SetChoices(round.Choices);

                session.PendingChoice = -1;
                while (session.PendingChoice < 0 && !session.TimedOut && !session.CancelRequested)
                {
                    yield return null;
                }

                if (session.TimedOut || session.CancelRequested)
                {
                    break;
                }

                int chosen = session.PendingChoice;
                session.PendingChoice = -1;

                bool correct = round.IsAnswer(chosen);
                if (correct)
                {
                    session.Correct++;
                    AudioDirector.Play(Sound.Correct);
                    _minigame.ShowCorrect(chosen);
                }
                else
                {
                    AudioDirector.Play(Sound.Wrong);
                    _minigame.ShowWrong(round.AnswerIndex);
                }

                yield return Tween.WaitUnscaled(
                    correct ? config.CorrectFeedbackSeconds : config.WrongFeedbackSeconds);
            }

            session.Running = false;

            if (session.CancelRequested)
            {
                // Left early: no reward moment, and nothing is taken away either.
                _minigame.EndRoundSet();
                _session = null;
                _busy = false;
                RebuildIfDeferred();
                yield break;
            }

            int reward = config.MiniGame.RewardFor(session.Correct);
            bool met = config.MiniGame.MeetsRewardThreshold(session.Correct);
            _pendingReward = met ? reward : 0;

            AudioDirector.Play(Sound.Reward);
            _minigame.ShowReward(met, session.Correct, reward);

            _rewardDismissed = false;
            while (!_rewardDismissed)
            {
                yield return null;
            }

            _minigame.EndRoundSet();
            _session = null;

            if (_pendingReward > 0)
            {
                int before = _state.Drops;
                _state.Drops += _pendingReward;
                Save();
                RefreshAll(animate: true, previousDrops: before);
            }

            _pendingReward = 0;
            _busy = false;
            RebuildIfDeferred();
        }

        private void OnOptionChosen(int index)
        {
            if (_session == null || !_session.Running)
            {
                return;
            }

            _session.PendingChoice = index;
        }

        private void OnMinigameDismissed()
        {
            if (_session != null && _session.Running)
            {
                _session.CancelRequested = true;
                return;
            }

            _rewardDismissed = true;
        }

        private void OnSettings()
        {
            _settings.Show();
        }

        private void OnReloadRequested()
        {
            StartCoroutine(ReloadRoutine());
        }

        private IEnumerator ReloadRoutine()
        {
            _settings.Close();
            DataBundle refreshed = null;
            yield return _loader.Load(_save.ServerUrlOverride, true, bundle => refreshed = bundle);

            if (refreshed != null)
            {
                ApplyBundle(refreshed);
            }
        }

        private void OnResetSaveRequested()
        {
            SaveStore.Delete();
            _save = new SaveState();
            _state = PlantState.NewGame(Config);
            _save.Plant = _state;
            _motion.ReduceMotion = false;
            Save();
            RefreshAll(animate: false);
            Log.Info("Save deleted and a new game started.");
        }

        private void RebuildIfDeferred()
        {
            if (_deferredRebuild)
            {
                BuildInterface();
            }
        }

        // ---- Refresh and persistence -----------------------------------------------------------

        private void Update()
        {
            if (_session == null || !_session.Running)
            {
                return;
            }

            _session.TimeLeft -= Time.unscaledDeltaTime;
            _minigame.SetTimeLeft(_session.TimeLeft, Config.MiniGame.TotalSeconds);

            if (_session.TimeLeft <= 0f)
            {
                // Out of time: the round set ends with whatever was earned. Nothing is lost or scolded.
                _session.TimedOut = true;
                _session.TimeLeft = 0f;
            }
        }

        private void RefreshAll(bool animate, int previousDrops = -1)
        {
            if (_hud == null)
            {
                return;
            }

            PotVisual visual = PotVisual.Resolve(Config, _state);
            _hud.SetDrops(previousDrops, _state.Drops, animate);
            _hud.RefreshActions(_state, !Strings.Has("water.not_needed"));
            _hud.RefreshPot(_state, visual);
            _potView.Apply(visual, Config, animate);
        }

        private void Save()
        {
            _save.Plant = _state;
            _save.ReduceMotion = _motion.ReduceMotion;
            if (!SaveStore.Save(_save, out string error))
            {
                Log.Warn($"Save failed: {error}");
            }
        }
    }
}
