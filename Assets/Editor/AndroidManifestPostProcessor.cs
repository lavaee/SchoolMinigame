using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace SmallPot.EditorTools
{
    /// <summary>
    /// Adds <c>android:usesCleartextTraffic="true"</c> to the manifest Unity generates for the Gradle
    /// project, and nothing else.
    ///
    /// This is done as a post-process rather than by shipping a custom main manifest on purpose.
    /// A file at Assets/Plugins/Android/AndroidManifest.xml *replaces* Unity's template instead of
    /// merging with it, which silently removes the launcher activity, the Unity meta-data and every
    /// permission Unity normally contributes: the build still succeeds and produces an APK that cannot
    /// be started. Patching the generated file keeps Unity's manifest intact.
    ///
    /// Plain HTTP is only needed for the optional live content server: Android 9 (API 28) and later
    /// block cleartext traffic by default, so without this the phone cannot reach
    /// http://&lt;computer&gt;:8787. The game needs no network at all, and the bundled and cached data
    /// layers always work offline.
    /// </summary>
    public sealed class AndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        private const string Attribute = "android:usesCleartextTraffic";

        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[SmallPot] No AndroidManifest.xml was found at {manifestPath}.");
                return;
            }

            string manifest = File.ReadAllText(manifestPath);
            if (manifest.Contains(Attribute))
            {
                return;
            }

            Match application = Regex.Match(manifest, "<application\\b[^>]*>");
            if (!application.Success)
            {
                Debug.LogWarning("[SmallPot] The generated manifest has no <application> tag to patch.");
                return;
            }

            string tag = application.Value;
            bool selfClosing = tag.EndsWith("/>");
            string patched = selfClosing
                ? tag.Substring(0, tag.Length - 2) + " " + Attribute + "=\"true\" />"
                : tag.Substring(0, tag.Length - 1) + " " + Attribute + "=\"true\">";

            File.WriteAllText(manifestPath, manifest.Replace(tag, patched));
            Debug.Log("[SmallPot] Enabled cleartext traffic in the generated Android manifest.");
        }
    }
}
