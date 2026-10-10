using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Handles to every part of the pot that changes when the plant does.
    ///
    /// This is a component rather than a plain object so that each reference is visible and assignable
    /// in the inspector. The stand-in geometry is generated once by the scene baker; from then on the
    /// scene is the source of truth, so swapping the pot, the soil, the stem or the leaves for authored
    /// art is a matter of dragging the new object into the matching slot - no code change, no rebuild.
    ///
    /// The size fields exist because authored art arrives at its own scale. <see cref="StemUnitHeight"/>
    /// is how tall your stem mesh is at scale 1, which is what lets the growth fractions in config.json
    /// keep meaning "a fraction of a fully grown plant" no matter what the mesh measures.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PotRig : MonoBehaviour
    {
        [Header("Structure")]
        [Tooltip("Top surface of the soil. Everything above it is the plant.")]
        public Transform PlantAnchor;

        [Tooltip("Container the plant view positions its stem in. Created under the anchor when missing.")]
        public Transform PlantRoot;

        [Tooltip("The stem. Scaled vertically to show growth.")]
        public Transform Stem;

        [Tooltip("The seed casing, shown only while the plant is still a seed.")]
        public Transform SeedShell;

        [Tooltip("Parent of the leaves. Created under the plant root when missing.")]
        public Transform FoliageRoot;

        [Tooltip("The blossom root, shown only on a flowering stage. Parent of the petals.")]
        public Transform Flower;

        [Tooltip("Leaf objects, in order: pair 0 left, pair 0 right, pair 1 left, and so on.")]
        public Transform[] Leaves = new Transform[0];

        [Tooltip("Shown while the pot has been watered and is waiting for the morning.")]
        public Transform WaterBadge;

        [Tooltip("Shown while the pot is thirsty.")]
        public Transform ThirstBadge;

        [Header("Materials")]
        public MeshRenderer Soil;

        [Tooltip("Your own soil material. Left empty, the baked stand-in stays. The wet-to-dry tint is applied over whichever material ends up on the renderer.")]
        public Material SoilMaterial;

        [Header("Sizes")]
        [Tooltip("World height above the soil that a fully grown plant reaches.")]
        public float StemMaxHeight = 1.05f;

        [Tooltip("Height of your stem mesh at scale 1, in world units.")]
        public float StemUnitHeight = 1f;

        [Tooltip("Scale applied to a leaf object at full size.")]
        public Vector3 LeafUnitScale = new Vector3(0.26f, 0.05f, 0.42f);

        [Tooltip("Scale applied to the seed casing.")]
        public Vector3 SeedUnitScale = new Vector3(0.15f, 0.10f, 0.19f);

        public float BadgeScale = 0.30f;

        [Tooltip("How many leaf pairs the plant may show. Stages asking for more are clamped to this.")]
        public int MaxLeafPairs = 3;

        [Header("Behaviour")]
        [Tooltip("Build any missing part at startup. On for an empty scene and while baking; off once the scene is authored, so nothing reappears after you delete or replace it.")]
        public bool CreateMissingParts;

        [Tooltip("Recolour the leaves and the stem to signal thirst. Turn off when your own art carries its own colours.")]
        public bool ColorPlantParts = true;

        /// <summary>Assigns the camera the badges face; found automatically when left empty.</summary>
        [Tooltip("Camera the badges and the blossom face. Found automatically when left empty.")]
        public Camera Camera;
    }
}
