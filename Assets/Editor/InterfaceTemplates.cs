using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmallPot.EditorTools
{
    /// <summary>
    /// Creates the two cards that are otherwise built entirely from code, and leaves them in the scene as
    /// ordinary objects that can be moved by hand.
    ///
    /// Both cards were written to be authored eventually: every builder in UiKit looks for a child of the
    /// right name first and adopts it when it finds one, so a card placed by hand survives a rebuild and
    /// keeps whatever was done to it. What was missing was a way to get those objects into the scene at all
    /// - until they exist there is nothing to nudge. This creates them through the views themselves rather
    /// than by copying their numbers out, so the result is the runtime layout by construction and cannot
    /// drift away from it as the views change.
    ///
    /// Nothing here runs on its own. There is no import hook, no play-mode hook and nothing marked
    /// execute-in-edit-mode: the scene is touched only when the menu item below is picked, and even then it
    /// is left unsaved, so the decision to keep the change stays with whoever made it.
    /// </summary>
    public static class InterfaceTemplates
    {
        /// <summary>The names the two views look their own root up by. These must match character for character.</summary>
        private const string NoticeRoot = "Notice";

        private const string UpdateRoot = "Update Prompt";

        [MenuItem("Window/SmallPot/Create Notice And Update Prompt Templates")]
        public static void CreateTemplates()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning(
                    "[SmallPot] No scene is open, so no templates were created. Open Main.unity first.");
                return;
            }

            // The canvas is found through the same call the game uses, so the objects land under the canvas
            // the runtime actually builds into. A missing GameController means this is not the game's scene,
            // and creating a canvas here would produce something that merely looks right standing still.
            var controller = UnityEngine.Object.FindObjectOfType<GameController>(true);
            if (controller == null)
            {
                Debug.LogWarning(
                    "[SmallPot] The open scene has no GameController, so there is no canvas to add to. Open " +
                    "Main.unity, or run Window/SmallPot/Generate Project first.");
                return;
            }

            Canvas canvas = UiKit.CreateCanvas("UI", controller.transform);

            // The real words, read straight off disk. A card with empty labels could not be positioned, and
            // one holding key names would be positioned against words that are not the ones shown.
            StringsStore strings = DataLoader.LoadBundledNow().Strings;

            // Whether each root was already there, and whether it was visible, so a second run leaves the
            // scene as it found it instead of switching things back on behind the user's back.
            Transform notice = canvas.transform.Find(NoticeRoot);
            Transform update = canvas.transform.Find(UpdateRoot);

            // Built by the views rather than by hand: there is then one description of this layout in the
            // project, and it is the one the game runs.
            new NoticeView(strings).Build(canvas.transform);
            new UpdatePromptView(strings, null).Build(canvas.transform);

            int marked = Adopt(canvas.transform, NoticeRoot) + Adopt(canvas.transform, UpdateRoot);

            Show(canvas.transform, NoticeRoot, notice != null, notice != null && notice.gameObject.activeSelf);
            Show(canvas.transform, UpdateRoot, update != null, update != null && update.gameObject.activeSelf);

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"[SmallPot] Templates are in the scene under '{HierarchyPath(canvas.transform)}': " +
                $"{NoticeRoot} and {UpdateRoot}, {marked} widget(s) marked as authored. Move the text where " +
                "you want it, then save the scene. Running this again reuses the objects already there, so " +
                "nothing is duplicated and nothing you moved is reset. To hand them back to the code, run " +
                "Window/SmallPot/Rebuild Interface From Code.");
        }

        /// <summary>
        /// Marks a card and everything under it as authored, and returns how many markers it added.
        ///
        /// The whole subtree rather than the root alone. The root's marker is what stops the card being laid
        /// out from the code, but it is each label's own marker that preserves that label's position, size,
        /// colour and alignment on the next build. Marked at the root alone, every label would snap back to
        /// the code's numbers on the first frame - which is precisely what this menu item exists to avoid.
        ///
        /// Idempotent by the check rather than by the call, because AuthoredWidget refuses a second copy of
        /// itself and a silent refusal would be counted as a marker that was never added.
        /// </summary>
        private static int Adopt(Transform canvas, string rootName)
        {
            Transform root = canvas != null ? canvas.Find(rootName) : null;
            if (root == null)
            {
                return 0;
            }

            int added = 0;
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.GetComponent<AuthoredWidget>() == null)
                {
                    rect.gameObject.AddComponent<AuthoredWidget>();
                    added++;
                }
            }

            return added;
        }

        /// <summary>
        /// Leaves a card visible the first time it is created, and otherwise puts back whatever it was.
        ///
        /// Both cards hide themselves at the end of their own build. That is right at runtime and useless
        /// here, because an object that is switched off cannot be positioned in the scene view. Restoring the
        /// previous state on later runs is what stops this item from undoing a deliberate decision to switch
        /// a finished card off before committing the scene.
        /// </summary>
        private static void Show(Transform canvas, string rootName, bool wasPresent, bool wasActive)
        {
            Transform root = canvas != null ? canvas.Find(rootName) : null;
            if (root == null)
            {
                return;
            }

            // Present means the state belongs to the user; absent means this run created it and the only
            // useful thing to do with a brand new template is to put it where it can be seen.
            bool wanted = wasPresent ? wasActive : true;
            if (root.gameObject.activeSelf != wanted)
            {
                root.gameObject.SetActive(wanted);
            }
        }

        /// <summary>The full path of an object in its scene, for a log line that can be followed.</summary>
        private static string HierarchyPath(Transform transform)
        {
            var builder = new StringBuilder(transform.name);
            for (Transform walk = transform.parent; walk != null; walk = walk.parent)
            {
                builder.Insert(0, walk.name + "/");
            }

            return builder.ToString();
        }
    }
}
