using System.Collections.Generic;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Small mesh and material helpers.
    ///
    /// Everything visible is built from primitives and a couple of generated meshes, so the project
    /// ships no art files at all. The only custom mesh is the pot, which is a tapered cylinder - a
    /// shape primitives cannot express.
    /// </summary>
    public static class MeshFactory
    {
        private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();
        private static readonly List<Material> Owned = new List<Material>();

        /// <summary>Lit material for solid geometry (windowsill, frame, pot, stem).</summary>
        public static Material Lit(string key, Color color, float smoothness = 0.12f)
        {
            string cacheKey = $"lit|{key}|{ColorUtility.ToHtmlStringRGBA(color)}|{smoothness:0.00}";
            if (MaterialCache.TryGetValue(cacheKey, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var material = new Material(shader)
            {
                name = $"SmallPot {key}",
                color = color,
                hideFlags = HideFlags.HideAndDontSave
            };

            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", 0f);

            MaterialCache[cacheKey] = material;
            Owned.Add(material);
            return material;
        }

        /// <summary>
        /// The shared material for every sprite in the scene, supplied once at boot.
        ///
        /// Sprites are tinted per renderer, so one white material covers badges, shadows and props.
        /// Keeping it as a referenced asset rather than calling Shader.Find at runtime is what
        /// guarantees the sprite shader survives into the built player.
        /// </summary>
        public static Material SpriteMaterial { get; set; }

        /// <summary>A tapered cylinder with caps, standing on the XZ plane and centred on the origin.</summary>
        public static Mesh TaperedCylinder(float radiusTop, float radiusBottom, float height, int segments = 24)
        {
            segments = Mathf.Max(6, segments);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            float half = height * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = t * Mathf.PI * 2f;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                // Slope of the side wall, so the normals lean outward correctly.
                float slope = (radiusBottom - radiusTop) / Mathf.Max(0.0001f, height);
                var normal = new Vector3(cos, slope, sin).normalized;

                vertices.Add(new Vector3(cos * radiusTop, half, sin * radiusTop));
                vertices.Add(new Vector3(cos * radiusBottom, -half, sin * radiusBottom));
                normals.Add(normal);
                normals.Add(normal);
                uvs.Add(new Vector2(t, 1f));
                uvs.Add(new Vector2(t, 0f));
            }

            for (int i = 0; i < segments; i++)
            {
                int top = i * 2;
                int bottom = top + 1;
                int nextTop = (i + 1) * 2;
                int nextBottom = nextTop + 1;

                triangles.Add(top);
                triangles.Add(nextTop);
                triangles.Add(bottom);

                triangles.Add(nextTop);
                triangles.Add(nextBottom);
                triangles.Add(bottom);
            }

            // Caps.
            int topCentre = vertices.Count;
            vertices.Add(new Vector3(0f, half, 0f));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int bottomCentre = vertices.Count;
            vertices.Add(new Vector3(0f, -half, 0f));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < segments; i++)
            {
                int top = i * 2;
                int nextTop = (i + 1) * 2;
                triangles.Add(topCentre);
                triangles.Add(nextTop);
                triangles.Add(top);

                int bottom = top + 1;
                int nextBottom = nextTop + 1;
                triangles.Add(bottomCentre);
                triangles.Add(bottom);
                triangles.Add(nextBottom);
            }

            var mesh = new Mesh { name = "SmallPot TaperedCylinder" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Creates a billboarded sprite: used for badges, the contact shadow and the small props.
        /// These are symbols rather than objects, so they always face the camera.
        /// </summary>
        public static GameObject SpriteObject(string name, Transform parent, Sprite sprite, Color color, int sortingOrder)
        {
            var instance = new GameObject(name);
            if (parent != null)
            {
                instance.transform.SetParent(parent, false);
            }

            var renderer = instance.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            if (SpriteMaterial != null)
            {
                renderer.sharedMaterial = SpriteMaterial;
            }

            return instance;
        }

        /// <summary>Creates a primitive with its collider removed: nothing here needs physics.</summary>
        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;

            Collider collider = instance.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            if (parent != null)
            {
                instance.transform.SetParent(parent, false);
            }

            var renderer = instance.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return instance;
        }

        /// <summary>Creates an empty GameObject that renders a supplied mesh.</summary>
        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var instance = new GameObject(name);
            if (parent != null)
            {
                instance.transform.SetParent(parent, false);
            }

            var filter = instance.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return instance;
        }

        public static void ReleaseAll()
        {
            foreach (Material material in Owned)
            {
                if (material != null)
                {
                    Object.DestroyImmediate(material);
                }
            }

            Owned.Clear();
            MaterialCache.Clear();
        }
    }
}
