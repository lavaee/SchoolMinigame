using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SmallPot.EditorTools
{
    /// <summary>
    /// Development harness: enters play mode and takes screenshots of scripted scenarios.
    ///
    /// It exists because the layout, the Persian shaping and the colour choices cannot be verified by a
    /// compiler, and a reviewer on a phone is not the right place to discover that a label overflows.
    /// Each scenario drives the real interface through its real buttons, so what is captured is what the
    /// game actually does.
    ///
    ///   Unity.exe -executeMethod SmallPot.EditorTools.CaptureRunner.Run
    /// with the environment variable SMALLPOT_SCENARIO set to one of the scenario names below.
    /// </summary>
    public static class CaptureRunner
    {
        private const string ActiveKey = "SmallPot.Capture.Active";
        private const string ScenarioKey = "SmallPot.Capture.Scenario";
        private const string DirectoryKey = "SmallPot.Capture.Directory";

        private static List<string> _steps;
        private static int _step;
        private static float _waitUntil;
        private static string _scenario = "start";
        private static string _outputDirectory;

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            _scenario = Environment.GetEnvironmentVariable("SMALLPOT_SCENARIO") ?? "start";
            _outputDirectory = Environment.GetEnvironmentVariable("SMALLPOT_CAPTURE_DIR")
                               ?? Path.Combine(Directory.GetCurrentDirectory(), "Logs", "shots");
            Directory.CreateDirectory(_outputDirectory);

            SessionState.SetString(ScenarioKey, _scenario);
            SessionState.SetString(DirectoryKey, _outputDirectory);
            SessionState.SetBool(ActiveKey, true);

            PrepareSave(_scenario);

            EditorSceneManager.OpenScene(ProjectSetup.ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            if (_steps == null)
            {
                // Entering play mode reloads the domain, so everything set up in Run() is restored from
                // SessionState rather than kept in a static field.
                _scenario = SessionState.GetString(ScenarioKey, "start");
                _outputDirectory = SessionState.GetString(DirectoryKey, Path.Combine(Directory.GetCurrentDirectory(), "Logs", "shots"));
                Directory.CreateDirectory(_outputDirectory);
                _steps = BuildSteps(_scenario);
                _step = 0;
                _waitUntil = Time.realtimeSinceStartup + 0.8f;
                return;
            }

            if (Time.realtimeSinceStartup < _waitUntil)
            {
                return;
            }

            if (_step >= _steps.Count)
            {
                SessionState.SetBool(ActiveKey, false);
                Debug.Log($"[SmallPot] Capture finished for scenario '{_scenario}'.");
                EditorApplication.Exit(0);
                return;
            }

            string step = _steps[_step++];
            Execute(step);
        }

        private static void Execute(string step)
        {
            int split = step.IndexOf(':');
            string verb = split < 0 ? step : step.Substring(0, split);
            string argument = split < 0 ? string.Empty : step.Substring(split + 1);

            switch (verb)
            {
                case "wait":
                    _waitUntil = Time.realtimeSinceStartup + ParseFloat(argument);
                    break;

                case "click":
                    Click(argument);
                    _waitUntil = Time.realtimeSinceStartup + 0.35f;
                    break;

                case "option":
                    ClickOption(int.Parse(argument));
                    _waitUntil = Time.realtimeSinceStartup + 0.5f;
                    break;

                case "shot":
                    Capture(argument);
                    _waitUntil = Time.realtimeSinceStartup + 0.2f;
                    break;

                default:
                    Debug.LogWarning($"[SmallPot] Unknown capture step '{step}'.");
                    break;
            }
        }

        private static List<string> BuildSteps(string scenario)
        {
            var steps = new List<string> { "wait:0.8" };

            switch (scenario)
            {
                case "start":
                    steps.Add("shot:start");
                    break;

                case "watered":
                    steps.Add("click:Water Button");
                    steps.Add("wait:1.0");
                    steps.Add("shot:watered");
                    break;

                case "thirsty":
                    steps.Add("click:Next Morning");
                    steps.Add("wait:1.6");
                    steps.Add("shot:thirsty-report");
                    steps.Add("click:Continue");
                    steps.Add("wait:0.6");
                    steps.Add("shot:thirsty-pot");
                    break;

                case "sprout":
                    steps.Add("click:Next Morning");
                    steps.Add("wait:1.6");
                    steps.Add("shot:grow-report");
                    steps.Add("click:Continue");
                    steps.Add("wait:0.8");
                    steps.Add("shot:sprout");
                    break;

                case "bloom":
                    for (int i = 0; i < 5; i++)
                    {
                        steps.Add("click:Water Button");
                        steps.Add("wait:0.9");
                        steps.Add("click:Next Morning");
                        steps.Add("wait:1.7");
                        steps.Add("click:Continue");
                        steps.Add("wait:0.7");
                    }

                    steps.Add("shot:bloom");
                    break;

                case "minigame":
                    steps.Add("click:Play Button");
                    steps.Add("wait:1.0");
                    steps.Add("shot:minigame");
                    break;

                case "wrong":
                    steps.Add("click:Play Button");
                    steps.Add("wait:1.0");
                    steps.Add("option:0");
                    steps.Add("wait:0.6");
                    steps.Add("shot:minigame-feedback");
                    break;

                case "reward":
                    steps.Add("click:Play Button");
                    steps.Add("wait:1.0");
                    for (int i = 0; i < 5; i++)
                    {
                        steps.Add("option:0");
                        steps.Add("wait:1.2");
                    }

                    steps.Add("shot:reward");
                    break;

                case "settings":
                    steps.Add("click:Settings");
                    steps.Add("wait:0.9");
                    steps.Add("shot:settings");
                    break;

                case "liveupdate":
                    // Waits out two polls: the first records the revision the server is serving, the
                    // second notices the push. Whatever pushes runs outside the editor, on the wall clock,
                    // which is the point - the game is not told to reload, it discovers the change.
                    steps.Add("wait:11");
                    steps.Add("wait:12");
                    steps.Add("shot:liveupdate");
                    break;

                default:
                    Debug.LogWarning($"[SmallPot] Unknown scenario '{scenario}'; capturing the start state.");
                    steps.Add("shot:start");
                    break;
            }

            return steps;
        }

        private static void Click(string name)
        {
            GameObject target = GameObject.Find(name);
            if (target == null)
            {
                Debug.LogWarning($"[SmallPot] Capture could not find '{name}'.");
                return;
            }

            Button button = target.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning($"[SmallPot] '{name}' has no button.");
                return;
            }

            if (!button.interactable)
            {
                Debug.LogWarning($"[SmallPot] '{name}' is not interactable; the click was skipped.");
                return;
            }

            button.onClick.Invoke();
        }

        private static void ClickOption(int index)
        {
            Button[] buttons = UnityEngine.Object.FindObjectsOfType<Button>();
            var options = new List<Button>();
            foreach (Button button in buttons)
            {
                if (button.gameObject.activeInHierarchy && button.name.StartsWith("Option ", StringComparison.Ordinal))
                {
                    options.Add(button);
                }
            }

            options.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (index < 0 || index >= options.Count)
            {
                Debug.LogWarning($"[SmallPot] No option button at index {index} ({options.Count} active).");
                return;
            }

            options[index].onClick.Invoke();
        }

        private static void Capture(string label)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[SmallPot] No main camera to capture.");
                return;
            }

            const int width = 720;
            const int height = 1280;

            // Overlay canvases draw to the screen, not through a camera, so they have to be switched to
            // camera space for the duration of an off-screen render.
            var canvases = new List<Canvas>();
            var previousModes = new List<RenderMode>();
            var previousCameras = new List<Camera>();
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                canvases.Add(canvas);
                previousModes.Add(canvas.renderMode);
                previousCameras.Add(canvas.worldCamera);
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1f;
                }
            }

            RenderTexture previous = camera.targetTexture;

            // sRGB matters: in Linear colour space an sRGB-less target stores linear values, and reading
            // those back produces a screenshot that is much darker than what the device shows.
            RenderTexture target = RenderTexture.GetTemporary(
                width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            camera.targetTexture = previous;
            RenderTexture.ReleaseTemporary(target);

            for (int i = 0; i < canvases.Count; i++)
            {
                canvases[i].renderMode = previousModes[i];
                canvases[i].worldCamera = previousCameras[i];
            }

            string path = Path.Combine(_outputDirectory, $"{label}.png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            Debug.Log($"[SmallPot] Captured {label} to {path}");
        }

        /// <summary>
        /// Writes a save file so a scenario can start in a state that would otherwise take a minute of
        /// play to reach. This uses the game's own save format, so it also exercises loading.
        /// </summary>
        private static void PrepareSave(string scenario)
        {
            SaveStore.Delete();
            if (scenario == "start" || scenario == "minigame" || scenario == "wrong" || scenario == "reward" || scenario == "settings")
            {
                return;
            }

            var state = new SaveState();
            state.Plant.Drops = 40;

            switch (scenario)
            {
                case "watered":
                    break;

                case "thirsty":
                    // Nothing watered, so the first morning makes the pot thirsty.
                    break;

                case "sprout":
                    state.Plant.Watered = true;
                    break;

                case "bloom":
                    break;

                default:
                    return;
            }

            if (!SaveStore.Save(state, out string error))
            {
                Debug.LogWarning($"[SmallPot] Could not prepare the save for '{scenario}': {error}");
            }
        }

        private static float ParseFloat(string text) =>
            float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : 0.5f;
    }
}
