using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SmallPot.EditorTools
{
    /// <summary>
    /// Generates everything in the project that is derived rather than authored by hand:
    /// the Persian font assets, the single scene, the build settings and the Android player settings.
    ///
    /// Run it with Window > SmallPot > Generate Project, or headlessly:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod SmallPot.EditorTools.ProjectSetup.GenerateAll
    ///
    /// It is idempotent: running it again rebuilds the font assets and the scene from the current
    /// data files.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string PrimaryFontPath = "Assets/Fonts/Vazirmatn-Regular.ttf";
        public const string FallbackFontPath = "Assets/Fonts/Amiri-Regular.ttf";
        public const string PrimaryFontAssetPath = "Assets/Fonts/Vazirmatn SDF.asset";
        public const string FallbackFontAssetPath = "Assets/Fonts/Amiri SDF.asset";
        public const string ArabicRangesPath = "Assets/3rdPartyPackages/RTLTMPro/Ranges/ArabicAll.txt";
        public const string SpriteMaterialPath = "Assets/Materials/SpriteDefault.mat";

        // Sampling values chosen so the whole Persian set fits one 2048 atlas with room to spare,
        // while multi-atlas support keeps dynamic growth possible.
        private const int SamplingPointSize = 64;
        private const int AtlasPadding = 6;
        private const int AtlasWidth = 2048;
        private const int AtlasHeight = 2048;

        [MenuItem("Window/SmallPot/Generate Project")]
        public static void GenerateAll()
        {
            BuildScript.ConfigureAndroid();
            TMP_FontAsset primary = GenerateFontAssets();
            Material spriteMaterial = EnsureSpriteMaterial();
            CreateMainScene(primary, spriteMaterial);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SmallPot] Project generated: font assets, Main scene, build settings, Android settings.");
        }

        /// <summary>
        /// Creates the shared sprite material.
        ///
        /// Sprites are tinted per renderer, so one white material covers every sprite in the game. It
        /// exists as a saved asset rather than a runtime Shader.Find because a material referenced by the
        /// scene is what guarantees the sprite shader is not stripped from the build.
        /// </summary>
        public static Material EnsureSpriteMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[SmallPot] The Sprites/Default shader was not found; sprites will use Unity's default.");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(SpriteMaterialPath) ?? "Assets/Materials");
            var material = new Material(shader) { name = "SmallPot Sprite" };
            AssetDatabase.CreateAsset(material, SpriteMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        [MenuItem("Window/SmallPot/Rebuild Interface From Code")]
        public static void RebuildInterfaceFromCode()
        {
            // The interface is authored once and then belongs to the scene. This is the way back: it drops
            // the markers that say "already yours", so the next build lays the widgets out from the code
            // again - for when the code's layout itself has changed and the scene should follow it.
            int handed = 0;
            foreach (AuthoredWidget marker in UnityEngine.Object.FindObjectsOfType<AuthoredWidget>(true))
            {
                UnityEngine.Object.DestroyImmediate(marker);
                handed++;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log(
                $"[SmallPot] {handed} widget(s) handed back to the code. Save the scene, then rebuild the " +
                "interface to author their layout and their parameters again.");
        }

        [MenuItem("Window/SmallPot/Regenerate Font Assets Only")]
        public static void GenerateFontAssetsMenu() => GenerateFontAssets();

        /// <summary>
        /// Creates the dynamic Persian font asset and its fallback.
        ///
        /// Dynamic population is deliberate: the whole point of the data files is that they can be
        /// replaced without rebuilding the app, and a new word in strings.json must render even though
        /// no atlas was baked for it. Every character currently used is added up front anyway, so the
        /// common path never pays for a glyph lookup at runtime.
        /// </summary>
        public static TMP_FontAsset GenerateFontAssets()
        {
            TMP_FontAsset fallback = CreateFontAsset(FallbackFontPath, FallbackFontAssetPath, null);
            TMP_FontAsset primary = CreateFontAsset(PrimaryFontPath, PrimaryFontAssetPath, fallback);

            string characters = CollectRequiredCharacters();
            primary.TryAddCharacters(characters, out string missing, true);
            if (!string.IsNullOrEmpty(missing))
            {
                // Missing glyphs are not fatal: the atlas is dynamic, and Amiri is registered as a
                // fallback, so anything absent here is still drawn from the fallback font at runtime.
                Debug.LogWarning($"[SmallPot] {missing.Length} character(s) were not added to the primary " +
                                 $"font atlas and will be taken from the fallback font: {missing}");
            }

            EditorUtility.SetDirty(primary);
            if (fallback != null)
            {
                EditorUtility.SetDirty(fallback);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SmallPot] Persian font assets ready ({characters.Length} characters requested).");
            return primary;
        }

        private static TMP_FontAsset CreateFontAsset(string ttfPath, string assetPath, TMP_FontAsset fallback)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (source == null)
            {
                throw new FileNotFoundException(
                    $"Font not found at {ttfPath}. It must be imported as a Font asset (TrueType).");
            }

            // Replace any previous asset so a re-run does not accumulate stale atlas textures.
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                source,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasWidth,
                AtlasHeight,
                AtlasPopulationMode.Dynamic,
                true);

            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // The atlas texture and material must be saved as sub-assets of the font asset.
            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    Texture2D texture = fontAsset.atlasTextures[i];
                    if (texture == null)
                    {
                        continue;
                    }

                    texture.name = $"{fontAsset.name} Atlas {i}";
                    AssetDatabase.AddObjectToAsset(texture, fontAsset);
                }
            }

            if (fontAsset.material != null)
            {
                fontAsset.material.name = $"{fontAsset.name} Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            if (fallback != null)
            {
                fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            }

            EditorUtility.SetDirty(fontAsset);
            return fontAsset;
        }

        /// <summary>
        /// Every character the game can currently display: the RTLTMPro Arabic ranges, printable
        /// ASCII, the Persian punctuation the supplied strings use, and every character that actually
        /// appears in the data files.
        /// </summary>
        public static string CollectRequiredCharacters()
        {
            var characters = new SortedSet<int>();

            void AddRange(int from, int to)
            {
                for (int c = from; c <= to; c++)
                {
                    characters.Add(c);
                }
            }

            // Printable ASCII: identifiers such as file names and server URLs are shown in settings.
            AddRange(0x20, 0x7E);

            // Persian guillemets, used by the supplied strings, and the zero width no-joiner.
            characters.Add(0x00AB);
            characters.Add(0x00BB);
            characters.Add(0x200C);
            characters.Add(0x200F);
            characters.Add(0x2022);

            // Persian digits, in case a data file supplies its own.
            AddRange(0x06F0, 0x06F9);

            foreach (int codepoint in ParseRanges(LoadArabicRanges()))
            {
                characters.Add(codepoint);
            }

            foreach (string text in CollectDataStrings())
            {
                foreach (char c in text)
                {
                    characters.Add(c);
                }
            }

            var builder = new StringBuilder(characters.Count);
            foreach (int codepoint in characters)
            {
                if (codepoint <= 0xFFFF)
                {
                    builder.Append((char)codepoint);
                }
            }

            return builder.ToString();
        }

        private static string LoadArabicRanges()
        {
            if (!File.Exists(ArabicRangesPath))
            {
                Debug.LogWarning($"[SmallPot] {ArabicRangesPath} was not found; only the characters found in " +
                                 "the data files will be added to the font atlas.");
                return string.Empty;
            }

            return File.ReadAllText(ArabicRangesPath, Encoding.UTF8);
        }

        /// <summary>Expands RTLTMPro's "060C,0621-0627,FE70-FEFF" notation into codepoints.</summary>
        private static IEnumerable<int> ParseRanges(string specification)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(specification))
            {
                return result;
            }

            foreach (string rawToken in specification.Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = rawToken.Trim().TrimStart('\uFEFF');
                if (token.Length == 0 || token.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int dash = token.IndexOf('-');
                if (dash > 0)
                {
                    string fromText = token.Substring(0, dash).Trim();
                    string toText = token.Substring(dash + 1).Trim();
                    if (TryParseHex(fromText, out int from) && TryParseHex(toText, out int to) && to >= from && to - from < 0x1000)
                    {
                        for (int c = from; c <= to; c++)
                        {
                            result.Add(c);
                        }
                    }

                    continue;
                }

                if (TryParseHex(token, out int single))
                {
                    result.Add(single);
                }
            }

            return result;
        }

        private static bool TryParseHex(string text, out int value) =>
            int.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);

        /// <summary>All string values from the authoritative repo-root data files.</summary>
        private static IEnumerable<string> CollectDataStrings()
        {
            var result = new List<string>();
            string dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
            if (!Directory.Exists(dataDirectory))
            {
                return result;
            }

            foreach (string file in Directory.GetFiles(dataDirectory, "*.json"))
            {
                if (!JsonValue.TryParse(File.ReadAllText(file, Encoding.UTF8), out JsonValue root, out string error))
                {
                    Debug.LogWarning($"[SmallPot] {Path.GetFileName(file)} could not be parsed while collecting " +
                                     $"font characters: {error}");
                    continue;
                }

                CollectStrings(root, result);
            }

            return result;
        }

        private static void CollectStrings(JsonValue value, List<string> output)
        {
            if (value == null)
            {
                return;
            }

            if (value.IsString)
            {
                string text = value.AsString();
                if (!string.IsNullOrEmpty(text))
                {
                    output.Add(text);
                }

                return;
            }

            if (value.IsArray)
            {
                foreach (JsonValue item in value.Items)
                {
                    CollectStrings(item, output);
                }

                return;
            }

            if (value.IsObject)
            {
                foreach (string key in value.Keys)
                {
                    CollectStrings(value[key], output);
                }
            }
        }

        /// <summary>
        /// Builds the one scene the game needs.
        ///
        /// It contains the camera, the lighting, the window and pot geometry, and the whole interface as
        /// ordinary objects, generated once by the scene baker and editable from then on. The generator
        /// stays behind a menu item for a clean slate, but the shipped scene is authored content: a sixth
        /// pot state needs a stage in config.json and a string, and a new model or a sound is an
        /// inspector job.
        /// </summary>
        public static void CreateMainScene(TMP_FontAsset primaryFont, Material spriteMaterial)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var app = new GameObject("App");
            GameBootstrap bootstrap = app.AddComponent<GameBootstrap>();
            bootstrap.FontAsset = primaryFont;
            bootstrap.SpriteMaterial = spriteMaterial;

            // save: false, because the scene is written a few lines below.
            SceneBaker.BuildIntoScene(bootstrap, save: false);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
            Debug.Log($"[SmallPot] Scene written to {ScenePath}.");
        }
    }
}
