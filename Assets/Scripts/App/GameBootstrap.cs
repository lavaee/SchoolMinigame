using TMPro;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// The scene's only component, and the composition root.
    ///
    /// The scene deliberately contains nothing but this object: the camera, the lighting, the
    /// windowsill, the pot and the entire interface are built from code at startup. That is what makes
    /// the data files authoritative for the visuals as well as for the text - adding a sixth stage to
    /// config.json changes what the pot looks like, with nothing to re-author in the editor.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        /// <summary>Generated Persian font asset, assigned when the project is generated.</summary>
        public TMP_FontAsset FontAsset;

        /// <summary>Shared sprite material, so the sprite shader is guaranteed to be in the build.</summary>
        public Material SpriteMaterial;

        public bool BuildOnStart = true;

        private void Awake()
        {
            if (BuildOnStart)
            {
                Build();
            }
        }

        public void Build()
        {
            UiKit.Font = FontAsset;
            MeshFactory.SpriteMaterial = SpriteMaterial;

            Camera camera = SceneKit.CreateCamera(null);
            SceneKit.CreateLighting(null);

            var world = new GameObject("World");
            SceneKit.CreateEnvironment(world.transform);
            PotRig rig = SceneKit.CreatePot(world.transform);

            var controller = gameObject.AddComponent<GameController>();

            var potView = gameObject.AddComponent<PotView>();
            potView.Initialize(rig, camera, controller.Motion);

            controller.Initialize(camera, potView);
        }
    }
}
