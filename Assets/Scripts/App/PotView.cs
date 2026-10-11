using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Draws the plant.
    ///
    /// This view is a renderer and nothing else: it is handed a <see cref="PotVisual"/> that core code
    /// already resolved from the data files, and it makes geometry match. It holds no opinion about how
    /// the game works, which is why every stage it can draw - including a stage that did not exist when
    /// this file was written - comes out of the same few numbers.
    ///
    /// Two channels carry the state without relying on colour: posture (the leaves visibly droop when
    /// the pot is thirsty) and badges (a droplet when watered, a struck-through droplet when thirsty).
    /// </summary>
    public sealed class PotView : MonoBehaviour
    {
        private struct LeafPart
        {
            public Transform Transform;
            public int PairIndex;
            public float Fraction;
            public float Angle;
            public float Size;
        }

        private const float MinimumStemHeight = 0.06f;
        private const float DroopDegrees = 40f;

        /// <summary>Used only when the rig is missing altogether, so a broken scene still says something.</summary>
        private const float FallbackMaxStemHeight = 1.05f;

        private PotRig _rig;
        private Camera _camera;
        private MotionPrefs _motion;

        private Transform _plant;
        private Transform _stem;
        private Transform _seedShell;
        private Transform _foliage;
        private readonly List<LeafPart> _leaves = new List<LeafPart>();
        private Transform _flowerRoot;
        private Transform _waterBadge;
        private Transform _thirstBadge;
        private MeshRenderer _soil;

        /// <summary>Tint applied through a property block so the material asset is never written to.</summary>
        private MaterialPropertyBlock _soilBlock;

        /// <summary>Parts already reported missing, so the warning is printed once instead of every frame.</summary>
        private readonly HashSet<string> _reported = new HashSet<string>();

        private float _height = MinimumStemHeight;
        private float _tilt;
        private float _appear = 1f;
        private bool _droop;
        private int _builtLeafPairs = -1;
        private bool _builtFlower;
        private Coroutine _routine;

        /// <summary>
        /// The plant root's authored scale, captured rather than assumed.
        ///
        /// The growth squash and the resting breath both animate this one value, so it is also the value
        /// they have to be able to put back: a plant that grew a fraction of a percent every morning would
        /// be a bug nobody notices until the tenth.
        /// </summary>
        private Vector3 _plantHome = Vector3.one;

        /// <summary>The foliage container's authored rotation, for the same reason.</summary>
        private Quaternion _foliageHome = Quaternion.identity;

        /// <summary>Whether the plant is currently thriving, so the breath is started once, not every frame.</summary>
        private bool _resting;

        public void Initialize(PotRig rig, Camera camera, MotionPrefs motion)
        {
            _rig = rig;
            _motion = motion;

            if (rig == null)
            {
                Debug.LogWarning(
                    "[SmallPot] PotView has no PotRig, so the plant cannot be drawn. " +
                    "Run \"Small Pot/Rebuild Stand-in Scene\", or assign one in the inspector.");
                return;
            }

            _camera = camera != null ? camera : rig.Camera;
            _soil = rig.Soil;
            _soilBlock = new MaterialPropertyBlock();

            // An empty slot means "keep the stand-in material". Assigning one is how you bring your own
            // soil surface in, and the wet/dry tint is still applied on top of whatever is here, so
            // thirst keeps reading on your art as well as ours.
            if (_soil != null && rig.SoilMaterial != null)
            {
                _soil.sharedMaterial = rig.SoilMaterial;
            }

            _plant = EnsureContainer(ref rig.PlantRoot, rig.PlantAnchor, "Plant");
            if (_plant == null)
            {
                Debug.LogWarning("[SmallPot] The pot has no plant anchor, so the plant cannot be drawn.");
                return;
            }

            _stem = EnsurePart(ref rig.Stem, _plant, "Stem", () => MeshFactory.MeshObject(
                "Stem",
                _plant,
                MeshFactory.TaperedCylinder(0.016f, 0.026f, 1f, 10),
                MeshFactory.Lit("stem", Palette.Stem)).transform);

            _seedShell = EnsurePart(ref rig.SeedShell, _plant, "Seed Shell", () =>
            {
                GameObject shell = MeshFactory.Primitive(
                    PrimitiveType.Sphere, "Seed Shell", _plant, MeshFactory.Lit("seed", Palette.Seed));
                shell.transform.SetLocalTRS(
                    new Vector3(0.01f, 0.035f, 0f),
                    Quaternion.Euler(0f, 24f, 14f),
                    rig.SeedUnitScale);
                return shell.transform;
            });

            _foliage = EnsureContainer(ref rig.FoliageRoot, _plant, "Foliage");

            // Captured once, before anything animates them, so both can always be returned to where the
            // scene's author put them.
            _plantHome = _plant.localScale;
            _foliageHome = _foliage != null ? _foliage.localRotation : Quaternion.identity;

            _waterBadge = EnsurePart(ref rig.WaterBadge, _plant, "Watered Badge", () => Badge(
                "Watered Badge", TextureFactory.Droplet(), Palette.Water));
            _thirstBadge = EnsurePart(ref rig.ThirstBadge, _plant, "Thirsty Badge", () => Badge(
                "Thirsty Badge", TextureFactory.DropletSlash(), Palette.Thirst));

            // Every leaf the plant may ever show is bound once. Growth then only changes how many are
            // active and where they sit, which is what lets authored leaves survive a stage change.
            BindLeaves();
            _flowerRoot = EnsurePart(ref rig.Flower, _plant, "Flower", () => BuildFlower(_plant));

            if (_waterBadge != null)
            {
                _waterBadge.gameObject.SetActive(false);
            }

            if (_thirstBadge != null)
            {
                _thirstBadge.gameObject.SetActive(false);
            }

            if (_flowerRoot != null)
            {
                _flowerRoot.gameObject.SetActive(false);
            }
        }

        private Transform Badge(string name, Sprite sprite, Color color)
        {
            GameObject badge = MeshFactory.SpriteObject(name, _plant, sprite, color, 10);
            badge.transform.SetLocalTRS(Vector3.zero, Quaternion.identity, Vector3.one * (_rig != null ? _rig.BadgeScale : 0.30f));
            return badge.transform;
        }

        /// <summary>
        /// Builds the leaf list from the rig's objects, creating the missing ones only while the rig
        /// allows it. The rig is updated in place, so whatever is bound here is what the inspector shows
        /// after the scene is baked.
        /// </summary>
        private void BindLeaves()
        {
            _leaves.Clear();

            int wanted = Mathf.Max(1, _rig != null ? _rig.MaxLeafPairs : 2) * 2;
            var bound = new List<Transform>();

            if (_rig != null && _rig.Leaves != null)
            {
                foreach (Transform leaf in _rig.Leaves)
                {
                    if (leaf != null)
                    {
                        bound.Add(leaf);
                    }
                }
            }

            for (int index = 0; index < wanted; index++)
            {
                Transform leaf = index < bound.Count ? bound[index] : null;
                if (leaf == null)
                {
                    int pair = index / 2;
                    int side = index % 2;
                    Transform slot = null;
                    leaf = EnsurePart(ref slot, _foliage, $"Leaf {pair}.{side}", () => MeshFactory.Primitive(
                        PrimitiveType.Sphere,
                        $"Leaf {pair}.{side}",
                        _foliage,
                        MeshFactory.Lit("leaf", Palette.Leaf)).transform);
                }

                if (leaf == null)
                {
                    continue;
                }

                _leaves.Add(new LeafPart
                {
                    Transform = leaf,
                    PairIndex = index / 2,
                    Fraction = 0.62f,
                    Angle = 24f,
                    Size = 1f
                });
            }

            // Leaves the rig listed beyond the bound set are left exactly as they are; hiding them here
            // would silently disable art the user added on purpose.
            if (_rig != null)
            {
                var all = new List<Transform>();
                foreach (LeafPart leaf in _leaves)
                {
                    all.Add(leaf.Transform);
                }

                for (int index = all.Count; index < bound.Count; index++)
                {
                    all.Add(bound[index]);
                }

                _rig.Leaves = all.ToArray();
            }
        }

        /// <summary>Finds or creates a plain container, used for the plant root and the foliage root.</summary>
        private Transform EnsureContainer(ref Transform slot, Transform parent, string name)
        {
            if (slot != null)
            {
                return slot;
            }

            Transform found = parent != null ? parent.Find(name) : null;
            if (found != null)
            {
                slot = found;
                return found;
            }

            if (_rig == null || !_rig.CreateMissingParts)
            {
                ReportMissing(name);
                return null;
            }

            var created = new GameObject(name);
            created.transform.SetParent(parent != null ? parent : _rig.transform, false);
            slot = created.transform;
            return slot;
        }

        /// <summary>
        /// Finds or creates one mesh part. The factory runs only when the part is genuinely absent and
        /// the rig allows creation, so an object you replaced with your own art is never overwritten.
        ///
        /// Note that this cannot delegate to a helper that makes a bare GameObject: the part needs its
        /// mesh or its sprite, and an empty object with the right name would silently render nothing.
        /// </summary>
        private Transform EnsurePart(ref Transform slot, Transform parent, string name, Func<Transform> create)
        {
            if (slot != null)
            {
                return slot;
            }

            if (parent != null)
            {
                Transform found = parent.Find(name);
                if (found != null)
                {
                    slot = found;
                    return found;
                }
            }

            if (_rig != null && !_rig.CreateMissingParts)
            {
                ReportMissing(name);
                return null;
            }

            Transform created = create();
            if (created == null)
            {
                return null;
            }

            created.name = name;
            created.SetParent(parent, false);
            slot = created;
            return created;
        }

        private void ReportMissing(string name)
        {
            if (_reported.Add(name))
            {
                Debug.LogWarning(
                    $"[SmallPot] The pot has no \"{name}\". Assign it on the PotRig component, or run " +
                    "\"Small Pot/Rebuild Stand-in Scene\" to generate the stand-in again.");
            }
        }

        /// <summary>Makes the pot look like the supplied visual. Never drops the final state.</summary>
        public void Apply(PotVisual visual, GameConfig config, bool animate)
        {
            if (_rig == null || _plant == null)
            {
                return;
            }

            float ceiling = _rig.StemMaxHeight > 0.01f ? _rig.StemMaxHeight : FallbackMaxStemHeight;
            float target = Mathf.Max(MinimumStemHeight, visual.Height * ceiling);

            // Read before they are overwritten: the beats below are all about a change, and a change cannot
            // be recognised once the new value has already replaced the old one. "Was built" keeps the
            // plant from squashing on the very first Apply, which is an appearance rather than growth.
            bool wasBuilt = _builtLeafPairs >= 0;
            bool wasDrooped = _droop;
            bool grew = target > _height + 0.001f;

            _tilt = visual.LeafTiltDegrees + (visual.Droop ? DroopDegrees : 0f);
            _droop = visual.Droop;

            bool structureChanged = visual.LeafPairs != _builtLeafPairs || visual.Flower != _builtFlower;
            if (structureChanged)
            {
                EnsureFoliage(visual.LeafPairs, visual.Flower);
                _builtLeafPairs = visual.LeafPairs;
                _builtFlower = visual.Flower;
            }

            if (_seedShell != null)
            {
                // The seed casing is only visible while the plant is still just a seed.
                _seedShell.gameObject.SetActive(visual.LeafPairs == 0 && !visual.Flower);
            }

            if (_stem != null && _rig.ColorPlantParts)
            {
                var stemRenderer = _stem.GetComponent<MeshRenderer>();
                if (stemRenderer != null)
                {
                    stemRenderer.sharedMaterial = visual.LeafPairs == 0 && !visual.Flower
                        ? MeshFactory.Lit("seed", Palette.Seed)
                        : MeshFactory.Lit("stem", Palette.Stem);
                }
            }

            ApplySoilColour(visual.SoilDryness);
            ApplyLeafColour();

            // The beats, in the order they must not fight each other. All of them move transforms and none
            // touches a material: the parts are meshes coloured through MeshFactory.Lit, and
            // PotRig.ColorPlantParts owns those colours.
            //
            // The plant root's scale and the foliage container's rotation are the only two values Layout()
            // does not rewrite every frame - the stem, the leaves and the badges are all repositioned there
            // - so they are the only places movement can live without being overwritten.
            if (_droop != wasDrooped)
            {
                SetDroop(_droop);
            }

            bool resting = !visual.Droop && (visual.LeafPairs > 0 || visual.Flower);
            if (resting != _resting)
            {
                _resting = resting;
                if (resting)
                {
                    StartBreathing();
                }
                else
                {
                    StopBreathing();
                }
            }

            // Last, because the squash stops the breath and puts it back when it has settled.
            if (animate && grew && wasBuilt)
            {
                SquashAndSettle();
            }

            if (_waterBadge != null)
            {
                _waterBadge.gameObject.SetActive(visual.WateredBadge);
            }

            if (_thirstBadge != null)
            {
                _thirstBadge.gameObject.SetActive(visual.ThirstyBadge);
            }

            float duration = animate ? _motion.Duration(config?.TransitionSeconds ?? 0.6f) : 0f;
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            if (duration <= 0f)
            {
                _height = target;
                _appear = 1f;
                Layout();
                return;
            }

            if (structureChanged)
            {
                _appear = 0f;
            }

            _routine = StartCoroutine(Grow(target, duration));
        }

        private IEnumerator Grow(float target, float duration)
        {
            float from = _height;
            yield return Tween.Value(0f, 1f, duration, t =>
            {
                _height = Mathf.LerpUnclamped(from, target, t);
                _appear = Mathf.Min(1f, t * 1.8f);
            });

            _height = target;
            _appear = 1f;
            _routine = null;
        }

        /// <summary>A droplet falls into the pot. Pure reward, skipped entirely when motion is reduced.</summary>
        public void PlayWaterPour()
        {
            if (_motion == null || _motion.ReduceMotion || !isActiveAndEnabled)
            {
                return;
            }

            Splash();
            StartCoroutine(PourRoutine());
        }

        private IEnumerator PourRoutine()
        {
            GameObject droplet = MeshFactory.SpriteObject(
                "Water Droplet", _plant, TextureFactory.Droplet(), Palette.Water, 12);
            droplet.transform.localScale = Vector3.one * 0.26f;

            float top = _height + 0.55f;
            yield return Tween.Value(0f, 1f, 0.42f, t =>
            {
                if (droplet == null)
                {
                    return;
                }

                droplet.transform.localPosition = new Vector3(0f, Mathf.LerpUnclamped(top, 0.03f, t), 0f);
                var renderer = droplet.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    Color color = renderer.color;
                    color.a = t < 0.75f ? 1f : Mathf.InverseLerp(1f, 0.75f, t);
                    renderer.color = color;
                }
            });

            if (droplet != null)
            {
                Destroy(droplet);
            }
        }

        private void Update()
        {
            if (_camera != null)
            {
                // Badges and the flower always face the camera, so the isometric angle never hides them.
                Quaternion facing = _camera.transform.rotation;
                if (_waterBadge != null)
                {
                    _waterBadge.transform.rotation = facing;
                }

                if (_thirstBadge != null)
                {
                    _thirstBadge.transform.rotation = facing;
                }

                if (_flowerRoot != null)
                {
                    // The flower is billboarded as a whole so its petals keep their radial arrangement.
                    // Billboarded individually they would all land on one another and disappear.
                    _flowerRoot.rotation = facing;
                }
            }
            Layout();
        }

        /// <summary>Repositions the stem, leaves, flower and badges from the current height.</summary>
        private void Layout()
        {
            if (_stem != null)
            {
                float height = Mathf.Max(0.001f, _height);
                // Dividing by the authored stem's own height keeps the config fractions meaning the same
                // thing whatever your stem mesh measures.
                float unit = _rig != null && _rig.StemUnitHeight > 0.001f ? _rig.StemUnitHeight : 1f;
                _stem.localPosition = new Vector3(0f, height * 0.5f, 0f);
                _stem.localScale = new Vector3(1f, height / unit, 1f);
            }

            for (int i = 0; i < _leaves.Count; i++)
            {
                LeafPart leaf = _leaves[i];
                if (leaf.Transform == null)
                {
                    continue;
                }

                float spread = 0.055f + (0.05f * leaf.Fraction);
                var outward = new Vector3(Mathf.Sin(leaf.Angle * Mathf.Deg2Rad), 0f, Mathf.Cos(leaf.Angle * Mathf.Deg2Rad));
                leaf.Transform.localPosition = new Vector3(
                    outward.x * spread,
                    Mathf.Max(0.03f, leaf.Fraction * _height),
                    outward.z * spread);

                leaf.Transform.localRotation =
                    Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Euler(_tilt, 0f, 0f);

                float appear = _appear;
                leaf.Transform.localScale = LeafScale * (leaf.Size * appear);
            }

            // A gentle bob makes the badges feel alive; reduce-motion removes it without removing them.
            float bob = _motion != null && _motion.ReduceMotion
                ? 0f
                : Mathf.Sin(Time.unscaledTime * 2.2f) * 0.022f;
            float badgeY = Mathf.Max(0.16f, _height) + 0.20f + bob;

            if (_waterBadge != null && _waterBadge.gameObject.activeSelf)
            {
                _waterBadge.transform.localPosition = new Vector3(_thirstBadge != null && _thirstBadge.gameObject.activeSelf ? -0.15f : 0f, badgeY, 0f);
            }

            if (_thirstBadge != null && _thirstBadge.gameObject.activeSelf)
            {
                _thirstBadge.transform.localPosition = new Vector3(_waterBadge != null && _waterBadge.gameObject.activeSelf ? 0.15f : 0f, badgeY, 0f);
            }

            if (_flowerRoot != null)
            {
                _flowerRoot.localPosition = new Vector3(0f, Mathf.Max(0.05f, _height) + 0.05f, 0f);
            }
        }

        /// <summary>
        /// Shows the leaves the stage calls for and hides the rest. Nothing is destroyed, which is what
        /// makes it safe to point the rig at objects you authored: growth only changes how many leaves
        /// are visible and where they sit, never that they exist.
        /// </summary>
        private void EnsureFoliage(int leafPairs, bool flower)
        {
            int available = Mathf.Max(0, _leaves.Count / 2);
            int shown = Mathf.Clamp(leafPairs, 0, available);

            for (int index = 0; index < _leaves.Count; index++)
            {
                LeafPart leaf = _leaves[index];
                if (leaf.Transform == null)
                {
                    continue;
                }

                bool visible = leaf.PairIndex < shown;
                leaf.Transform.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                // The fractions depend on how many pairs this stage shows, so an existing pair moves
                // down the stem when a new one appears above it.
                float pairs = Mathf.Max(1, shown);
                leaf.Fraction = pairs <= 1f ? 0.62f : 0.34f + (0.52f * leaf.PairIndex / (pairs - 1f));
                leaf.Angle = 24f + (leaf.PairIndex * 38f) + ((index % 2) * 180f);
                leaf.Size = 0.85f + (0.25f * leaf.PairIndex / Mathf.Max(1f, pairs - 1f));
                _leaves[index] = leaf;
            }

            if (_flowerRoot != null)
            {
                _flowerRoot.gameObject.SetActive(flower);
            }

            Layout();
        }

        /// <summary>Creates the stand-in blossom: five petals and a heart under one billboarded root.</summary>
        private static Transform BuildFlower(Transform parent)
        {
            Material petalMaterial = MeshFactory.Lit("blossom", Palette.Blossom);

            // Petals are children of a billboarded root, arranged in a ring inside its plane, so the
            // blossom reads as five petals from the fixed isometric angle.
            var flowerObject = new GameObject("Flower");
            flowerObject.transform.SetParent(parent, false);
            Transform root = flowerObject.transform;

            for (int i = 0; i < 5; i++)
            {
                float angle = i * 72f;
                float radians = angle * Mathf.Deg2Rad;
                GameObject petal = MeshFactory.Primitive(
                    PrimitiveType.Sphere,
                    $"Petal {i}",
                    root,
                    petalMaterial);
                petal.transform.SetLocalTRS(
                    new Vector3(Mathf.Cos(radians) * 0.10f, Mathf.Sin(radians) * 0.10f, 0f),
                    Quaternion.Euler(0f, 0f, angle),
                    new Vector3(0.13f, 0.21f, 0.05f));
            }

            GameObject heart = MeshFactory.Primitive(
                PrimitiveType.Sphere,
                "Flower Heart",
                root,
                MeshFactory.Lit("blossomheart", Palette.BlossomHeart));
            heart.transform.SetLocalTRS(Vector3.zero, Quaternion.identity, new Vector3(0.09f, 0.09f, 0.055f));
            return root;
        }

        /// <summary>Scale applied to one leaf object at full size.</summary>
        private Vector3 LeafScale => _rig != null && _rig.LeafUnitScale != Vector3.zero
            ? _rig.LeafUnitScale
            : new Vector3(0.26f, 0.05f, 0.42f);

        /// <summary>
        /// Tints the soil through a property block rather than by writing to the material, so a material
        /// you authored is never modified and no asset is ever dirtied by playing the game.
        /// </summary>
        private void ApplySoilColour(float dryness)
        {
            if (_soil == null || _soilBlock == null)
            {
                return;
            }

            Material material = _soil.sharedMaterial;
            if (material == null || !material.HasProperty("_Color"))
            {
                return;
            }

            _soilBlock.Clear();
            _soilBlock.SetColor("_Color", Color.Lerp(Palette.SoilWet, Palette.SoilDry, Mathf.Clamp01(dryness)));
            _soil.SetPropertyBlock(_soilBlock);
        }

        private void ApplyLeafColour()
        {
            if (_rig != null && !_rig.ColorPlantParts)
            {
                return;
            }

            Material leafMaterial = MeshFactory.Lit(
                _droop ? "leafdry" : "leaf",
                _droop ? Palette.LeafThirsty : Palette.Leaf);

            foreach (LeafPart leaf in _leaves)
            {
                if (leaf.Transform == null)
                {
                    continue;
                }

                var renderer = leaf.Transform.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = leafMaterial;
                }
            }

            if (_flowerRoot == null)
            {
                return;
            }

            Material petalMaterial = MeshFactory.Lit(
                _droop ? "blossomdry" : "blossom",
                _droop ? new Color32(0xDD, 0xB0, 0xC0, 0xFF) : Palette.Blossom);

            foreach (Transform petal in _flowerRoot)
            {
                var renderer = petal.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = petalMaterial;
                }
            }
        }

        /// <summary>
        /// The growth beat: a squash, then a settle that overshoots and comes back.
        ///
        /// It drives the plant root's scale, which Layout() never writes - the stem carries the height in
        /// its own scale - so growth cannot be overwritten by the next frame's layout pass.
        /// </summary>
        private void SquashAndSettle()
        {
            if (_plant == null)
            {
                return;
            }

            float motion = _motion != null ? _motion.Duration(0.42f) : 0.42f;
            if (motion <= 0f)
            {
                return;
            }

            Juice.Stop(_plant);
            _plant.localScale = _plantHome;

            DOTween.Sequence()
                .SetUpdate(true)
                .SetId(_plant)
                .Append(_plant.DOScale(
                        new Vector3(_plantHome.x * 1.12f, _plantHome.y * 0.86f, _plantHome.z * 1.12f),
                        motion * 0.30f)
                    .SetEase(Ease.OutQuad))
                .Append(_plant.DOScale(
                        new Vector3(_plantHome.x * 0.97f, _plantHome.y * 1.06f, _plantHome.z * 0.97f),
                        motion * 0.35f)
                    .SetEase(Ease.InOutQuad))
                .Append(_plant.DOScale(_plantHome, motion * 0.35f).SetEase(Ease.OutBack))
                .OnComplete(() =>
                {
                    _plant.localScale = _plantHome;
                    if (_resting)
                    {
                        StartBreathing();
                    }
                });
        }

        /// <summary>
        /// The thirst droop: the foliage leans over, slowly, and stays there.
        ///
        /// A rotation of the foliage container rather than of the leaves, because Layout() rewrites every
        /// leaf's rotation each frame and would overwrite it. Child rotations are relative, so leaning the
        /// container tilts all of the leaves at once and leaves the per-leaf droop angle on top of it.
        /// </summary>
        private void SetDroop(bool drooping)
        {
            if (_foliage == null)
            {
                return;
            }

            float motion = _motion != null ? _motion.Duration(1.1f) : 1.1f;
            Quaternion target = drooping
                ? _foliageHome * Quaternion.Euler(DroopDegrees * 0.3f, 0f, 0f)
                : _foliageHome;

            Juice.Stop(_foliage);

            if (motion <= 0f)
            {
                _foliage.localRotation = target;
                return;
            }

            _foliage.DOLocalRotate(target.eulerAngles, motion)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetId(_foliage);
        }

        /// <summary>
        /// A slow breath for a plant that is thriving: movement without asking for attention.
        ///
        /// The plant root again, and started only when the thriving state actually changes rather than on
        /// every Apply, because a breath restarted every refresh would stutter instead of breathe.
        /// </summary>
        private void StartBreathing()
        {
            if (_plant == null)
            {
                return;
            }

            float motion = _motion != null ? _motion.Duration(2.2f) : 2.2f;

            Juice.Stop(_plant);
            _plant.localScale = _plantHome;

            if (motion <= 0f)
            {
                return;
            }

            _plant.DOScale(_plantHome * 1.02f, motion)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true)
                .SetId(_plant);
        }

        private void StopBreathing()
        {
            if (_plant == null)
            {
                return;
            }

            Juice.Stop(_plant);
            _plant.localScale = _plantHome;
        }

        /// <summary>The watering punch: one quick overshoot, then the breath comes back.</summary>
        private void Splash()
        {
            if (_plant == null)
            {
                return;
            }

            float motion = _motion != null ? _motion.Duration(0.34f) : 0.34f;
            if (motion <= 0f)
            {
                return;
            }

            Juice.Stop(_plant);
            _plant.localScale = _plantHome;

            _plant.DOPunchScale(Vector3.one * 0.14f, motion, 8, 0.9f)
                .SetUpdate(true)
                .SetId(_plant)
                .OnComplete(() =>
                {
                    _plant.localScale = _plantHome;
                    if (_resting)
                    {
                        StartBreathing();
                    }
                });
        }

        public float CurrentHeight => _height;
    }
}
