using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Position, rotation and scale in one call.
    ///
    /// The scene is assembled from code, and the three-line version of this appears often enough to be
    /// worth naming.
    /// </summary>
    public static class TransformExtensions
    {
        public static Transform SetLocalTRS(this Transform transform, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
            return transform;
        }

        public static Transform SetLocalTRS(this Transform transform, Vector3 position, Vector3 scale) =>
            transform.SetLocalTRS(position, Quaternion.identity, scale);
    }
}
