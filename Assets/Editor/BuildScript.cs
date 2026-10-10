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
            PlayerSettings.Android.bundleVersionCode = 1;

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
            text.AppendLine("  \"showDataSourceInfo\": true,");
            text.AppendLine("  \"showDebugPanel\": true");
            text.AppendLine("}");

            string path = Path.Combine(Application.streamingAssetsPath, "data", "dev.json");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            AssetDatabase.Refresh();

            Debug.Log(urls.Length > 0
                ? $"[SmallPot] The app will try: {string.Join(", ", urls)}"
                : "[SmallPot] No data server baked; the app runs from the bundled files only.");
        }

        public static void SetAndroidBuildTarget()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }
    }
}
