using UnityEngine;
using UnityEngine.EventSystems;

namespace SmallPot
{
    /// <summary>
    /// The squish under a press, for every button the kit builds.
    ///
    /// It listens for the pointer itself rather than relying on the Button's own transitions, because a
    /// press that is felt on the way down is a different thing from a colour change on the way up. The
    /// scale it returns to is the one the button was authored with, captured once.
    ///
    /// It sits in its own file because Unity keeps one script asset per file, named after the file. A
    /// second component class inside another file has no script asset of its own, so adding it in the
    /// editor makes Unity embed a script object directly into the scene - and a player build cannot read
    /// a scene that contains one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonJuice : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("How far it squashes while held. Zero uses the value from data/dev.json.")]
        public float PressedScale;

        private RectTransform _rect;
        private Vector3 _home;
        private bool _held;

        private void Awake()
        {
            _rect = transform as RectTransform;
            _home = _rect != null ? _rect.localScale : Vector3.one;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _held = true;
            float scale = PressedScale > 0f ? PressedScale : Juice.ButtonPressedScale;
            Juice.Press(_rect, _home, scale);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_held)
            {
                _held = false;
                Juice.Release(_rect, _home);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Dragged off the button: let it back up rather than leaving it squashed.
            if (_held)
            {
                _held = false;
                Juice.Release(_rect, _home);
            }
        }

        private void OnDisable()
        {
            _held = false;
            if (_rect != null)
            {
                _rect.localScale = _home;
            }
        }
    }
}
