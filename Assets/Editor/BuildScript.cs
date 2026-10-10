using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SmallPot.EditorTools
{
    /// <summary>
    /// Headless build entry points.
    ///
    /// Everything the Android build needs is applied from code so the build is reproducible from a
    /// clean checkout, and so the settings that matter to the brief (Android 9 minimum, 64-bit, the
    /// portrait layout) are visible in one reviewable place instead of buried in ProjectSettings.
    /// </summary>
    public static class BuildScript
    {
        public const string BundleIdentifier = "com.smallpot.game";
        public const string ProductName = "Small Pot";
        public const string CompanyName = "SmallPot";
        public const string ApkFileName = "SmallPot.apk";

        /// <summary>Output folder for built players, relative to the project root.</summary>
        private const string BuildDirectoryName = "Builds";

        /// <summary>Entry point for a cheap "do the scripts compile" check.</summary>
        public static void CompileCheck()
        {
            Debug.Log("SMALLPOT_COMPILE_OK");
        }

        public static void ConfigureAndroid()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = "1.0.0";

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleIdentifier);
            // Bumped whenever the shipped data changes: the app drops its cached copies when the version
            // it was installed as differs from the one the cache was written by, so a new build starts
            // from its own files instead of whatever the previous install had saved.
            PlayerSettings.Android.bundleVersionCode = 2;

            // The brief requires Android 9 and above.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // ARM64 is required for modern devices and for Play; ARMv7 keeps older hardware working.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;

            // ARM64 requires IL2CPP: the Mono backend cannot produce a 64-bit Android player.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            // The data server is plain HTTP on the local network; Unity adds INTERNET when the code
            // uses UnityWebRequest, and this makes it explicit.
            PlayerSettings.Android.forceInternetPermission = true;

            // Plain HTTP has to be allowed explicitly. Unity's default refuses it, and the refusal is
            // invisible from the app's side: every request to the local server fails, the content never
            // refreshes, and nothing on screen says why. A server on a home network has no certificate to
            // trust, so this is the deliberate choice for this project rather than a shortcut around one.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            ApplyKeystoreFromEnvironment();

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Debugging;
        }

        /// <summary>
        /// Applies a signing keystore when one is supplied through the environment. Without it Unity
        /// signs with its debug keystore, which is fine for handing an APK to a reviewer but means
        /// every machine produces a differently signed build.
        /// </summary>
        private static void ApplyKeystoreFromEnvironment()
        {
            string keystorePath = Environment.GetEnvironmentVariable("SMALLPOT_KEYSTORE");
            string keystorePass = Environment.GetEnvironmentVariable("SMALLPOT_KEYSTORE_PASS");
            string alias = Environment.GetEnvironmentVariable("SMALLPOT_KEYALIAS");
            string aliasPass = Environment.GetEnvironmentVariable("SMALLPOT_KEYALIAS_PASS");

            if (string.IsNullOrEmpty(keystorePath) || !File.Exists(keystorePath))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.Log("[SmallPot] No keystore supplied; the build will be signed with Unity's debug keystore.");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePass ?? string.Empty;
            PlayerSettings.Android.keyaliasName = alias ?? "smallpot";
            PlayerSettings.Android.keyaliasPass = aliasPass ?? keystorePass ?? string.Empty;
            Debug.Log($"[SmallPot] Signing with {keystorePath} (alias '{alias}').");
        }

        public static void BuildAndroid()
        {
            ConfigureAndroid();

            // The APK must carry the employer's data files, so the bundled layer is refreshed first.
            SyncDataToStreamingAssets();

            // Then the addresses this particular build should try, which the sync above has just
            // overwritten with the committed default.
            ApplyDataServerUrls();

            string outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), BuildDirectoryName);
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, ApkFileName);

            var options = new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            if (options.scenes.Length == 0)
            {
                throw new InvalidOperationException(
                    "No scenes are enabled in Build Settings. Run SmallPot/Setup/Generate Project first.");
            }

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[SmallPot] Build succeeded: {outputPath} ({summary.totalSize / (1024 * 1024)} MB)");
                EditorApplication.Exit(0);
                return;
            }

            Debug.LogError($"[SmallPot] Build failed: {summary.result} with {summary.totalErrors} error(s).");
            EditorApplication.Exit(1);
        }

        private static string[] EnabledScenes() =>
            EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();

        /// <summary>
        /// Mirrors the authoritative repo-root data/ directory into Assets/StreamingAssets/data.
        ///
        /// The repo-root copy is the one the data server serves and the one the employer can replace;
        /// this copy is the offline floor inside the APK. Both come from the same files, so they cannot
        /// drift apart.
        /// </summary>
        public static void SyncDataToStreamingAssets()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string source = Path.Combine(projectRoot, "data");
            string destination = Path.Combine(Application.streamingAssetsPath, "data");

            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException($"Data directory not found: {source}");
            }

            Directory.CreateDirectory(destination);
            int copied = 0;
            foreach (string file in Directory.GetFiles(source, "*.json"))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
                copied++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[SmallPot] Synced {copied} data file(s) to Assets/StreamingAssets/data.");
        }

        /// <summary>
        /// Writes the data server addresses for this build into the dev.json that ships inside the APK.
        ///
        /// They come from the environment rather than from the project because they describe the machine
        /// doing the building: a phone reaches the server at that machine's address on the network, and at
        /// 127.0.0.1 when the port is forwarded over USB with "adb reverse". Both are written, so one APK
        /// works over Wi-Fi and over a cable. An unset variable leaves dev.json alone; a variable that is
        /// set but empty bakes no server at all, which is the bundled-only build.
        /// </summary>
        private static void ApplyDataServerUrls()
        {
            const string variable = "SMALLPOT_DATA_SERVER_URLS";
            string raw = Environment.GetEnvironmentVariable(variable);
            if (raw == null)
            {
                return;
            }

            string[] urls = raw
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(url => url.Trim().TrimEnd('/'))
                .Where(url => url.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Everything that is not an address comes from the supplied file, so those flags are decided
            // in one place and the build overrides only what it has to.
            DevSettings supplied = ReadSuppliedDevSettings();

            var text = new StringBuilder();
            text.AppendLine("{");
            text.AppendLine("  \"version\": 1,");
            text.AppendLine($"  \"dataServerUrl\": \"{(urls.Length > 0 ? urls[0] : string.Empty)}\",");
            text.AppendLine("  \"dataServerUrls\": [");
            for (int i = 0; i < urls.Length; i++)
            {
                text.Append("    \"").Append(urls[i]).Append('"');
                text.AppendLine(i < urls.Length - 1 ? "," : string.Empty);
            }

            text.AppendLine("  ],");
            text.AppendLine($"  \"showDataSourceInfo\": {(supplied.ShowDataSourceInfo ? "true" : "false")},");
            text.AppendLine($"  \"showDebugPanel\": {(supplied.ShowDebugPanel ? "true" : "false")},");
            text.AppendLine($"  \"requireServer\": {(supplied.RequireServer ? "true" : "false")},");
            text.AppendLine(
                $"  \"connectionCheckSeconds\": {supplied.ConnectionCheckSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},");
            text.AppendLine(
                $"  \"updateWatchHoldSeconds\": {supplied.UpdateWatchHoldSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
            text.AppendLine("}");

            string path = Path.Combine(Application.streamingAssetsPath, "data", "dev.json");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            AssetDatabase.Refresh();

            Debug.Log(urls.Length > 0
                ? $"[SmallPot] The app will try: {string.Join(", ", urls)}"
                : "[SmallPot] No data server baked; the app runs from the bundled files only.");
        }

        /// <summary>Reads the supplied dev.json so the build inherits its flags rather than inventing them.</summary>
        private static DevSettings ReadSuppliedDevSettings()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "data", DevSettings.FileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SmallPot] data/{DevSettings.FileName} was not found; build defaults are used.");
                return new DevSettings();
            }

            if (JsonValue.TryParse(File.ReadAllText(path, Encoding.UTF8), out JsonValue root, out string error))
            {
                return DevSettings.FromJson(root);
            }

            Debug.LogWarning($"[SmallPot] data/{DevSettings.FileName} could not be read ({error}); defaults are used.");
            return new DevSettings();
        }

        public static void SetAndroidBuildTarget()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }
    }
}
