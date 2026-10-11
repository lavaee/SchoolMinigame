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
        [Header("Assets")]
        [Tooltip("Generated Persian font asset. Assigned by the project setup.")]
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
            UiKit.Font = FontAsset;
            MeshFactory.SpriteMaterial = SpriteMaterial;

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

    }
}
