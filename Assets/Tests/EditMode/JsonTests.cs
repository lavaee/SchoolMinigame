using NUnit.Framework;
using SmallPot;

namespace SmallPot.Tests
{
    /// <summary>
    /// The JSON reader is written by hand rather than pulled from a package, so it carries the whole
    /// data-driven contract on its own and is worth testing directly.
    /// </summary>
    public sealed class JsonTests
    {
        [Test]
        public void ParsesPrimitives()
        {
            Assert.AreEqual(JsonKind.Number, JsonValue.Parse("42").Kind);
            Assert.AreEqual(42, JsonValue.Parse("42").AsInt(0));
            Assert.AreEqual(-1.5f, JsonValue.Parse("-1.5").AsFloat(0f), 0.0001f);
            Assert.AreEqual(JsonKind.String, JsonValue.Parse("\"hello\"").Kind);
            Assert.AreEqual("hello", JsonValue.Parse("\"hello\"").AsString());
            Assert.IsTrue(JsonValue.Parse("true").AsBool(false));
            Assert.IsTrue(JsonValue.Parse("null").IsNull);
        }

        [Test]
        public void ParsesNestedDocuments()
        {
            const string text = @"{
                ""a"": { ""b"": [1, 2, { ""c"": ""x"" }] },
                ""empty"": {},
                ""list"": [],
                ""escaped"": ""line\nquote\""slash\\""
            }";

            JsonValue root = JsonValue.Parse(text);
            Assert.AreEqual(JsonKind.Object, root.Kind);
            Assert.AreEqual("x", root["a"]["b"][2]["c"].AsString());
            Assert.AreEqual(2, root["a"]["b"][1].AsInt(0));
            Assert.AreEqual(0, root["empty"].Count);
            Assert.AreEqual(0, root["list"].Count);
            Assert.AreEqual("line\nquote\"slash\\", root["escaped"].AsString());
        }

        [Test]
        public void MissingKeysAndWrongTypesAreTotal()
        {
            JsonValue root = JsonValue.Parse(@"{ ""number"": 5, ""text"": ""x"" }");

            Assert.IsFalse(root.Has("nope"));
            Assert.IsTrue(root["nope"].IsNull);
            Assert.AreEqual("fallback", root["nope"].AsString("fallback"));
            Assert.AreEqual(7, root["nope"].AsInt(7));
            Assert.AreEqual(0, root["text"].AsInt(0));
            Assert.AreEqual("fallback", root["number"].AsString("fallback"));
            Assert.IsFalse(root["number"].AsBool(false));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{")]
        [TestCase("{ \"a\": }")]
        [TestCase("[1, 2")]
        [TestCase("{\"a\": 1} trailing")]
        [TestCase("{'a': 1}")]
        public void MalformedInputFailsWithAMessage(string text)
        {
            Assert.IsFalse(JsonValue.TryParse(text, out JsonValue value, out string error));
            Assert.IsTrue(value.IsNull, "A failed parse has to yield the null value, never a half-parsed one.");
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [Test]
        public void ParseThrowsForMalformedInput()
        {
            Assert.Throws<JsonParseException>(() => JsonValue.Parse("{ oops }"));
        }

        [Test]
        public void HandlesUnicodeEscapes()
        {
            // The Persian data files are stored as literal UTF-8, but the reader still has to cope
            // with an escaped form in case a file is ever re-encoded.
            JsonValue root = JsonValue.Parse(@"{ ""s"": ""\u0645\u0627\u0646\u062f\u0646"" }");
            Assert.AreEqual("\u0645\u0627\u0646\u062f\u0646", root["s"].AsString());
            Assert.AreEqual(5, root["s"].AsString().Length);
        }
    }
}
