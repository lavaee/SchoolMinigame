using System.Collections;
using System.Collections.Generic;
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
            public float Fraction;
            public float Angle;
            public float Size;
        }

        private const float MinimumStemHeight = 0.06f;
        private const float DroopDegrees = 40f;
        private const float BadgeScale = 0.30f;

        private PotRig _rig;
        private Camera _camera;
        private MotionPrefs _motion;

        private Transform _plant;
        private Transform _stem;
        private GameObject _seedShell;
        private readonly List<LeafPart> _leaves = new List<LeafPart>();
        private Transform _flowerRoot;
        private GameObject _waterBadge;
        private GameObject _thirstBadge;
        private Material _soilMaterial;

        private float _height = MinimumStemHeight;
        private float _tilt;
        private float _appear = 1f;
        private bool _droop;
        private int _builtLeafPairs = -1;
        private bool _builtFlower;
        private Coroutine _routine;

        public void Initialize(PotRig rig, Camera camera, MotionPrefs motion)
        {
            _rig = rig;
            _camera = camera;
            _motion = motion;
            _soilMaterial = rig.SoilMaterial;

            _plant = new GameObject("Plant").transform;
            _plant.SetParent(rig.PlantAnchor, false);

            _stem = MeshFactory.MeshObject(
                "Stem",
                _plant,
                MeshFactory.TaperedCylinder(0.016f, 0.026f, 1f, 10),
                MeshFactory.Lit("stem", Palette.Stem)).transform;

            _seedShell = MeshFactory.Primitive(
                PrimitiveType.Sphere,
                "Seed Shell",
                _plant,
                MeshFactory.Lit("seed", Palette.Seed));
            _seedShell.transform.SetLocalTRS(
                new Vector3(0.01f, 0.035f, 0f),
                Quaternion.Euler(0f, 24f, 14f),
                new Vector3(0.15f, 0.10f, 0.19f));

            _waterBadge = MeshFactory.SpriteObject(
                "Watered Badge", _plant, TextureFactory.Droplet(), Palette.Water, 10);
            _waterBadge.transform.SetLocalTRS(Vector3.zero, Quaternion.identity, Vector3.one * BadgeScale);
            _waterBadge.SetActive(false);

            _thirstBadge = MeshFactory.SpriteObject(
                "Thirsty Badge", _plant, TextureFactory.DropletSlash(), Palette.Thirst, 10);
            _thirstBadge.transform.SetLocalTRS(Vector3.zero, Quaternion.identity, Vector3.one * BadgeScale);
            _thirstBadge.SetActive(false);
        }

        /// <summary>Makes the pot look like the supplied visual. Never drops the final state.</summary>
        public void Apply(PotVisual visual, GameConfig config, bool animate)
        {
            float target = Mathf.Max(MinimumStemHeight, visual.Height * PotRig.MaxStemHeight);
            _tilt = visual.LeafTiltDegrees + (visual.Droop ? DroopDegrees : 0f);
            _droop = visual.Droop;

            bool structureChanged = visual.LeafPairs != _builtLeafPairs || visual.Flower != _builtFlower;
            if (structureChanged)
            {
                BuildFoliage(visual.LeafPairs, visual.Flower);
                _builtLeafPairs = visual.LeafPairs;
                _builtFlower = visual.Flower;
            }

            if (_seedShell != null)
            {
                // The seed casing is only visible while the plant is still just a seed.
                _seedShell.SetActive(visual.LeafPairs == 0 && !visual.Flower);
            }

            if (_stem != null)
            {
                _stem.GetComponent<MeshRenderer>().sharedMaterial = visual.LeafPairs == 0 && !visual.Flower
                    ? MeshFactory.Lit("seed", Palette.Seed)
                    : MeshFactory.Lit("stem", Palette.Stem);
            }

            if (_soilMaterial != null)
            {
                _soilMaterial.color = Color.Lerp(Palette.SoilWet, Palette.SoilDry, Mathf.Clamp01(visual.SoilDryness));
            }

            ApplyLeafColour();

            if (_waterBadge != null)
            {
                _waterBadge.SetActive(visual.WateredBadge);
            }

            if (_thirstBadge != null)
            {
                _thirstBadge.SetActive(visual.ThirstyBadge);
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
                _stem.localPosition = new Vector3(0f, height * 0.5f, 0f);
                _stem.localScale = new Vector3(1f, height, 1f);
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
                leaf.Transform.localScale = new Vector3(0.26f, 0.05f, 0.42f) * (leaf.Size * appear);
            }

            // A gentle bob makes the badges feel alive; reduce-motion removes it without removing them.
            float bob = _motion != null && _motion.ReduceMotion
                ? 0f
                : Mathf.Sin(Time.unscaledTime * 2.2f) * 0.022f;
            float badgeY = Mathf.Max(0.16f, _height) + 0.20f + bob;

            if (_waterBadge != null && _waterBadge.activeSelf)
            {
                _waterBadge.transform.localPosition = new Vector3(_thirstBadge != null && _thirstBadge.activeSelf ? -0.15f : 0f, badgeY, 0f);
            }

            if (_thirstBadge != null && _thirstBadge.activeSelf)
            {
                _thirstBadge.transform.localPosition = new Vector3(_waterBadge != null && _waterBadge.activeSelf ? 0.15f : 0f, badgeY, 0f);
            }

            if (_flowerRoot != null)
            {
                _flowerRoot.localPosition = new Vector3(0f, Mathf.Max(0.05f, _height) + 0.05f, 0f);
            }
        }

        /// <summary>Rebuilds leaves and flower. Cheap enough to redo on any stage change.</summary>
        private void BuildFoliage(int leafPairs, bool flower)
        {
            foreach (LeafPart leaf in _leaves)
            {
                if (leaf.Transform != null)
                {
                    Destroy(leaf.Transform.gameObject);
                }
            }

            _leaves.Clear();

            if (_flowerRoot != null)
            {
                Destroy(_flowerRoot.gameObject);
                _flowerRoot = null;
            }

            Material leafMaterial = MeshFactory.Lit(_droop ? "leafdry" : "leaf", _droop ? Palette.LeafThirsty : Palette.Leaf);

            for (int pair = 0; pair < leafPairs; pair++)
            {
                float fraction = leafPairs <= 1 ? 0.62f : 0.34f + (0.52f * pair / (leafPairs - 1f));
                float baseAngle = 24f + (pair * 38f);
                float size = 0.85f + (0.25f * pair / Mathf.Max(1f, leafPairs - 1f));

                for (int side = 0; side < 2; side++)
                {
                    float angle = baseAngle + (side * 180f);
                    GameObject leafObject = MeshFactory.Primitive(
                        PrimitiveType.Sphere,
                        $"Leaf {pair}.{side}",
                        _plant,
                        leafMaterial);

                    _leaves.Add(new LeafPart
                    {
                        Transform = leafObject.transform,
                        Fraction = fraction,
                        Angle = angle,
                        Size = size
                    });
                }
            }

            if (flower)
            {
                Material petalMaterial = MeshFactory.Lit(
                    _droop ? "blossomdry" : "blossom",
                    _droop ? new Color32(0xDD, 0xB0, 0xC0, 0xFF) : Palette.Blossom);

                // Petals are children of a billboarded root, arranged in a ring inside its plane, so the
                // blossom reads as five petals from the fixed isometric angle.
                var flowerObject = new GameObject("Flower");
                flowerObject.transform.SetParent(_plant, false);
                _flowerRoot = flowerObject.transform;

                for (int i = 0; i < 5; i++)
                {
                    float angle = i * 72f;
                    float radians = angle * Mathf.Deg2Rad;
                    GameObject petal = MeshFactory.Primitive(
                        PrimitiveType.Sphere,
                        $"Petal {i}",
                        _flowerRoot,
                        petalMaterial);
                    petal.transform.SetLocalTRS(
                        new Vector3(Mathf.Cos(radians) * 0.10f, Mathf.Sin(radians) * 0.10f, 0f),
                        Quaternion.Euler(0f, 0f, angle),
                        new Vector3(0.13f, 0.21f, 0.05f));
                }

                GameObject heart = MeshFactory.Primitive(
                    PrimitiveType.Sphere,
                    "Flower Heart",
                    _flowerRoot,
                    MeshFactory.Lit("blossomheart", Palette.BlossomHeart));
                heart.transform.SetLocalTRS(Vector3.zero, Quaternion.identity, new Vector3(0.09f, 0.09f, 0.055f));

                Layout();
            }

            Layout();
        }

        private void ApplyLeafColour()
        {
            Material material = MeshFactory.Lit(
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
                    renderer.sharedMaterial = material;
                }
            }
        }

        public float CurrentHeight => _height;
    }
}
