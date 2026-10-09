using System;
using System.IO;
using System.Linq;
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

        public static void SetAndroidBuildTarget()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }
    }
}
