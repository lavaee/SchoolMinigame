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
        private bool _connected;
        private bool _watching;
        private bool _retrying;
        private bool _hasServer;
        private ConnectionGateView _gate;
        private UpdatePromptView _update;

        /// <summary>
        /// The revision the server has reported and the player has not accepted yet, or -1 for none.
        ///
        /// It survives a rebuild of the interface, because a rebuild is not an answer: the content has still
        /// not been taken, and a card that disappeared because something else refreshed the scene would drop
        /// the update on the floor.
        /// </summary>
        private int _pendingVersion = -1;

        /// <summary>
        /// Set by the screenshot harness, which plays offline on purpose: without it the gate would refuse
        /// to start and every capture would be a picture of the gate. The game itself never sets it.
        /// </summary>
        /// <summary>Set while the connection is being watched, so only one watch is ever running.</summary>
        private bool _connectionWatching;

        /// <summary>Set while the gate's own retry loop is running, so it is never doubled up.</summary>
        private bool _gateWatching;

        public static bool SuppressServerGate;

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

            // Phase two: refresh from the data server when one is configured. A server that is not there is
            // not an error by default - the copy on the device is enough to play - but a build can insist
            // on one, and that case gets a screen of its own instead of a shrug.
            _hasServer = DataLoader.CandidateUrls(_save.ServerUrlOverride, _bundle?.Dev).Count > 0;
            if (_hasServer)
            {
                yield return Connect();
            }

            if (RequiresServer && !_connected && !SuppressServerGate)
            {
                BlockOnServer();
                yield break;
            }

            // Phase three: keep listening for a push. This is what makes the admin page's button reach a
            // phone that is already open, without anyone touching the device.
            WatchForPushes();
        }

        /// <summary>
        /// Waits on the server for the content revision to change, and offers whatever arrives.
        ///
        /// This used to ask every ten seconds, so a push was felt anywhere between instantly and ten seconds
        /// later. The person pushing is usually looking at the screen, so that delay read as "it did not
        /// work". The server holds this question open now and the push itself answers it, which is what
        /// makes a push arrive in the time one round trip takes.
        ///
        /// The offer is made even in the middle of a round. The card stops the clock rather than the round,
        /// and taking it defers the interface rebuild until the round set closes, so a round can never be
        /// stranded by an update that the player chose to take.
        /// </summary>
        private IEnumerator VersionWatch()
        {
            while (true)
            {
                // A negative version asks "which revision is it now?" and is answered at once, which is how
                // the first watch learns its baseline: an app that starts after a push has nothing to be
                // told about, and should not offer the content it already has.
                int version = -1;
                yield return _loader.AwaitVersion(
                    _save.ServerUrlOverride, _contentVersion, UpdateWatchHoldSeconds, reported => version = reported);

                if (version < 0)
                {
                    // Nothing answered. The connection watch owns the gate; all this owes a dead address is
                    // not to spin against it.
                    yield return new WaitForSecondsRealtime(ConnectionCheckSeconds);
                    continue;
                }

                if (version == _contentVersion)
                {
                    continue;
                }

                bool baseline = _contentVersion < 0;
                _contentVersion = version;

                if (baseline)
                {
                    continue;
                }

                Log.Info($"Content revision {version} was pushed; offering it instead of applying it.");
                OfferUpdate(version);
            }
        }

        /// <summary>
        /// Puts the update card up and leaves the decision with the player.
        ///
        /// Applying the content here instead is what made a push feel like a glitch: a round could vanish
        /// mid-answer, and the only sign was a chip that flashed for three seconds.
        /// </summary>
        private void OfferUpdate(int version)
        {
            _pendingVersion = version;
            _update?.Show(version);

            // Paused before the card is read, not after: the round clock is shared, and it must not run
            // while somebody is deciding.
            RefreshPaused();

            if (Strings.Has("content.pending"))
            {
                _hud.SetStatusChip(Strings.Get("content.pending"));
            }
        }

        /// <summary>Takes the update the player accepted.</summary>
        private void ApplyUpdate()
        {
            _pendingVersion = -1;
            _update?.Hide();
            RefreshPaused();
            StartCoroutine(ApplyUpdateRoutine());
        }

        private IEnumerator ApplyUpdateRoutine()
        {
            yield return ReloadRoutine();

            // The note comes after the reload, which rebuilds the interface: shown before it, the rebuild
            // would wipe it a frame later.
            if (Strings.Has("content.updated"))
            {
                _hud.FlashStatusChip(Strings.Get("content.updated"), 3.5f);
            }
        }

        /// <summary>How often the gate tries again on its own while it is up.</summary>
        private const float GateRetrySeconds = 10f;

        /// <summary>
        /// Loads the server's copy of the content and remembers whether a server answered at all.
        ///
        /// "A server answered" is deliberately not the same question as "the content changed": the version
        /// watch needs to know it has a live address, and the gate needs to know when it has none.
        /// </summary>
        private IEnumerator Connect()
        {
            DataBundle remote = null;
            yield return _loader.Load(_save.ServerUrlOverride, true, bundle => remote = bundle);

            _connected = !string.IsNullOrEmpty(_loader.LastServerUrl);

            if (!_connected)
            {
                Log.Warn("No data server answered; the copy on the device is being used.");
                yield break;
            }

            if (remote != null && remote.AnyFileFromRemote)
            {
                Log.Info("Remote data changed the active bundle; rebuilding from the server copy.");
                ApplyBundle(remote);
            }
        }

        private void WatchForPushes()
        {
            if (_watching || !_hasServer)
            {
                return;
            }

            _watching = true;
            StartCoroutine(VersionWatch());
            StartCoroutine(ConnectionWatch());
        }

        /// <summary>True when this bundle insists on a server before the game may start.</summary>
        private bool RequiresServer => _bundle != null && _bundle.Dev != null && _bundle.Dev.RequireServer;

        private List<string> TriedAddresses() =>
            DataLoader.CandidateUrls(_save.ServerUrlOverride, _bundle?.Dev);

        /// <summary>
        /// Puts the gate up and keeps trying quietly behind it.
        ///
        /// The retry button is the deliberate path; the loop underneath means a server that comes up a
        /// minute later is still picked up without anyone touching the phone.
        /// </summary>
        private void BlockOnServer()
        {
            Log.Warn("This build requires a data server and none answered; waiting rather than starting.");

            List<string> tried = TriedAddresses();
            _gate?.SetHeadline("gate.title", "gate.body");
            _gate?.Show(tried);
            _gate?.SuggestAddress(tried.Count > 0 ? tried[0] : string.Empty);
            _gate?.SetStatus(Strings.Has("gate.checking") ? Strings.Get("gate.checking") : string.Empty);

            StartCoroutine(GateWatch());
        }

        private IEnumerator GateWatch()
        {
            if (_gateWatching)
            {
                yield break;
            }

            _gateWatching = true;
            while (_gate != null && _gate.Visible)
            {
                yield return new WaitForSecondsRealtime(GateRetrySeconds);
                yield return RetryConnection();
            }

            _gateWatching = false;
        }

        /// <summary>
        /// Watches the server while the game is being played.
        ///
        /// A dropped connection used to be invisible: the app kept the last answer it had and only noticed
        /// when it was started again. This asks, and when the answer stops coming the game is paused and
        /// the gate says why, so nobody plays on against a server that has gone.
        /// </summary>
        private IEnumerator ConnectionWatch()
        {
            if (_connectionWatching || !RequiresServer || SuppressServerGate)
            {
                yield break;
            }

            _connectionWatching = true;

            while (true)
            {
                yield return new WaitForSecondsRealtime(ConnectionCheckSeconds);

                // While the gate is up its own loop is already asking, and it owns getting us back.
                if (_gate != null && _gate.Visible)
                {
                    continue;
                }

                int version = -1;
                yield return _loader.CheckVersion(_save.ServerUrlOverride, reported => version = reported);

                if (version >= 0)
                {
                    continue;
                }

                Log.Warn("The data server stopped answering; pausing until it is back.");
                ConnectionLost();
            }
        }

        /// <summary>How often the connection is checked, from data/dev.json.</summary>
        private float ConnectionCheckSeconds =>
            _bundle != null && _bundle.Dev != null && _bundle.Dev.ConnectionCheckSeconds >= 1f
                ? _bundle.Dev.ConnectionCheckSeconds
                : 3f;

        /// <summary>
        /// How long the server is asked to hold a content watch open, from data/dev.json.
        ///
        /// Long enough that an idle app is not churning through requests, short enough that a connection
        /// dropped somewhere along the way is noticed by the watch rather than trusted forever.
        /// </summary>
        private float UpdateWatchHoldSeconds =>
            _bundle != null && _bundle.Dev != null && _bundle.Dev.UpdateWatchHoldSeconds >= 2f
                ? _bundle.Dev.UpdateWatchHoldSeconds
                : 20f;

        /// <summary>Puts the gate up mid-game, because a server that was answering has stopped.</summary>
        private void ConnectionLost()
        {
            _connected = false;

            _gate?.SetHeadline("gate.lost_title", "gate.lost_body");
            _gate?.Show(TriedAddresses());
            _gate?.SetStatus(Strings.Has("gate.lost_status") ? Strings.Get("gate.lost_status") : string.Empty);
            AudioDirector.Play(Sound.Wrong);
            RefreshPaused();

            StartCoroutine(GateWatch());
        }

        /// <summary>
        /// Stops or resumes everything the game is doing.
        ///
        /// Time is what the round timer, the count-ups and the animations read, so scaling it is the one
        /// pause a tap arriving while the gate is up cannot cheat.
        /// </summary>
        private void SetPaused(bool paused) => Time.timeScale = paused ? 0f : 1f;

        /// <summary>
        /// Stops time while something is standing in front of the player, and starts it again when nothing is.
        ///
        /// Both the gate and the update card ask for a decision, and asking the two panels rather than
        /// counting pauses keeps one of them from cancelling the other out.
        /// </summary>
        private void RefreshPaused() =>
            SetPaused((_gate != null && _gate.Visible) || (_update != null && _update.Visible));

        /// <summary>The gate's retry: remembers a corrected address and tries again.</summary>
        private void RequestRetry(string address)
        {
            // An emptied field is a deliberate "forget the address I typed". Without that, an address saved
            // once - a loopback address typed for a USB cable, say - could never be undone from the device.
            if (!string.Equals(address ?? string.Empty, _save.ServerUrlOverride ?? string.Empty, StringComparison.Ordinal))
            {
                _save.ServerUrlOverride = address ?? string.Empty;
                if (!SaveStore.Save(_save, out string error))
                {
                    Log.Warn($"The corrected address could not be saved ({error}); it will still be used now.");
                }
            }

            StartCoroutine(RetryConnection());
        }

        private IEnumerator RetryConnection()
        {
            if (_retrying)
            {
                yield break;
            }

            _retrying = true;
            _gate?.SetStatus(Strings.Has("gate.checking") ? Strings.Get("gate.checking") : string.Empty);

            _hasServer = DataLoader.CandidateUrls(_save.ServerUrlOverride, _bundle?.Dev).Count > 0;
            yield return Connect();
            _retrying = false;

            if (_connected)
            {
                _gate?.Hide();
                RefreshPaused();
                if (Strings.Has("gate.connected"))
                {
                    _hud.FlashStatusChip(Strings.Get("gate.connected"), 3.5f);
                }

                WatchForPushes();
                yield break;
            }

            _gate?.Show(TriedAddresses());
            _gate?.SetStatus(Strings.Has("gate.failed") ? Strings.Get("gate.failed") : string.Empty);
            AudioDirector.Play(Sound.Wrong);
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

            // Last, so that it covers everything else when it has to.
            _gate = new ConnectionGateView(Strings, RequestRetry);
            _gate.Build(canvas.transform);

            _update = new UpdatePromptView(Strings, ApplyUpdate);
            _update.Build(canvas.transform);

            // A rebuild is not an answer: if content is still waiting to be taken, it is offered again.
            if (_pendingVersion >= 0)
            {
                OfferUpdate(_pendingVersion);
            }

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

            string sourceLabel = Strings.Get(key);
            int count = remote > 0 ? remote : cache;

            // The words and the number together, rather than a bare count: a number alone in the corner is a
            // riddle, and the count is worth explaining because it is how many of the six data files came
            // from there.
            if (Strings.Has("settings.data_source_count"))
            {
                return Strings.Format("settings.data_source_count", new Dictionary<string, string>(2, StringComparer.Ordinal)
                {
                    { "source", sourceLabel },
                    { "n", Strings.DisplayNumber(count) }
                });
            }

            return sourceLabel + " " + Strings.DisplayNumber(count);
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

            // Below the threshold the moment is still marked, but kindly: the reward clip is a fanfare and
            // the encouragement clip is a nudge, and the brief is explicit that a missed round is not blame.
            AudioDirector.Play(met ? Sound.Reward : Sound.Encourage);
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

            // Scaled time, so the round set stops when the game is paused for a lost connection. With the
            // clock at its normal speed this is the same number it always was.
            _session.TimeLeft -= Time.deltaTime;
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
