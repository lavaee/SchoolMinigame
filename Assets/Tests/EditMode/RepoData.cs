using System.IO;
using NUnit.Framework;
using SmallPot;
using UnityEngine;

namespace SmallPot.Tests
{
    /// <summary>Helpers for tests that read the real data files shipped with the project.</summary>
    public static class RepoData
    {
        /// <summary>The repo's data folder, which is the same content that is copied to StreamingAssets.</summary>
        public static string DataDirectory
        {
            get
            {
                string path = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", "data"));
                Assert.IsTrue(Directory.Exists(path), $"Data directory not found: {path}");
                return path;
            }
        }

        public static string PathOf(string fileName) =>
            System.IO.Path.Combine(DataDirectory, fileName);

        public static string Text(string fileName)
        {
            string path = PathOf(fileName);
            Assert.IsTrue(File.Exists(path), $"Data file not found: {path}");
            return File.ReadAllText(path);
        }

        public static GameConfig Config() => GameConfig.FromJsonText(Text(GameConfig.ConfigFileName));

        public static StringsStore Strings()
        {
            var store = StringsStore.CreateEmpty();

            // Same order as the loader: lowest priority first, so the supplied file wins.
            store.Merge(StringsStore.ExtraFileName, JsonValue.Parse(Text(StringsStore.ExtraFileName)));
            store.Merge(StringsStore.BaseFileName, JsonValue.Parse(Text(StringsStore.BaseFileName)));
            store.Merge(StringsStore.OverrideFileName, JsonValue.Parse(Text(StringsStore.OverrideFileName)));
            return store;
        }
    }
}
