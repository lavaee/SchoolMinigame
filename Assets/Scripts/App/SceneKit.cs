using UnityEngine;
using UnityEngine.Rendering;

namespace SmallPot
{
    /// <summary>
    /// The game's colours, in one place.
    ///
    /// These deliberately do not live in config.json. The brief asks for all *numbers* to be data, and
    /// they are - durations, costs, thresholds and stage counts all come from the file. Art direction
    /// is not configuration, and putting a palette into the employer's config.json would mean editing
    /// a file we are not allowed to edit.
    /// </summary>
    public static class Palette
    {
        /// <summary>
        /// A colour from data, or the game's own when the key is absent or unreadable.
        ///
        /// "none" is honoured as plain white, which for an icon means "leave the artwork's own colours
        /// alone" - the answer somebody wants after replacing a sprite and finding it tinted green.
        /// </summary>
        public static Color Colour(string text, Color fallback)
        {
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            string trimmed = text.Trim();
            if (string.Equals(trimmed, "none", System.StringComparison.OrdinalIgnoreCase))
            {
                return Color.white;
            }

            return ColorUtility.TryParseHtmlString(trimmed, out Color parsed) ? parsed : fallback;
        }

        /// <summary>
        /// The tint for an icon. With nothing in the data the answer depends on the art: a generated icon
        /// is a white silhouette that needs a tint to become an icon, while a replaced one carries its own
        /// colours and tinting it is how somebody's artwork ends up the wrong colour.
        /// </summary>
        public static Color Tint(string text, bool artIsAuthored, Color fallback)
        {
            if (!string.IsNullOrEmpty(text))
            {
                return Colour(text, fallback);
            }

            return artIsAuthored ? Color.white : fallback;
        }

        public static readonly Color Room = new Color32(0xE9, 0xDA, 0xC2, 0xFF);
        public static readonly Color Wall = new Color32(0xF6, 0xEC, 0xDA, 0xFF);
        public static readonly Color WallShade = new Color32(0xE4, 0xD6, 0xBE, 0xFF);
        public static readonly Color Frame = new Color32(0xFA, 0xF7, 0xF1, 0xFF);
        public static readonly Color SillTop = new Color32(0xDE, 0xAC, 0x72, 0xFF);
        public static readonly Color SillFace = new Color32(0xC4, 0x8B, 0x50, 0xFF);
        public static readonly Color Sky = new Color32(0xBB, 0xE0, 0xF2, 0xFF);
        public static readonly Color SkyDeep = new Color32(0x9F, 0xD2, 0xEC, 0xFF);
        public static readonly Color Cloud = new Color32(0xFF, 0xFF, 0xFF, 0xFF);

        public static readonly Color Pot = new Color32(0xC8, 0x6A, 0x40, 0xFF);
        public static readonly Color PotDark = new Color32(0xA9, 0x53, 0x2E, 0xFF);
        public static readonly Color Saucer = new Color32(0xB5, 0x57, 0x33, 0xFF);

        public static readonly Color SoilWet = new Color32(0x4A, 0x33, 0x27, 0xFF);
        public static readonly Color SoilDry = new Color32(0xA5, 0x86, 0x5F, 0xFF);

        public static readonly Color Stem = new Color32(0x5C, 0xA1, 0x43, 0xFF);
        public static readonly Color Seed = new Color32(0x8A, 0x63, 0x40, 0xFF);
        public static readonly Color Leaf = new Color32(0x6E, 0xBF, 0x4F, 0xFF);
        public static readonly Color LeafThirsty = new Color32(0x8E, 0xA8, 0x5C, 0xFF);
        public static readonly Color Blossom = new Color32(0xF3, 0xA3, 0xC3, 0xFF);
        public static readonly Color BlossomHeart = new Color32(0xF7, 0xD6, 0x4A, 0xFF);

        public static readonly Color Water = new Color32(0x4F, 0xB3, 0xE8, 0xFF);
        public static readonly Color Thirst = new Color32(0xE8, 0xA3, 0x3D, 0xFF);

        public static readonly Color Ink = new Color32(0x4A, 0x3B, 0x2E, 0xFF);
        public static readonly Color InkSoft = new Color32(0x8A, 0x79, 0x67, 0xFF);
        public static readonly Color Panel = new Color32(0xFF, 0xFC, 0xF6, 0xFF);
        public static readonly Color PanelEdge = new Color32(0xE2, 0xD5, 0xC0, 0xFF);
        public static readonly Color Accent = new Color32(0x5C, 0xA1, 0x43, 0xFF);
        public static readonly Color AccentSoft = new Color32(0xE5, 0xF1, 0xDE, 0xFF);
        public static readonly Color Caution = new Color32(0xD9, 0x8B, 0x3A, 0xFF);
        public static readonly Color Disabled = new Color32(0xC9, 0xC0, 0xB4, 0xFF);
        public static readonly Color Scrim = new Color32(0x2A, 0x22, 0x1A, 0x9A);
    }

    /// <summary>
    /// Builds the static part of the world: camera, lighting, the window and the pot.
    ///
    /// The camera is an orthographic three-quarter view of a windowsill, which is the isometric look
    /// the brief asks for without making the ledge run diagonally across a portrait screen.
    ///
    /// This is not called at startup any more: it is the stand-in generator behind
    /// "Small Pot / Rebuild Stand-in Scene", which bakes the geometry into the scene once. After that
    /// the scene itself is what ships and what you edit.
    /// </summary>
    public static class SceneKit
    {
        public const float CameraYaw = 30f;
        public const float CameraPitch = 27f;

        /// <summary>Where the camera looks: slightly above the sill, so the plant sits centrally.</summary>
        public static readonly Vector3 CameraTarget = new Vector3(0f, 0.62f, 0.15f);

        public const float OrthographicSize = 1.9f;

        public static Camera CreateCamera(Transform parent)
        {
            var gameObject = new GameObject("Main Camera");
            gameObject.tag = "MainCamera";
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            var camera = gameObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = OrthographicSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Palette.Room;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            camera.allowHDR = false;
            camera.allowMSAA = true;

            gameObject.transform.rotation = Quaternion.Euler(CameraPitch, CameraYaw, 0f);
            gameObject.transform.position = CameraTarget - (gameObject.transform.forward * 14f);

            return camera;
        }

        public static void CreateLighting(Transform parent)
        {
            // No realtime shadows: the contact shadow under the pot grounds everything, and skipping
            // shadow maps keeps the frame budget comfortable on a low-end Android phone.
            var key = new GameObject("Key Light");
            if (parent != null)
            {
                key.transform.SetParent(parent, false);
            }

            var keyLight = key.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color32(0xFF, 0xF4, 0xE0, 0xFF);
            keyLight.intensity = 0.85f;
            keyLight.shadows = LightShadows.None;
            key.transform.rotation = Quaternion.Euler(46f, -38f, 0f);

            var fill = new GameObject("Fill Light");
            if (parent != null)
            {
                fill.transform.SetParent(parent, false);
            }

            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color32(0xD8, 0xE8, 0xFF, 0xFF);
            fillLight.intensity = 0.55f;
            fillLight.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(-18f, 150f, 0f);

            // Ambient is deliberately high and the key light gentle: this is a flat, toy-like look, and a
            // strong key would turn the side of the window that faces away from the sun into grey.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color32(0xDC, 0xD8, 0xD0, 0xFF);
            RenderSettings.fog = false;
        }

        /// <summary>Sill, wall, window frame, sky and the small props that make the ledge feel lived in.</summary>
        public static GameObject CreateEnvironment(Transform parent)
        {
            var root = new GameObject("Environment");
            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }

            Transform t = root.transform;

            // The camera shows roughly 2.14 world units across, so the opening is 1.6 wide and the frame
            // sits just inside that: the window reads as a window, with a strip of wall on either side.
            MeshFactory.Primitive(PrimitiveType.Cube, "Sky", t, MeshFactory.Lit("sky", Palette.Sky))
                .transform.SetLocalTRS(new Vector3(0f, 1.3f, 0.62f), Quaternion.identity, new Vector3(1.66f, 2.7f, 0.06f));

            MeshFactory.Primitive(PrimitiveType.Cube, "Sky Band", t, MeshFactory.Lit("skyband", Palette.SkyDeep))
                .transform.SetLocalTRS(new Vector3(0f, 0.16f, 0.58f), Quaternion.identity, new Vector3(1.66f, 0.44f, 0.06f));

            CreateCloud(t, "Cloud A", new Vector3(-0.34f, 1.98f, 0.52f), 0.42f);
            CreateCloud(t, "Cloud B", new Vector3(0.40f, 1.52f, 0.50f), 0.30f);

            // The wall, built as three panels so the window opening is a real gap rather than a decal.
            MeshFactory.Primitive(PrimitiveType.Cube, "Wall Left", t, MeshFactory.Lit("wall", Palette.Wall))
                .transform.SetLocalTRS(new Vector3(-1.44f, 1.25f, 0.45f), Quaternion.identity, new Vector3(1.15f, 2.6f, 0.14f));

            MeshFactory.Primitive(PrimitiveType.Cube, "Wall Right", t, MeshFactory.Lit("wall", Palette.Wall))
                .transform.SetLocalTRS(new Vector3(1.44f, 1.25f, 0.45f), Quaternion.identity, new Vector3(1.15f, 2.6f, 0.14f));

            MeshFactory.Primitive(PrimitiveType.Cube, "Wall Top", t, MeshFactory.Lit("wallshade", Palette.WallShade))
                .transform.SetLocalTRS(new Vector3(0f, 2.46f, 0.45f), Quaternion.identity, new Vector3(3.0f, 0.36f, 0.14f));

            // Window frame.
            Material frame = MeshFactory.Lit("frame", Palette.Frame);
            MeshFactory.Primitive(PrimitiveType.Cube, "Frame Left", t, frame)
                .transform.SetLocalTRS(new Vector3(-0.86f, 1.15f, 0.40f), Quaternion.identity, new Vector3(0.11f, 2.6f, 0.26f));
            MeshFactory.Primitive(PrimitiveType.Cube, "Frame Right", t, frame)
                .transform.SetLocalTRS(new Vector3(0.86f, 1.15f, 0.40f), Quaternion.identity, new Vector3(0.11f, 2.6f, 0.26f));
            MeshFactory.Primitive(PrimitiveType.Cube, "Frame Top", t, frame)
                .transform.SetLocalTRS(new Vector3(0f, 2.20f, 0.40f), Quaternion.identity, new Vector3(1.83f, 0.11f, 0.26f));

            // Horizontal muntin: the detail that makes the opening read as a window.
            MeshFactory.Primitive(PrimitiveType.Cube, "Frame Bar", t, frame)
                .transform.SetLocalTRS(new Vector3(0f, 1.10f, 0.36f), Quaternion.identity, new Vector3(1.72f, 0.07f, 0.20f));

            // Sill: the stage the pot stands on.
            MeshFactory.Primitive(PrimitiveType.Cube, "Sill", t, MeshFactory.Lit("sillface", Palette.SillFace))
                .transform.SetLocalTRS(new Vector3(0f, -0.075f, 0.26f), Quaternion.identity, new Vector3(3.0f, 0.15f, 0.80f));

            MeshFactory.Primitive(PrimitiveType.Cube, "Sill Top", t, MeshFactory.Lit("sill", Palette.SillTop))
                .transform.SetLocalTRS(new Vector3(0f, -0.004f, 0.26f), Quaternion.identity, new Vector3(3.0f, 0.012f, 0.80f));

            // Props: two pebbles and a leaf that fell off, small signs that someone lives here.
            MeshFactory.Primitive(PrimitiveType.Sphere, "Pebble A", t, MeshFactory.Lit("pebble", new Color32(0xB9, 0xB2, 0xA6, 0xFF)))
                .transform.SetLocalTRS(new Vector3(-1.02f, 0.035f, 0.34f), Quaternion.Euler(0f, 20f, 12f), new Vector3(0.13f, 0.075f, 0.11f));
            MeshFactory.Primitive(PrimitiveType.Sphere, "Pebble B", t, MeshFactory.Lit("pebble", new Color32(0xB9, 0xB2, 0xA6, 0xFF)))
                .transform.SetLocalTRS(new Vector3(-0.86f, 0.025f, 0.46f), Quaternion.Euler(0f, -30f, -8f), new Vector3(0.09f, 0.05f, 0.08f));

            GameObject fallenLeaf = MeshFactory.SpriteObject(
                "Fallen Leaf", t, TextureFactory.Leaf(), new Color32(0x9C, 0xB5, 0x62, 0xFF), 1);
            fallenLeaf.transform.SetLocalTRS(
                new Vector3(0.92f, 0.035f, 0.46f),
                Quaternion.Euler(90f, 0f, 34f),
                new Vector3(0.30f, 0.30f, 0.30f));

            return root;
        }

        private static void CreateCloud(Transform parent, string name, Vector3 position, float scale)
        {
            Material cloud = MeshFactory.Lit("cloud", Palette.Cloud);
            var cloudRoot = new GameObject(name);
            cloudRoot.transform.SetParent(parent, false);
            cloudRoot.transform.localPosition = position;

            var offsets = new[]
            {
                new Vector3(-0.34f, 0f, 0f),
                new Vector3(0f, 0.06f, 0f),
                new Vector3(0.34f, -0.01f, 0f)
            };

            var sizes = new[]
            {
                new Vector3(0.30f, 0.18f, 0.10f),
                new Vector3(0.40f, 0.24f, 0.10f),
                new Vector3(0.28f, 0.16f, 0.10f)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                MeshFactory.Primitive(PrimitiveType.Sphere, $"{name} Puff {i}", cloudRoot.transform, cloud)
                    .transform.SetLocalTRS(offsets[i] * scale, Quaternion.identity, sizes[i] * scale);
            }
        }

        /// <summary>
        /// The pot, its saucer, the soil and the contact shadow.
        ///
        /// The pot is a tapered cylinder because a flowerpot is one, and the soil is a separate disc so
        /// that its colour can dry out independently of the pot.
        /// </summary>
        public static PotRig CreatePot(Transform parent)
        {
            const float saucerHeight = 0.028f;
            const float potHeight = 0.40f;
            const float rimHeight = 0.06f;
            const float potBaseY = saucerHeight;
            const float potTopY = potBaseY + potHeight;
            const float rimTopY = potTopY + rimHeight;
            const float soilTopY = rimTopY - 0.015f;

            var root = new GameObject("Pot");
            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }

            Transform t = root.transform;

            GameObject saucer = MeshFactory.MeshObject(
                "Saucer",
                t,
                MeshFactory.TaperedCylinder(0.345f, 0.315f, saucerHeight),
                MeshFactory.Lit("saucer", Palette.Saucer));
            saucer.transform.localPosition = new Vector3(0f, saucerHeight * 0.5f, 0f);

            GameObject body = MeshFactory.MeshObject(
                "Body",
                t,
                MeshFactory.TaperedCylinder(0.40f, 0.29f, potHeight),
                MeshFactory.Lit("pot", Palette.Pot));
            body.transform.localPosition = new Vector3(0f, potBaseY + (potHeight * 0.5f), 0f);

            GameObject rim = MeshFactory.MeshObject(
                "Rim",
                t,
                MeshFactory.TaperedCylinder(0.435f, 0.415f, rimHeight),
                MeshFactory.Lit("potdark", Palette.PotDark));
            rim.transform.localPosition = new Vector3(0f, potTopY + (rimHeight * 0.5f), 0f);

            Material soilMaterial = MeshFactory.Lit("soil", Palette.SoilWet);
            GameObject soil = MeshFactory.MeshObject(
                "Soil",
                t,
                MeshFactory.TaperedCylinder(0.375f, 0.375f, 0.05f),
                soilMaterial);
            soil.transform.localPosition = new Vector3(0f, soilTopY - 0.025f, 0f);

            GameObject shadow = MeshFactory.SpriteObject(
                "Contact Shadow", t, TextureFactory.SoftShadow(), new Color(1f, 1f, 1f, 0.5f), 0);
            shadow.transform.SetLocalTRS(
                new Vector3(0f, 0.007f, 0.04f),
                Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.78f, 0.78f, 1f));

            var anchor = new GameObject("Plant Anchor");
            anchor.transform.SetParent(t, false);
            anchor.transform.localPosition = new Vector3(0f, soilTopY, 0f);

            var rig = t.gameObject.AddComponent<PotRig>();
            rig.PlantAnchor = anchor.transform;
            rig.Soil = soil.GetComponent<MeshRenderer>();
            rig.SoilMaterial = soilMaterial;

            // The generator is the one caller that must be allowed to create the plant parts. The baker
            // turns this back off before saving, so a part you delete afterwards stays deleted.
            rig.CreateMissingParts = true;
            return rig;
        }
    }
}
