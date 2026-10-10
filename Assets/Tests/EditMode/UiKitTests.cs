using NUnit.Framework;
using UnityEngine;

namespace SmallPot.Tests
{
    /// <summary>
    /// The interface is authored once and then belongs to whoever opens the scene. These tests hold that
    /// promise down: a widget that is already there keeps the layout and the parameters it was given, and
    /// only the words keep coming from the data.
    /// </summary>
    public class UiKitTests
    {
        private GameObject _canvas;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Canvas", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_canvas);
        }

        [Test]
        public void AWidgetThatWasAlreadyInTheSceneKeepsItsOwnLayoutAndParameters()
        {
            RectTransform rect = UiKit.Place(
                UiKit.Node("Panel", _canvas.transform),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(100f, 50f));
            var text = UiKit.Text(rect, "first", 30f, Color.black);

            // What finishing the look by hand looks like: a nudged rect, a bigger font, another colour.
            rect.sizeDelta = new Vector2(240f, 90f);
            rect.anchoredPosition = new Vector2(12f, 34f);
            text.fontSize = 44f;
            text.color = Color.red;

            // The next build, which is what a reload at runtime is.
            RectTransform again = UiKit.Place(
                UiKit.Node("Panel", _canvas.transform),
                new Vector2(0f, 1f),
                new Vector2(500f, 500f),
                new Vector2(10f, 10f));
            var againText = UiKit.Text(rect, "second", 30f, Color.black);

            Assert.AreSame(rect, again, "the widget already in the scene is reused, not replaced");
            Assert.AreSame(text, againText);
            Assert.AreEqual(new Vector2(240f, 90f), rect.sizeDelta, "the size given by hand survives");
            Assert.AreEqual(new Vector2(12f, 34f), rect.anchoredPosition, "the place given by hand survives");
            Assert.AreEqual(44f, text.fontSize, "the size given by hand survives");
            Assert.AreEqual(Color.red, text.color, "the colour given by hand survives");
            StringAssert.DoesNotContain("first", text.text, "the words still come from the data");
            Assert.IsNotEmpty(text.text);
        }

        [Test]
        public void AWidgetBuiltForTheFirstTimeIsAuthoredFromTheCode()
        {
            RectTransform rect = UiKit.Place(
                UiKit.Node("Fresh", _canvas.transform),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(100f, 50f));
            var text = UiKit.Text(rect, "hello", 30f, Color.black);

            Assert.AreEqual(new Vector2(100f, 50f), rect.sizeDelta);
            Assert.AreEqual(Vector2.zero, rect.anchoredPosition);
            Assert.AreEqual(30f, text.fontSize);
            Assert.AreEqual(Color.black, text.color);
            Assert.IsNotEmpty(text.text);
        }
    }
}
