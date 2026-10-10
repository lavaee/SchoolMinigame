using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SmallPot.EditorTools
{
    /// <summary>
    /// Turns the stand-in generator into scene content.
    ///
    /// The game used to build its world at startup. That made "replace the data and the game changes"
    /// true for the visuals, but it also meant there was nothing to edit: no model to swap, no sound to
    /// attach, no widget to re-skin. So the generator now runs once, here, and what it produces is baked
    /// into the scene as ordinary objects -
    ///
    ///   * generated materials become .mat assets, because a material created in code cannot be saved
    ///     into a scene and would come back as a blank white surface;
    ///   * the tapered cylinder becomes a .asset mesh;
    ///   * every generated sprite becomes a PNG under Resources, because an Image can only serialise a
    ///     reference to an asset, and this is also what lets you replace one icon with your own drawing;
    ///
    /// and afterwards the PotRig, the views and the bootstrap all bind to what is in the scene instead of
    /// creating it. Running the builder again is still possible, deliberately, from the menu.
    /// </summary>
    public static class SceneBaker
    {
        private const string SpriteFolder = "Assets/Resources/SmallPotSprites";
        private const string MaterialFolder = "Assets/Materials/Generated";
        private const string MeshFolder = "Assets/Meshes/Generated";

        private const string WorldObject = "World";
        private const string CanvasObject = "UI";
        private const string CameraObject = "Main Camera";
        private const string EventSystemObject = "EventSystem";

        [MenuItem("Small Pot/Rebuild Stand-in Scene", priority = 20)]
        public static void RebuildFromMenu()
        {
            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap == null)
            {
                EditorUtility.DisplayDialog(
                    "Small Pot",
                    "Open the game scene first. This rebuilds the generated parts inside it.",
                    "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Rebuild the stand-in",
                "This replaces the camera, the lighting, the World and the interface that the generator "
                + "made, and bakes fresh sprite, material and mesh assets.\n\n"
                + "Everything you added elsewhere in the scene is kept. Continue?",
                "Rebuild",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            BuildIntoScene(bootstrap, save: true);
        }

        [MenuItem("Small Pot/Bake Generated Art to Assets", priority = 21)]
        public static void BakeArtFromMenu()
        {
            GameBootstrap bootstrap = Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap == null)
            {
                EditorUtility.DisplayDialog("Small Pot", "Open the game scene first.", "OK");
                return;
            }

            // No rebuild: this only writes the art that is already in the scene out to assets and points
            // the existing components at it, which is what you want after editing the generator by hand.
            int sprites = BakeSprites();
            int materials = BakeMaterials();
            int meshes = BakeMeshes();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[SmallPot] Baked {sprites} sprite(s), {materials} material(s) and {meshes} mesh(es).");
        }

        /// <summary>
        /// Generates the stand-in world and interface into the scene the bootstrap lives in.
        /// <paramref name="save"/> is false when the caller saves the scene itself.
        /// </summary>
        public static void BuildIntoScene(GameBootstrap bootstrap, bool save)
        {
            DataBundle bundle = DataLoader.LoadBundledNow();

            RemoveGenerated(bootstrap);

            Camera camera = SceneKit.CreateCamera(null);
            camera.gameObject.name = CameraObject;
            camera.gameObject.tag = "MainCamera";
            if (camera.GetComponent<AudioListener>() == null)
            {
                // Without a listener the clips the user adds later would be silent for no visible reason.
                camera.gameObject.AddComponent<AudioListener>();
            }

            SceneKit.CreateLighting(null);

            var world = new GameObject(WorldObject);
            SceneKit.CreateEnvironment(world.transform);
            PotRig rig = SceneKit.CreatePot(world.transform);
            rig.Camera = camera;
            rig.CreateMissingParts = true;

            AudioDirector audio = bootstrap.GetComponent<AudioDirector>();
            if (audio == null)
            {
                audio = bootstrap.gameObject.AddComponent<AudioDirector>();
            }

            // The plant is created by the same view the game uses, so what is baked is exactly what the
            // runtime expects to find - names, sizes and all.
            PotView potView = bootstrap.GetComponent<PotView>();
            if (potView == null)
            {
                potView = bootstrap.gameObject.AddComponent<PotView>();
            }

            potView.Initialize(rig, camera, new MotionPrefs());

            // Apply the richest stage first, so every leaf and the blossom exist even though the scene is
            // left showing a seed. Stages that come later only toggle objects that already exist.
            potView.Apply(PotVisual.Resolve(bundle.Config, FullyGrown(bundle.Config)), bundle.Config, false);

            GameController controller = bootstrap.GetComponent<GameController>();
            if (controller == null)
            {
                controller = bootstrap.gameObject.AddComponent<GameController>();
            }

            controller.Initialize(camera, potView, audio);
            controller.BuildInterfaceFor(bundle);
            UiKit.EnsureEventSystem(bootstrap.transform);

            int sprites = BakeSprites();
            int materials = BakeMaterials();
            int meshes = BakeMeshes();

            // Back to the opening state: the scene should look like a new game when it is opened.
            potView.Apply(PotVisual.Resolve(bundle.Config, PlantState.NewGame(bundle.Config)), bundle.Config, false);

            // Off, so a part that is deleted or replaced later stays that way.
            rig.CreateMissingParts = false;

            bootstrap.PotRig = rig;
            bootstrap.Camera = camera;
            bootstrap.Audio = audio;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (save)
            {
                EditorSceneManager.SaveOpenScenes();
                Debug.Log($"[SmallPot] Stand-in rebuilt and saved. Baked {sprites} sprite(s), "
                    + $"{materials} material(s) and {meshes} mesh(es).");
            }
        }

        /// <summary>A state at the last stage, used to make sure every part is created.</summary>
        private static PlantState FullyGrown(GameConfig config)
        {
            return new PlantState
            {
                Drops = config.StartDrops,
                GrowthIndex = Mathf.Max(0, config.Growth.Stages.Count - 1)
            };
        }

        /// <summary>
        /// Removes what the generator owns, by name. Everything outside this short list is left alone, so
        /// a rebuild cannot take your own objects with it.
        /// </summary>
        private static void RemoveGenerated(GameBootstrap bootstrap)
        {
            var doomed = new List<GameObject>();

            foreach (string name in new[] { WorldObject, CanvasObject, CameraObject, EventSystemObject })
            {
                foreach (GameObject candidate in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (candidate.name == name)
                    {
                        doomed.Add(candidate);
                    }
                }
            }

            foreach (GameObject candidate in doomed)
            {
                if (candidate != bootstrap.gameObject)
                {
                    Object.DestroyImmediate(candidate);
                }
            }
        }

        // ---- Baking generated art into real assets ------------------------------------------------

        /// <summary>
        /// Writes every generated sprite out as a PNG and points the images at the imported asset.
        ///
        /// The nine-slice border and the pivot come from the sprite that was generated, so the imported
        /// asset looks identical to what the code drew. Replacing a PNG with your own art is then a
        /// drop-in: same file name, same border, your drawing.
        /// </summary>
        private static int BakeSprites()
        {
            // The keys are the ones TextureFactory caches under, so the baked PNG and the generated
            // sprite are the same picture. TextureFactory loads them from Resources before generating.
            var keyed = new Dictionary<string, Sprite>
            {
                { "solid", TextureFactory.Solid() },
                { "round-64-10", TextureFactory.RoundedRect(radius: 10) },
                { "round-64-16", TextureFactory.RoundedRect(radius: 16) },
                { "round-64-18", TextureFactory.RoundedRect() },
                { "round-64-22", TextureFactory.RoundedRect(radius: 22) },
                { "round-64-24", TextureFactory.RoundedRect(radius: 24) },
                { "round-64-30", TextureFactory.RoundedRect(radius: 30) },
                { "round-64-32", TextureFactory.RoundedRect(radius: 32) },
                { "round-64-36", TextureFactory.RoundedRect(radius: 36) },
                { "circle-128", TextureFactory.Circle() },
                { "droplet-128", TextureFactory.Droplet() },
                { "droplet-slash-128", TextureFactory.DropletSlash() },
                { "leaf-128", TextureFactory.Leaf() },
                { "blossom-128", TextureFactory.Blossom() },
                { "check-128", TextureFactory.Check() },
                { "cross-128", TextureFactory.Cross() },
                { "gear-128", TextureFactory.Gear() },
                { "chevron-up-128", TextureFactory.ChevronUp() },
                { "play-128", TextureFactory.Play() },
                { "refresh-128", TextureFactory.Refresh() },
                { "shadow-128", TextureFactory.SoftShadow() }
            };

            Directory.CreateDirectory(SpriteFolder);

            var replacements = new Dictionary<Sprite, Sprite>();
            int written = 0;

            foreach (KeyValuePair<string, Sprite> entry in keyed)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                Sprite baked = WriteSpriteAsset(entry.Key, entry.Value);
                if (baked != null)
                {
                    replacements[entry.Value] = baked;
                    written++;
                }
            }

            AssetDatabase.Refresh();

            foreach (Image image in InScene<Image>())
            {
                if (image.sprite != null && replacements.TryGetValue(image.sprite, out Sprite baked))
                {
                    image.sprite = baked;
                }
            }

            foreach (SpriteRenderer renderer in InScene<SpriteRenderer>())
            {
                if (renderer.sprite != null && replacements.TryGetValue(renderer.sprite, out Sprite baked))
                {
                    renderer.sprite = baked;
                }
            }

            return written;
        }

        private static Sprite WriteSpriteAsset(string key, Sprite source)
        {
            string path = $"{SpriteFolder}/{key}.png";

            Texture2D texture = source.texture;
            if (texture == null)
            {
                return null;
            }

            // Readable copies only: a sprite whose texture cannot be read cannot be encoded.
            Texture2D readable = texture;
            if (!texture.isReadable)
            {
                RenderTexture temporary = RenderTexture.GetTemporary(
                    texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(texture, temporary);

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = temporary;
                readable = new Texture2D(texture.width, texture.height, TextureFormat.ARGB32, false);
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readable.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            File.WriteAllBytes(path, readable.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;

                // Border, pivot and mesh type only exist on the settings object, not on the importer.
                // Copying them across is what makes the imported sprite identical to the generated one:
                // the nine-slice panels keep their border, and the off-centre icons keep their pivot.
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteBorder = source.border;
                settings.spritePixelsPerUnit = source.pixelsPerUnit;

                if (source.rect.width > 0f && source.rect.height > 0f)
                {
                    settings.spriteAlignment = (int)SpriteAlignment.Custom;
                    settings.spritePivot = new Vector2(
                        source.pivot.x / source.rect.width,
                        source.pivot.y / source.rect.height);
                }

                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Writes every generated material out as a .mat asset and swaps it into the renderers.
        ///
        /// A material created in code carries HideAndDontSave, so it is not merely unsaved - it is
        /// unsaveable, and a scene that referenced it would come back white. This is the step that makes
        /// the scene's appearance real.
        /// </summary>
        private static int BakeMaterials()
        {
            Directory.CreateDirectory(MaterialFolder);

            var replacements = new Dictionary<Material, Material>();
            var used = new HashSet<string>();
            int written = 0;

            foreach (Renderer renderer in InScene<Renderer>())
            {
                Material source = renderer.sharedMaterial;
                if (source == null || replacements.ContainsKey(source))
                {
                    continue;
                }

                if (AssetDatabase.Contains(source))
                {
                    replacements[source] = source;
                    continue;
                }

                string name = MakeUnique(Sanitise(source.name), used);
                string path = $"{MaterialFolder}/{name}.mat";
                AssetDatabase.DeleteAsset(path);

                var copy = new Material(source)
                {
                    name = name,
                    hideFlags = HideFlags.None
                };

                AssetDatabase.CreateAsset(copy, path);
                replacements[source] = copy;
                written++;
            }

            AssetDatabase.Refresh();

            foreach (Renderer renderer in InScene<Renderer>())
            {
                Material source = renderer.sharedMaterial;
                if (source != null && replacements.TryGetValue(source, out Material baked) && baked != source)
                {
                    renderer.sharedMaterial = baked;
                }
            }

            return written;
        }

        /// <summary>
        /// Writes meshes that do not already live in an asset - the tapered pot and stem, in practice.
        /// The primitives the engine provides are left alone: they are shared, and duplicating them would
        /// only break batching.
        /// </summary>
        private static int BakeMeshes()
        {
            Directory.CreateDirectory(MeshFolder);

            var builtin = new HashSet<Mesh>();
            foreach (string primitive in new[] { "Sphere", "Capsule", "Cylinder", "Cube", "Plane", "Quad" })
            {
                Mesh mesh = Resources.GetBuiltinResource<Mesh>(primitive + ".fbx");
                if (mesh != null)
                {
                    builtin.Add(mesh);
                }
            }

            var replacements = new Dictionary<Mesh, Mesh>();
            var used = new HashSet<string>();
            int written = 0;

            foreach (MeshFilter filter in InScene<MeshFilter>())
            {
                Mesh source = filter.sharedMesh;
                if (source == null || builtin.Contains(source) || replacements.ContainsKey(source))
                {
                    continue;
                }

                if (AssetDatabase.Contains(source))
                {
                    replacements[source] = source;
                    continue;
                }

                string name = MakeUnique(Sanitise(source.name), used);
                string path = $"{MeshFolder}/{name}.asset";
                AssetDatabase.DeleteAsset(path);

                var copy = Object.Instantiate(source);
                copy.name = name;
                AssetDatabase.CreateAsset(copy, path);
                replacements[source] = copy;
                written++;
            }

            AssetDatabase.Refresh();

            foreach (MeshFilter filter in InScene<MeshFilter>())
            {
                Mesh source = filter.sharedMesh;
                if (source != null && replacements.TryGetValue(source, out Mesh baked) && baked != source)
                {
                    filter.sharedMesh = baked;
                }
            }

            return written;
        }

        /// <summary>Every component of one type in the open scene, across all of its root objects.</summary>
        private static IEnumerable<T> InScene<T>() where T : Component
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (T found in root.GetComponentsInChildren<T>(true))
                {
                    yield return found;
                }
            }
        }

        private static string MakeUnique(string name, HashSet<string> used)
        {
            string candidate = name;
            int suffix = 2;

            while (!used.Add(candidate))
            {
                candidate = $"{name} {suffix}";
                suffix++;
            }

            return candidate;
        }

        private static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Generated";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '-');
            }

            return name.Replace('/', '-').Trim();
        }
    }
}
