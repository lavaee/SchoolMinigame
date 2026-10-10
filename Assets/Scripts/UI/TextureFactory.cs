using System.Collections.Generic;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Draws every sprite the game uses into textures at runtime.
    ///
    /// No third-party art is shipped. That keeps the licence surface at zero and, more usefully, lets
    /// the icons carry meaning on their own: each pot state has a distinct silhouette and each piece
    /// of feedback has an icon, so nothing in this game depends on colour alone being understood.
    /// Textures are generated once and cached, because they are a few kilobytes each.
    /// </summary>
    public static class TextureFactory
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>Where baked sprite assets live, relative to a Resources folder.</summary>
        public const string SpriteFolder = "SmallPotSprites";

        /// <summary>
        /// Returns one sprite, generating it only when the project does not already contain it.
        ///
        /// Generated art is baked into the project as a real sprite asset, which is what lets the scene
        /// hold a serialisable reference to it and lets you replace any single icon with your own drawing
        /// by overwriting one file.
        /// </summary>
        private static Sprite Cached(string key, System.Func<Sprite> create)
        {
            if (Cache.TryGetValue(key, out Sprite sprite) && sprite != null)
            {
                return sprite;
            }

            sprite = Resources.Load<Sprite>($"{SpriteFolder}/{key}") ?? create();
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>The engine's built-in white sprite for solid rectangles (panels, button bodies).</summary>
        public static Sprite Solid() => Cached("solid", () =>
        {
            var texture = NewTexture(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return ToSprite(texture, new Vector2(0.5f, 0.5f), 100f, Vector4.zero);
        });

        /// <summary>A rounded rectangle in 9-slice form, for panels and buttons.</summary>
        public static Sprite RoundedRect(int size = 64, int radius = 18) => Cached($"round-{size}-{radius}", () =>
        {
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            float r = radius;
            float max = size - 1;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x < r ? r - x : x > max - r ? x - (max - r) : 0f;
                    float dy = y < r ? r - y : y > max - r ? y - (max - r) : 0f;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float alpha = Mathf.Clamp01(r - distance + 0.5f);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            float border = radius + 2;
            return ToSprite(
                texture,
                new Vector2(0.5f, 0.5f),
                100f,
                new Vector4(border, border, border, border));
        });

        public static Sprite Circle(int size = 128) => Cached($"circle-{size}", () =>
        {
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            float centre = (size - 1) * 0.5f;
            float radius = centre - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt(((x - centre) * (x - centre)) + ((y - centre) * (y - centre)));
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return ToSprite(texture, new Vector2(0.5f, 0.5f), 100f, Vector4.zero);
        });

        /// <summary>A water droplet, the currency of the game.</summary>
        public static Sprite Droplet(int size = 128) => Cached($"droplet-{size}", () =>
            Shape(size, (u, v) =>
            {
                // v runs bottom to top. A pointed top, a round bottom.
                float cx = 0.5f;
                float tip = 0.94f;
                float radius = 0.33f;
                float bodyTop = 0.62f;

                float distanceToTip = Mathf.Abs(u - cx) / 0.16f + (tip - v);
                float circle = Mathf.Sqrt(((u - cx) * (u - cx)) + ((v - 0.36f) * (v - 0.36f))) - radius;
                float body = v <= bodyTop ? circle : Mathf.Min(circle, distanceToTip);
                return -body;
            }));

        /// <summary>
        /// A droplet with a slash through it: the "thirsty" badge. It is a shape, not a colour, so the
        /// state survives both colour blindness and the reduced-motion setting.
        /// </summary>
        public static Sprite DropletSlash(int size = 128) => Cached($"droplet-slash-{size}", () =>
            Shape(size, (u, v) =>
            {
                float cx = 0.5f;
                float tip = 0.94f;
                float radius = 0.33f;
                float bodyTop = 0.62f;

                float distanceToTip = Mathf.Abs(u - cx) / 0.16f + (tip - v);
                float circle = Mathf.Sqrt(((u - cx) * (u - cx)) + ((v - 0.36f) * (v - 0.36f))) - radius;
                float body = v <= bodyTop ? circle : Mathf.Min(circle, distanceToTip);
                float droplet = -body;

                // Diagonal bar from bottom-left to top-right, carved out of the droplet.
                float along = (u + v) * 0.5f;
                float across = (u - v);
                float bar = Mathf.Max(Mathf.Abs(across) - 0.055f, Mathf.Abs(along - 0.5f) - 0.42f);
                float carved = Mathf.Min(droplet, -bar);
                return carved;
            }));

        /// <summary>A leaf: a pointed ellipse with a centre vein. Used for the pot's foliage.</summary>
        public static Sprite Leaf(int size = 128) => Cached($"leaf-{size}", () =>
            Shape(size, (u, v) =>
            {
                float cx = 0.5f;
                float nx = (u - cx) / 0.30f;
                float ny = (v - 0.5f) / 0.46f;
                float body = Mathf.Sqrt((nx * nx) + (ny * ny)) - 1f;

                // Pinch the ends so it reads as a leaf rather than an oval.
                float pinch = Mathf.Abs(ny) * 0.35f;
                return -(body + pinch);
            }));

        /// <summary>A five-petal blossom, used once the plant blooms.</summary>
        public static Sprite Blossom(int size = 128) => Cached($"blossom-{size}", () =>
            Shape(size, (u, v) =>
            {
                float cx = 0.5f;
                float cy = 0.5f;
                float angle = Mathf.Atan2(v - cy, u - cx);
                float distance = Mathf.Sqrt(((u - cx) * (u - cx)) + ((v - cy) * (v - cy)));
                float petals = 0.26f + (0.09f * Mathf.Cos(angle * 5f));
                return -(distance - petals);
            }));

        public static Sprite Check(int size = 128) => Cached($"check-{size}", () =>
            Shape(size, (u, v) =>
            {
                float thickness = 0.075f;
                float a = Segment(u, v, 0.22f, 0.52f, 0.44f, 0.26f, thickness);
                float b = Segment(u, v, 0.44f, 0.26f, 0.80f, 0.74f, thickness);
                return -Mathf.Min(a, b);
            }));

        public static Sprite Cross(int size = 128) => Cached($"cross-{size}", () =>
            Shape(size, (u, v) =>
            {
                float thickness = 0.075f;
                float a = Segment(u, v, 0.26f, 0.26f, 0.74f, 0.74f, thickness);
                float b = Segment(u, v, 0.26f, 0.74f, 0.74f, 0.26f, thickness);
                return -Mathf.Min(a, b);
            }));

        /// <summary>A gear, for the settings button.</summary>
        public static Sprite Gear(int size = 128) => Cached($"gear-{size}", () =>
            Shape(size, (u, v) =>
            {
                float cx = 0.5f;
                float cy = 0.5f;
                float angle = Mathf.Atan2(v - cy, u - cx);
                float distance = Mathf.Sqrt(((u - cx) * (u - cx)) + ((v - cy) * (v - cy)));
                float teeth = 0.30f + (0.075f * Mathf.Sign(Mathf.Cos(angle * 8f)));
                float body = distance - teeth;
                float hole = 0.115f - distance;
                return -Mathf.Min(body, hole);
            }));

        /// <summary>An upward chevron, for "next morning".</summary>
        public static Sprite ChevronUp(int size = 128) => Cached($"chevron-{size}", () =>
            Shape(size, (u, v) =>
            {
                float thickness = 0.085f;
                float a = Segment(u, v, 0.22f, 0.40f, 0.50f, 0.68f, thickness);
                float b = Segment(u, v, 0.50f, 0.68f, 0.78f, 0.40f, thickness);
                return -Mathf.Min(a, b);
            }));

        /// <summary>A play triangle, for "collect drops".</summary>
        public static Sprite Play(int size = 128) => Cached($"play-{size}", () =>
            Shape(size, (u, v) =>
            {
                float left = 0.28f;
                float right = 0.76f;
                float halfHeight = (right - u) * 0.62f;
                float inside = Mathf.Max(u - right, Mathf.Max(left - u, Mathf.Abs(v - 0.5f) - halfHeight));
                return -inside;
            }));

        /// <summary>A refresh arrow, for "reload data".</summary>
        public static Sprite Refresh(int size = 128) => Cached($"refresh-{size}", () =>
            Shape(size, (u, v) =>
            {
                float cx = 0.5f;
                float cy = 0.5f;
                float angle = Mathf.Atan2(v - cy, u - cx);
                float distance = Mathf.Sqrt(((u - cx) * (u - cx)) + ((v - cy) * (v - cy)));
                float ring = Mathf.Abs(distance - 0.30f) - 0.07f;
                // Leave a gap in the ring so it reads as an open arrow rather than a circle.
                bool gap = angle > -0.5f && angle < 0.55f;
                return gap && distance > 0.22f ? 1f : -ring;
            }));

        /// <summary>A soft radial blob, used as the pot's contact shadow.</summary>
        public static Sprite SoftShadow(int size = 128) => Cached($"shadow-{size}", () =>
        {
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            float centre = (size - 1) * 0.5f;
            float radius = centre;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt(((x - centre) * (x - centre)) + ((y - centre) * (y - centre))) / radius;
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha;
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha * 0.55f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return ToSprite(texture, new Vector2(0.5f, 0.5f), 100f, Vector4.zero);
        });

        private static float Segment(float u, float v, float x0, float y0, float x1, float y1, float thickness)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float lengthSquared = (dx * dx) + (dy * dy);
            float t = lengthSquared <= 0f ? 0f : Mathf.Clamp01((((u - x0) * dx) + ((v - y0) * dy)) / lengthSquared);
            float px = x0 + (t * dx);
            float py = y0 + (t * dy);
            return Mathf.Sqrt(((u - px) * (u - px)) + ((v - py) * (v - py))) - thickness;
        }

        /// <summary>
        /// Rasterises a signed-distance function into a white sprite with antialiased alpha.
        /// The function returns a negative value inside the shape and a positive value outside.
        /// </summary>
        private static Sprite Shape(int size, System.Func<float, float, float> signedDistance)
        {
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            float step = 1f / (size - 1);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x * step;
                    float v = y * step;
                    float distance = signedDistance(u, v);
                    float alpha = Mathf.Clamp01(0.5f - (distance / step));
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return ToSprite(texture, new Vector2(0.5f, 0.5f), 100f, Vector4.zero);
        }

        private static Texture2D NewTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            return texture;
        }

        private static Sprite ToSprite(Texture2D texture, Vector2 pivot, float pixelsPerUnit, Vector4 border) =>
            Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                pivot,
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                border);
    }
}
