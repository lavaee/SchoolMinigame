using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SmallPot
{
    /// <summary>Where the text of a data file came from. Surfaced in the settings panel.</summary>
    public enum DataSource
    {
        None,
        Bundled,
        Cache,
        Remote
    }

    /// <summary>Result of resolving one data file through the three layers.</summary>
    public sealed class LoadedFile
    {
        public string FileName;
        public string Text;
        public DataSource Source = DataSource.None;
        public readonly List<string> Errors = new List<string>();

        public bool Ok => Text != null;

        public string Describe()
        {
            switch (Source)
            {
                case DataSource.Remote: return "remote";
                case DataSource.Cache: return "cache";
                case DataSource.Bundled: return "bundled";
                default: return "missing";
            }
        }
    }

    /// <summary>Everything the game needs to run, plus where each piece came from.</summary>
    public sealed class DataBundle
    {
        public GameConfig Config = GameConfig.Defaults();
        public StringsStore Strings = StringsStore.CreateEmpty();
        public PairsData Pairs = new PairsData();
        public DevSettings Dev = new DevSettings();

        public readonly Dictionary<string, LoadedFile> Files = new Dictionary<string, LoadedFile>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Warnings = new List<string>();

        public DataSource SourceOf(string fileName) =>
            Files.TryGetValue(fileName, out LoadedFile file) ? file.Source : DataSource.None;

        public bool AnyFileFromRemote
        {
            get
            {
                foreach (KeyValuePair<string, LoadedFile> pair in Files)
                {
                    if (pair.Value.Source == DataSource.Remote)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void Note(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Warnings.Add(message);
            }
        }
    }

    /// <summary>
    /// Resolves data/*.json through three layers, the first that yields valid JSON winning:
    ///
    ///   1. remote  - the data server, so the files can be replaced without rebuilding the app
    ///   2. cache   - the last successful remote copy, kept in persistentDataPath/data
    ///   3. bundled - the copy inside the APK, which is also the employer's pristine revision
    ///
    /// The bundled layer means the game is always playable with no server, no network and no cache.
    /// Every layer is validated by parsing it, so an HTML error page from a proxy or a half-finished
    /// edit cannot displace good data with garbage - it is rejected and the next layer is used.
    /// </summary>
    public sealed class DataLoader
    {
        public const string DataFolderName = "data";

        /// <summary>The files the game reads, in the order they are resolved.</summary>
        public static readonly string[] AllFiles =
        {
            DevSettings.FileName,
            GameConfig.ConfigFileName,
            StringsStore.BaseFileName,
            StringsStore.OverrideFileName,
            StringsStore.ExtraFileName,
            PairsData.FileName
        };

        /// <summary>Kept short so a phone on the wrong network still starts immediately.</summary>
        public float RemoteTimeoutSeconds = 3f;

        /// <summary>Every baked address, in order, so a poll can keep trying while none has answered yet.</summary>
        private readonly List<string> _serverCandidates = new List<string>();
        private bool _allowRemote;

        public static string CacheDirectory => Path.Combine(Application.persistentDataPath, DataFolderName);

        public static string BundledDirectory => Path.Combine(Application.streamingAssetsPath, DataFolderName);

        /// <summary>Base URL actually used for the most recent load.</summary>
        public string LastServerUrl { get; private set; } = string.Empty;

        /// <summary>
        /// Loads everything. <paramref name="serverUrlOverride"/> comes from the saved settings and
        /// wins over data/dev.json, so the server can be repointed from the phone itself.
        /// </summary>
        public IEnumerator Load(string serverUrlOverride, bool allowRemote, Action<DataBundle> done)
        {
            // The cache belongs to the installed build, not to the app forever. A new build ships new
            // files, and a cached copy of a file the build has since gained keys in would hide them - which
            // is exactly the sort of thing that looks like "the app is in English and I cannot fix it".
            EnsureCacheMatchesAppVersion();

            // Phase 1: learn the base URL from dev.json on disk only. The remote layer cannot be used
            // to discover where the remote layer is.
            var localDev = new Dictionary<string, LoadedFile>(StringComparer.OrdinalIgnoreCase);
            yield return Resolve(DevSettings.FileName, string.Empty, localDev);

            DevSettings dev = null;
            if (localDev.TryGetValue(DevSettings.FileName, out LoadedFile devFile) && devFile.Ok &&
                JsonValue.TryParse(devFile.Text, out JsonValue devRoot, out _))
            {
                dev = DevSettings.FromJson(devRoot);
            }

            // A phone reaches the same server at a different address depending on how it is connected:
            // the machine on the network over Wi-Fi, and 127.0.0.1 when the port is forwarded over USB.
            // Every baked address is tried here and the first that answers is the one used from then on.
            _serverCandidates.Clear();
            _serverCandidates.AddRange(CandidateUrls(serverUrlOverride, dev));
            _allowRemote = allowRemote && _serverCandidates.Count > 0;
            LastServerUrl = string.Empty;

            if (_allowRemote)
            {
                yield return Probe(_serverCandidates, url => LastServerUrl = url);
            }

            // Phase 2: resolve every file, now with the remote layer available.
            var files = new Dictionary<string, LoadedFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string fileName in AllFiles)
            {
                yield return Resolve(fileName, LastServerUrl, files);
            }

            done?.Invoke(Build(files));
        }

        /// <summary>Loads without touching the network. Used by tests and by the offline path.</summary>
        public IEnumerator LoadFromDiskOnly(Action<DataBundle> done)
        {
            yield return Load(string.Empty, false, done);
        }

        /// <summary>
        /// Asks the server which revision of its content it is serving, so a running client can notice a
        /// push. Reports -1 whenever there is no server or no readable answer, because a failed poll only
        /// ever means "nothing is waiting" - it is not an error worth surfacing.
        /// </summary>
        public IEnumerator CheckVersion(string serverUrlOverride, Action<int> done)
        {
            string baseUrl = null;
            yield return ResolveBaseUrl(serverUrlOverride, url => baseUrl = url);

            if (string.IsNullOrEmpty(baseUrl))
            {
                done?.Invoke(-1);
                yield break;
            }

            string text = null;
            yield return FetchRemote(
                baseUrl.TrimEnd('/') + "/version",
                value => text = value,
                _ => { });

            done?.Invoke(ReadVersion(text));
        }

        /// <summary>
        /// Waits for the server's content revision to stop matching <paramref name="since"/>.
        ///
        /// The server holds this request open and the push itself answers it, so the app hears about new
        /// content in the time one round trip takes rather than, on average, half a poll interval. A
        /// negative <paramref name="since"/> asks "what is the revision now?" and is answered at once, which
        /// is how the first watch establishes its baseline.
        ///
        /// Reports -1 when nothing answered, exactly as <see cref="CheckVersion"/> does, because a watch
        /// that failed only means "nothing is waiting".
        /// </summary>
        public IEnumerator AwaitVersion(string serverUrlOverride, int since, float holdSeconds, Action<int> done)
        {
            string baseUrl = null;
            yield return ResolveBaseUrl(serverUrlOverride, url => baseUrl = url);

            if (string.IsNullOrEmpty(baseUrl))
            {
                done?.Invoke(-1);
                yield break;
            }

            int hold = Mathf.Clamp(Mathf.RoundToInt(holdSeconds), 2, 60);
            string text = null;
            yield return FetchRemote(
                baseUrl.TrimEnd('/') + "/api/watch?version=" + since + "&hold=" + hold,
                value => text = value,
                _ => { },
                // The request has to outlive the hold, or every quiet watch would come back as a failure
                // instead of as "nothing has changed yet".
                hold + 6f);

            done?.Invoke(ReadVersion(text));
        }

        /// <summary>The revision in a /version or /api/watch answer, or -1 when there is none to read.</summary>
        private static int ReadVersion(string text)
        {
            if (string.IsNullOrEmpty(text) || !JsonValue.TryParse(text, out JsonValue root, out _))
            {
                return -1;
            }

            return root["version"].AsInt(-1);
        }

        /// <summary>
        /// The address to talk to: the override, then whatever last answered, then a fresh probe.
        ///
        /// The probe is what lets an app that started before the server did connect on its own instead of
        /// waiting for someone to reload by hand.
        /// </summary>
        private IEnumerator ResolveBaseUrl(string serverUrlOverride, Action<string> done)
        {
            string baseUrl = FirstNonEmpty(serverUrlOverride, LastServerUrl);

            if (string.IsNullOrEmpty(baseUrl) && _serverCandidates.Count > 0)
            {
                yield return Probe(_serverCandidates, url => LastServerUrl = url);
                baseUrl = LastServerUrl;
            }

            done?.Invoke(baseUrl);
        }

        /// <summary>
        /// Reads the bundled files straight off disk with no coroutine, so editor tools can have the real
        /// data outside play mode. A missing file simply becomes a default, exactly as it does at runtime.
        /// </summary>
        public static DataBundle LoadBundledNow()
        {
            var files = new Dictionary<string, LoadedFile>(StringComparer.OrdinalIgnoreCase);

            foreach (string fileName in AllFiles)
            {
                var file = new LoadedFile { FileName = fileName };
                string path = Path.Combine(BundledDirectory, fileName);

                if (File.Exists(path))
                {
                    file.Text = File.ReadAllText(path, Encoding.UTF8);
                    file.Source = DataSource.Bundled;
                }

                files[fileName] = file;
            }

            return Build(files);
        }

        /// <summary>
        /// The addresses to try, in order: an explicit override first, then whatever the build baked.
        /// Static and pure so the order can be pinned by a test without touching a network.
        /// </summary>
        public static List<string> CandidateUrls(string overrideUrl, DevSettings dev)
        {
            var urls = new List<string>();

            Add(overrideUrl);
            if (dev != null)
            {
                foreach (string url in dev.DataServerUrls)
                {
                    Add(url);
                }

                Add(dev.DataServerUrl);
            }

            // A phone cannot reach the machine's own loopback address, so on a device an address pointing
            // there is dropped rather than offered, listed or suggested. The exception is an address the
            // player typed in themselves: that is an instruction, and typing 127.0.0.1 is how a USB cable
            // with "adb reverse" is used. Doing this here rather than only in the build means a stale
            // address in a cached or served file cannot bring loopback back either.
            if (Application.isMobilePlatform)
            {
                string typed = Normalise(overrideUrl);
                urls.RemoveAll(url =>
                    IsLoopbackAddress(url) && !string.Equals(url, typed, StringComparison.OrdinalIgnoreCase));
            }

            return urls;

            void Add(string url)
            {
                string trimmed = (url ?? string.Empty).Trim().TrimEnd('/');
                if (trimmed.Length == 0)
                {
                    return;
                }

                foreach (string existing in urls)
                {
                    if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                urls.Add(trimmed);
            }
        }

        /// <summary>True when the address points back at the machine running the app.</summary>
        public static bool IsLoopbackAddress(string url)
        {
            string text = Normalise(url).ToLowerInvariant();
            int scheme = text.IndexOf("://", StringComparison.Ordinal);
            string authority = scheme >= 0 ? text.Substring(scheme + 3) : text;

            int end = authority.IndexOfAny(new[] { '/', '?' });
            if (end >= 0)
            {
                authority = authority.Substring(0, end);
            }

            int port = authority.LastIndexOf(':');
            string host = port >= 0 ? authority.Substring(0, port) : authority;

            return host == "localhost" || host == "::1" || host == "[::1]" ||
                   host == "127.0.0.1" || host.StartsWith("127.", StringComparison.Ordinal);
        }

        private static string Normalise(string url) => (url ?? string.Empty).Trim().TrimEnd('/');

        /// <summary>
        /// Tries each address once and reports the first that answers the version endpoint. Every failure
        /// is silent: the caller still has the cache and the bundled copy to fall back on, and a server
        /// that is simply not running is a normal way to play, not an error.
        /// </summary>
        private IEnumerator Probe(List<string> candidates, Action<string> winner)
        {
            foreach (string baseUrl in candidates)
            {
                bool answered = false;
                yield return FetchRemote(baseUrl + "/version", _ => answered = true, _ => { });

                if (answered)
                {
                    winner?.Invoke(baseUrl);
                    yield break;
                }
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// True for the file that says where the server is and how the app should behave. It comes from the
        /// app itself - the copy inside the build, or the copy the server is serving right now - and never
        /// from the cache, so a device cannot keep running on an address the build has moved on from.
        /// </summary>
        private static bool IsLocalConfiguration(string fileName) =>
            string.Equals(fileName, DevSettings.FileName, StringComparison.OrdinalIgnoreCase);

        private IEnumerator Resolve(string fileName, string serverUrl, Dictionary<string, LoadedFile> output)        {
            var file = new LoadedFile { FileName = fileName };

            // Layer 1: remote.
            if (!string.IsNullOrEmpty(serverUrl))
            {
                string url = serverUrl.TrimEnd('/') + "/" + fileName;
                string remoteText = null;
                string remoteError = null;
                yield return FetchRemote(url, text => remoteText = text, error => remoteError = error);

                if (!string.IsNullOrEmpty(remoteText) && IsValidJson(remoteText))
                {
                    file.Text = remoteText;
                    file.Source = DataSource.Remote;
                    WriteCache(fileName, remoteText);
                }
                else
                {
                    file.Errors.Add(string.IsNullOrEmpty(remoteError)
                        ? "remote: response was not valid JSON"
                        : "remote: " + remoteError);
                }
            }

            // Layer 2: cache. Skipped for the app's own configuration file: a stale cached dev.json keeps
            // pointing the app at an address the build has moved on from, and keeps a flag like requireServer
            // switched off, with nothing on screen to say the copy inside the app is being ignored. The
            // employer's content is cached exactly as before.
            if (file.Text == null && !IsLocalConfiguration(fileName))
            {
                string cached = ReadCache(fileName);
                if (!string.IsNullOrEmpty(cached) && IsValidJson(cached))
                {
                    file.Text = cached;
                    file.Source = DataSource.Cache;
                }
                else if (cached != null)
                {
                    file.Errors.Add("cache: stored copy was not valid JSON");
                }
            }

            // Layer 3: bundled.
            if (file.Text == null)
            {
                string bundledText = null;
                string bundledError = null;
                yield return ReadBundled(fileName, text => bundledText = text, error => bundledError = error);
                if (!string.IsNullOrEmpty(bundledText) && IsValidJson(bundledText))
                {
                    file.Text = bundledText;
                    file.Source = DataSource.Bundled;
                }
                else
                {
                    file.Errors.Add(string.IsNullOrEmpty(bundledError)
                        ? "bundled: response was not valid JSON"
                        : "bundled: " + bundledError);
                }
            }

            if (!file.Ok)
            {
                file.Errors.Add($"{fileName} could not be loaded from any source; defaults will be used.");
            }

            output[fileName] = file;
        }

        private IEnumerator FetchRemote(string url, Action<string> onSuccess, Action<string> onError, float timeoutSeconds = -1f)
        {
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = Mathf.Max(
                1, Mathf.CeilToInt(timeoutSeconds > 0f ? timeoutSeconds : RemoteTimeoutSeconds));

            // The server always serves the current file; caching is this class's job, not HTTP's.
            request.SetRequestHeader("Cache-Control", "no-cache");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
            else
            {
                onError?.Invoke(request.error ?? $"HTTP {request.responseCode}");
            }

            request.Dispose();
        }

        private static IEnumerator ReadBundled(string fileName, Action<string> onSuccess, Action<string> onError)
        {
            string path = Path.Combine(BundledDirectory, fileName);

            // On Android StreamingAssets lives inside the APK, so it has to be read through
            // UnityWebRequest; on desktop and in the editor it is an ordinary file.
            bool needsWebRequest = path.Contains("://") || path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);

            if (!needsWebRequest)
            {
                if (File.Exists(path))
                {
                    string text = null;
                    string error = null;
                    try
                    {
                        text = File.ReadAllText(path, Encoding.UTF8);
                    }
                    catch (Exception exception)
                    {
                        error = exception.Message;
                    }

                    if (text != null)
                    {
                        onSuccess?.Invoke(text);
                    }
                    else
                    {
                        onError?.Invoke(error);
                    }

                    yield break;
                }

                onError?.Invoke($"not found at {path}");
                yield break;
            }

            UnityWebRequest request = UnityWebRequest.Get(path);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
            else
            {
                onError?.Invoke(request.error ?? $"HTTP {request.responseCode}");
            }

            request.Dispose();
        }

        private static bool IsValidJson(string text) => JsonValue.TryParse(text, out _, out _);

        private static string ReadCache(string fileName)
        {
            try
            {
                string path = Path.Combine(CacheDirectory, fileName);
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (Exception exception)
            {
                Log.Warn($"Could not read cached {fileName}: {exception.Message}");
                return null;
            }
        }

        private static void WriteCache(string fileName, string text)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllText(Path.Combine(CacheDirectory, fileName), text, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Log.Warn($"Could not cache {fileName}: {exception.Message}");
            }
        }

        /// <summary>Names the build that wrote the cached copies.</summary>
        private const string CacheStampFileName = "cache.build";

        /// <summary>
        /// Identifies the build the cache belongs to.
        ///
        /// The version number alone is not enough: the build that changes what ships is often the same
        /// version with a different build, so the build's own identifier is what is compared.
        /// </summary>
        private static string CurrentBuildStamp =>
            (Application.version ?? string.Empty).Trim() + "+" + (Application.buildGUID ?? string.Empty).Trim();

        /// <summary>
        /// Removes the cached copies, so the next load falls back to the bundled revision.
        ///
        /// The stamp is written straight back: "forget the data" must not also mean "forget which build
        /// wrote it", or the next load would clear again and take a freshly planted cache with it.
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"Could not clear the data cache: {exception.Message}");
            }

            WriteCache(CacheStampFileName, CurrentBuildStamp);
        }

        /// <summary>Drops the cache when it was written by a different build of the app.</summary>
        private static void EnsureCacheMatchesAppVersion()
        {
            string stamp = Path.Combine(CacheDirectory, CacheStampFileName);
            try
            {
                if (File.Exists(stamp) && File.ReadAllText(stamp, Encoding.UTF8).Trim() == CurrentBuildStamp)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"Could not read the cache stamp: {exception.Message}");
            }

            Log.Info("This build has not written the cached data; starting from the files inside the app.");
            ClearCache();
        }

        private static DataBundle Build(Dictionary<string, LoadedFile> files)
        {
            var bundle = new DataBundle();
            foreach (KeyValuePair<string, LoadedFile> pair in files)
            {
                bundle.Files[pair.Key] = pair.Value;
                foreach (string error in pair.Value.Errors)
                {
                    bundle.Note(error);
                }
            }

            // strings-extra is merged first and strings.json last, so the override file always wins.
            MergeStrings(bundle, StringsStore.ExtraFileName);
            MergeStrings(bundle, StringsStore.BaseFileName);
            MergeStrings(bundle, StringsStore.OverrideFileName);

            if (TryGetText(files, GameConfig.ConfigFileName, out string configText))
            {
                bundle.Config = GameConfig.FromJsonText(configText);
            }
            else
            {
                bundle.Note($"{GameConfig.ConfigFileName} was unavailable; the built-in defaults are in use.");
            }

            foreach (string warning in bundle.Config.Warnings)
            {
                bundle.Note(warning);
            }

            if (TryGetText(files, PairsData.FileName, out string pairsText))
            {
                bundle.Pairs = PairsData.FromJsonText(pairsText, out string pairsError);
                if (!string.IsNullOrEmpty(pairsError))
                {
                    bundle.Note(pairsError);
                }
            }
            else
            {
                bundle.Note($"{PairsData.FileName} was unavailable; the minigame will explain that no words are loaded.");
            }

            foreach (string warning in bundle.Pairs.Warnings)
            {
                bundle.Note(warning);
            }

            if (TryGetText(files, DevSettings.FileName, out string devText) &&
                JsonValue.TryParse(devText, out JsonValue devRoot, out _))
            {
                bundle.Dev = DevSettings.FromJson(devRoot);
            }

            foreach (string warning in bundle.Strings.Warnings)
            {
                bundle.Note(warning);
            }

            return bundle;
        }

        private static void MergeStrings(DataBundle bundle, string fileName)
        {
            if (!TryGetText(bundle.Files, fileName, out string text))
            {
                return;
            }

            if (JsonValue.TryParse(text, out JsonValue root, out string error))
            {
                bundle.Strings.Merge(fileName, root);
            }
            else
            {
                bundle.Note($"{fileName}: {error}");
            }
        }

        private static bool TryGetText(Dictionary<string, LoadedFile> files, string fileName, out string text)
        {
            if (files.TryGetValue(fileName, out LoadedFile file) && file.Ok)
            {
                text = file.Text;
                return true;
            }

            text = null;
            return false;
        }
    }
}
