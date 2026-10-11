using UnityEditor;
using UnityEngine;

namespace SmallPot.EditorTools
{
    /// <summary>
    /// Import settings for the game's audio, applied by folder.
    ///
    /// The two folders want opposite things and both defaults are wrong for one of them. A click has to
    /// make a sound on the same frame it is pressed, which means decompressed on load and preloaded - the
    /// default streaming import is exactly how interface sounds end up feeling late. An ambience bed is
    /// minutes long, played once and looped, and decompressing it up front would hold megabytes in memory
    /// for no benefit, so it streams.
    ///
    /// Doing it here rather than by hand means it survives the files being replaced, re-imported or moved.
    /// </summary>
    public sealed class AudioPostprocessor : AssetPostprocessor
    {
        public const string EffectsFolder = "/Audio/FX/";
        public const string MusicFolder = "/Audio/Music/";

        private void OnPreprocessAudio()
        {
            AudioImporter importer = assetImporter as AudioImporter;
            if (importer == null)
            {
                return;
            }

            string path = assetPath.Replace('\\', '/');

            if (path.Contains(MusicFolder))
            {
                Apply(importer, AudioClipLoadType.Streaming, preload: false, background: true);
                return;
            }

            if (path.Contains(EffectsFolder))
            {
                Apply(importer, AudioClipLoadType.DecompressOnLoad, preload: true, background: false);
            }
        }

        private static void Apply(AudioImporter importer, AudioClipLoadType loadType, bool preload, bool background)
        {
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = loadType;

            // Preload belongs to the sample settings, not the importer: the importer's own property is
            // obsolete and refuses to compile, and this is the per-platform setting it moved to.
            settings.preloadAudioData = preload;

            // Uncompressed for effects: they are small, and a decode step in the middle of a button press is
            // a delay the player can feel. Music keeps its compression because it streams anyway.
            settings.compressionFormat = loadType == AudioClipLoadType.Streaming
                ? AudioCompressionFormat.Vorbis
                : AudioCompressionFormat.PCM;

            importer.defaultSampleSettings = settings;
            importer.loadInBackground = background;
        }
    }
}
