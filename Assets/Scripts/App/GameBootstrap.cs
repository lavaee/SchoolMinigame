using TMPro;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// The composition root.
    ///
    /// Nothing is generated here. The camera, the lighting, the window, the pot and the whole interface
    /// live in the scene, created once by "Small Pot/Rebuild Stand-in Scene" and editable from then on,
    /// so replacing a model, a sprite or a sound is an inspector job rather than a code change. This
    /// script only finds the parts, wires them together and hands them to the controller.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        /// <summary>
        /// Where the generated font asset lives, relative to a Resources folder.
        ///
        /// Shared with the generator in the editor so the two cannot drift apart: that code writes the
        /// asset to this path, and this code loads it from here.
        /// </summary>
        public const string FontResourcePath = "Fonts/Lalezar SDF";

        [Header("Assets")]
        [Tooltip("Font from the scene. When this is empty, or still points at the older Persian font, the " +
                 "interface uses the Lalezar asset in Resources instead. A font assigned on purpose wins.")]
        public TMP_FontAsset FontAsset;

        [Tooltip("Shared sprite material, so the sprite shader is guaranteed to be in the build.")]
        public Material SpriteMaterial;

        [Header("Scene parts")]
        [Tooltip("The pot and its plant. Found in the scene when left empty.")]
        public PotRig PotRig;

        [Tooltip("The camera the game plays through. Falls back to the main camera.")]
        public Camera Camera;

        [Tooltip("Optional. Leave empty to run silently, which is the default until you add clips.")]
        public AudioDirector Audio;

        private void Awake()
        {
            Bind();
        }

        /// <summary>Wires the scene up and starts the game. Safe to call again from the editor.</summary>
        public void Bind()
        {
            UiKit.Font = ChooseFont();
            MeshFactory.SpriteMaterial = SpriteMaterial;

            // Only while playing: the scene is authored by hand, and an editor-time Bind that reassigned the
            // font on every label would quietly dirty it.
            if (Application.isPlaying)
            {
                ApplyFontToScene(UiKit.Font);
            }

            if (PotRig == null)
            {
                PotRig = FindObjectOfType<PotRig>();
            }

            if (Camera == null)
            {
                Camera = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
            }

            if (Audio == null)
            {
                Audio = FindObjectOfType<AudioDirector>();
            }

            // The ambience runs on an object of its own, with sources of its own, so an effect's pitch and a
            // bed's fade can never interfere. It starts on the general bed; the minigame asks for its own.
            //
            // Created only while playing: this method can also be called from the editor, and quietly adding
            // objects to somebody's scene is not something a bootstrap should do.
            if (Application.isPlaying)
            {
                MusicDirector music = FindObjectOfType<MusicDirector>();
                if (music == null)
                {
                    var holder = new GameObject("SmallPot Music");
                    holder.transform.SetParent(transform, false);
                    music = holder.AddComponent<MusicDirector>();
                }

                music.PlayBoard();
            }

            GameController controller = gameObject.GetComponent<GameController>();
            if (controller == null)
            {
                controller = gameObject.AddComponent<GameController>();
            }

            PotView potView = gameObject.GetComponent<PotView>();
            if (potView == null)
            {
                potView = gameObject.AddComponent<PotView>();
            }

            potView.Initialize(PotRig, Camera, controller.Motion);

            // The gate every animation consults, published once so a widget's own juice component does not
            // have to search the scene for the controller on every tap.
            Juice.Prefs = controller.Motion;

            controller.Initialize(Camera, potView, Audio);
        }

        /// <summary>
        /// The font the interface draws with.
        ///
        /// The scene holds a serialised reference written when the project was generated, and the scene is
        /// not this project's to rewrite. So the choice is made here instead. Lalezar wins when the scene
        /// has no font, or still points at the Persian font an earlier generation produced; a font somebody
        /// assigned on purpose is left exactly where it is, because silently replacing an author's choice
        /// would be the rudest thing a bootstrap could do. One line says which font won, so there is never
        /// a question about why the text looks the way it does.
        /// </summary>
        private TMP_FontAsset ChooseFont()
        {
            TMP_FontAsset lalezar = Resources.Load<TMP_FontAsset>(FontResourcePath);
            string assigned = FontAsset != null ? FontAsset.name : "none";

            if (lalezar == null)
            {
                Debug.Log($"[SmallPot] font: {assigned}. Lalezar is not in Resources yet - run " +
                          "\"Window/SmallPot/Generate Project\" once to create it.");
                return FontAsset;
            }

            if (FontAsset == null)
            {
                Debug.Log($"[SmallPot] font: {lalezar.name} (the scene had no font assigned).");
                return lalezar;
            }

            if (FontAsset == lalezar || !IsSupersededFont(FontAsset))
            {
                Debug.Log($"[SmallPot] font: {assigned} (assigned in the scene, so it wins).");
                return FontAsset;
            }

            Debug.Log($"[SmallPot] font: {lalezar.name} (the scene still points at the older {assigned}).");
            return lalezar;
        }

        /// <summary>
        /// True for the Persian font assets an earlier generation of this project produced.
        ///
        /// Matched on name rather than on path, because a built game has no AssetDatabase to ask and the
        /// name is the one thing that survives into the build.
        /// </summary>
        private static bool IsSupersededFont(TMP_FontAsset font)
        {
            string name = font != null && font.name != null ? font.name : string.Empty;
            return name.Contains("Vazirmatn") || name.Contains("Amiri");
        }

        /// <summary>
        /// Moves labels that are already in the scene onto the chosen font.
        ///
        /// The builders set a font only on the widgets they lay out themselves: a widget authored in the
        /// scene keeps the font it was given there, which is the right default and the reason this exists.
        /// The scene was generated with the older Persian font, so without this every label the scene owns
        /// would go on drawing in it. Labels on any other font are left alone.
        /// </summary>
        private static void ApplyFontToScene(TMP_FontAsset font)
        {
            if (font == null)
            {
                return;
            }

            TMP_Text[] labels = FindObjectsOfType<TMP_Text>(true);
            int moved = 0;

            for (int i = 0; i < labels.Length; i++)
            {
                TMP_FontAsset current = labels[i].font;
                if (current == null || IsSupersededFont(current))
                {
                    // The font alone is set. The string is never touched: RTLTMPro shapes when a string is
                    // assigned, and shaping one twice is what turns a word into glyphs that no longer read
                    // as words.
                    labels[i].font = font;
                    moved++;
                }
            }

            if (moved > 0)
            {
                Debug.Log($"[SmallPot] font: {moved} label(s) already in the scene moved to {font.name}.");
            }
        }
    }
}
