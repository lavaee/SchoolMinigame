using System;
using RTLTMPro;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SmallPot
{
    /// <summary>A button plus the accessibility-relevant parts around it.</summary>
    public sealed class LabeledButton
    {
        public RectTransform Rect;
        public Button Button;
        public Image Background;
        public RTLTextMeshPro Label;

        public GameObject Root => Rect != null ? Rect.gameObject : null;

        /// <summary>Disables the button and greys it out, without changing the text.</summary>
        public void SetInteractable(bool interactable)
        {
            if (Button != null)
            {
                Button.interactable = interactable;
            }
        }

        /// <summary>Swaps the icon shown on the button, used for the "not enough drops" state.</summary>
        public Image Icon { get; set; }
    }

    /// <summary>
    /// Builds the interface from code.
    ///
    /// Two decisions are collected here rather than scattered through the views. The first is that all
    /// user-visible text is an RTLTextMeshPro component, configured in exactly one place, so Persian
    /// shaping and the Persian-digit rule cannot be applied inconsistently. The second is that the
    /// layout is anchored explicitly instead of using layout groups, which keeps the top bar and the
    /// bottom action area stable when the aspect ratio changes between a phone and a tablet.
    /// </summary>
    public static class UiKit
    {
        /// <summary>Assigned at boot from the generated Persian font asset.</summary>
        public static TMP_FontAsset Font;

        /// <summary>Design resolution; the canvas scales by width, so tablets simply get more height.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(720f, 1280f);

        public const float Margin = 32f;

        // Alignment for centred text is unambiguous; paragraphs use these two, which are defined once
        // so that TMP's RTL alignment flip can be verified in one place instead of guessed per view.
        public const TextAlignmentOptions Centred = TextAlignmentOptions.Center;
        public const TextAlignmentOptions Paragraph = TextAlignmentOptions.TopRight;
        public const TextAlignmentOptions ParagraphCentred = TextAlignmentOptions.Top;

        /// <summary>
        /// The interface canvas. Found rather than created when the scene already has one, so the baked
        /// canvas and the runtime canvas are the same object.
        /// </summary>
        public static Canvas CreateCanvas(string name, Transform parent)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            GameObject gameObject;

            if (existing != null)
            {
                gameObject = existing.gameObject;
            }
            else
            {
                gameObject = new GameObject(name);
                if (parent != null)
                {
                    gameObject.transform.SetParent(parent, false);
                }
            }

            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            GetOrAdd<GraphicRaycaster>(gameObject);
            return canvas;
        }

        public static void EnsureEventSystem(Transform parent)
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var gameObject = new GameObject("EventSystem");
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            gameObject.AddComponent<EventSystem>();
            gameObject.AddComponent<StandaloneInputModule>();
        }

        /// <summary>Gets a component or adds it, so running a builder twice never stacks duplicates.</summary>
        public static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T found = target.GetComponent<T>();
            return found != null ? found : target.AddComponent<T>();
        }

        /// <summary>
        /// Finds a child by name, or creates it.
        ///
        /// Finding first is what lets the same builder run twice: the scene baker runs it once in the
        /// editor to place the interface in the scene, and the game runs it again at startup to refresh
        /// the labels from the current data. The second run updates what is already there rather than
        /// replacing it, which is what preserves interface art you replaced by hand.
        /// </summary>
        public static RectTransform Node(string name, Transform parent)
        {
            if (parent != null)
            {
                var existing = parent.Find(name) as RectTransform;
                if (existing != null)
                {
                    return existing;
                }
            }

            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return (RectTransform)gameObject.transform;
        }

        /// <summary>Fills the parent, inset by the supplied amounts on each side.</summary>
        public static RectTransform Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>Pins a fixed-size element to a point in the parent, using that point as the pivot.</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>A full-width band pinned to the top or the bottom of the parent.</summary>
        public static RectTransform Band(RectTransform rect, bool fromTop, float distance, float height, float margin = Margin)
        {
            float y = fromTop ? 1f : 0f;
            rect.anchorMin = new Vector2(0f, y);
            rect.anchorMax = new Vector2(1f, y);
            rect.pivot = new Vector2(0.5f, y);
            rect.sizeDelta = new Vector2(-margin * 2f, height);
            rect.anchoredPosition = new Vector2(0f, fromTop ? -distance : distance);
            return rect;
        }

        /// <summary>
        /// A rounded panel. The sprite is only assigned when the image has none, so a background you
        /// replaced with your own art survives every rebuild; the colour keeps being driven, because the
        /// colour is what carries state.
        /// </summary>
        public static Image Panel(string name, Transform parent, Color color, int radius = 22)
        {
            RectTransform rect = Node(name, parent);
            var image = GetOrAdd<Image>(rect.gameObject);

            if (image.sprite == null)
            {
                image.sprite = TextureFactory.RoundedRect(radius: radius);
                image.type = Image.Type.Sliced;
            }

            image.color = color;
            return image;
        }

        public static Image Solid(string name, Transform parent, Color color)
        {
            RectTransform rect = Node(name, parent);
            var image = GetOrAdd<Image>(rect.gameObject);
            if (image.sprite == null)
            {
                // An Image with no sprite draws nothing at all, so a solid rectangle needs the one-pixel one.
                image.sprite = TextureFactory.Solid();
            }

            image.color = color;
            return image;
        }

        public static Image Icon(string name, Transform parent, Sprite sprite, Color color)
        {
            RectTransform rect = Node(name, parent);
            var image = GetOrAdd<Image>(rect.gameObject);

            if (image.sprite == null)
            {
                image.sprite = sprite;
            }

            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        /// <summary>
        /// The single place where a text element is configured.
        ///
        /// <paramref name="forceFix"/> is true for Persian copy and false for Latin-only strings such as
        /// a server URL or a file name: RTLTMPro shapes any text that starts with an RTL character, but a
        /// Latin string must be left exactly as written, and its full-string reversal would otherwise
        /// turn a URL inside out.
        /// </summary>
        public static RTLTextMeshPro Text(
            string name,
            Transform parent,
            string content,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment = Centred,
            bool forceFix = true,
            FontStyles style = FontStyles.Normal,
            bool wrap = true)
        {
            RectTransform rect = Node(name, parent);
            return Text(rect, content, fontSize, color, alignment, forceFix, style, wrap);
        }

        public static RTLTextMeshPro Text(
            RectTransform rect,
            string content,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment = Centred,
            bool forceFix = true,
            FontStyles style = FontStyles.Normal,
            bool wrap = true)
        {
            var text = GetOrAdd<RTLTextMeshPro>(rect.gameObject);

            if (Font != null)
            {
                text.font = Font;
            }

            // Flags first, then the content: the component shapes the string as it is assigned.
            text.Farsi = true;
            text.FixTags = true;
            text.PreserveNumbers = true;
            text.ForceFix = forceFix;

            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.enableWordWrapping = wrap;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;

            text.text = content ?? string.Empty;
            return text;
        }

        public static LabeledButton Button(
            string name,
            Transform parent,
            string label,
            float labelSize,
            Color background,
            Color labelColor,
            UnityAction onClick,
            Sprite icon = null)
        {
            RectTransform rect = Node(name, parent);
            var image = GetOrAdd<Image>(rect.gameObject);

            if (image.sprite == null)
            {
                image.sprite = TextureFactory.RoundedRect();
                image.type = Image.Type.Sliced;
            }

            image.color = background;

            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            if (onClick != null)
            {
                // Cleared first: the builders run again on every data reload, and stacking the same
                // handler twice would fire it twice.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => AudioDirector.Play(Sound.Click));
                button.onClick.AddListener(onClick);
            }

            var result = new LabeledButton
            {
                Rect = rect,
                Button = button,
                Background = image
            };

            RectTransform labelRect = Stretch(Node("Label", rect), 16f, 4f, 16f, 4f);
            result.Label = Text(labelRect, label, labelSize, labelColor);

            if (icon != null)
            {
                Image iconImage = Icon("Icon", rect, icon, labelColor);
                Place(iconImage.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(38f, 38f));
                Stretch(labelRect, 64f, 4f, 16f, 4f);
                result.Icon = iconImage;
            }

            return result;
        }

        /// <summary>Adds a soft drop shadow behind a panel, which separates it from the scene behind.</summary>
        public static void AddShadow(RectTransform target, float distance = 8f, float alpha = 0.18f)
        {
            var shadow = GetOrAdd<Shadow>(target.gameObject);
            shadow.effectColor = new Color(0f, 0f, 0f, alpha);
            shadow.effectDistance = new Vector2(0f, -distance);
        }

        public static CanvasGroup Group(GameObject target)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
            }

            return group;
        }

        /// <summary>A full-screen click blocker, so modal panels cannot leak clicks to the HUD.</summary>
        public static Image Scrim(string name, Transform parent, UnityAction onClick = null)
        {
            RectTransform rect = Node(name, parent);
            Stretch(rect);
            var image = GetOrAdd<Image>(rect.gameObject);
            if (image.sprite == null)
            {
                // The stretch above is what blocks the clicks; the sprite is what makes the dimming visible,
                // because an Image with no sprite draws nothing.
                image.sprite = TextureFactory.Solid();
            }

            image.color = Palette.Scrim;

            if (onClick != null)
            {
                var button = GetOrAdd<Button>(rect.gameObject);
                button.transition = Selectable.Transition.None;
                button.targetGraphic = image;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(onClick);
            }

            return image;
        }

        /// <summary>
        /// A one-line text field. The kit had none, and the connection gate needs one: a server address
        /// that turns out to be wrong has to be fixable on the device, not by rebuilding the app.
        ///
        /// Persian runs right to left, so the field, its placeholder and its caret all sit on the right.
        /// An address is Latin and must not be reshaped or reversed, which is what forceFix false means.
        /// </summary>
        public static TMP_InputField Input(
            string name,
            Transform parent,
            string placeholder,
            float fontSize,
            Color background,
            Color ink,
            Color placeholderInk)
        {
            RectTransform rect = Node(name, parent);
            var panel = GetOrAdd<Image>(rect.gameObject);
            if (panel.sprite == null)
            {
                panel.sprite = TextureFactory.RoundedRect();
                panel.type = Image.Type.Sliced;
            }

            panel.color = background;

            RectTransform viewport = Node("Viewport", rect);
            Stretch(viewport, 18f, 8f, 18f, 8f);
            GetOrAdd<RectMask2D>(viewport.gameObject);

            RTLTextMeshPro text = Text(
                "Text", viewport, string.Empty, fontSize, ink, TextAlignmentOptions.MidlineRight, false);
            Stretch(text.rectTransform);

            RTLTextMeshPro hint = Text(
                "Placeholder", viewport, placeholder, fontSize, placeholderInk,
                TextAlignmentOptions.MidlineRight, false);
            Stretch(hint.rectTransform);

            var field = GetOrAdd<TMP_InputField>(rect.gameObject);
            field.targetGraphic = panel;
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 120;
            return field;
        }
    }
}
