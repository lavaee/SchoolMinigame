using System.Collections.Generic;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Which sound belongs to which moment, by file name, and how each moment is voiced.
    ///
    /// The clips are named after the events they belong to - "UI button", "Correct Choice", "Blob" - so the
    /// mapping is a table rather than a row of drag-and-drop slots. A file dropped into
    /// Assets/Resources/Audio/FX is therefore picked up with no scene edit, no prefab edit and no code
    /// change, which is what lets the interface be replaced later without losing the sound of it.
    ///
    /// A file that is absent is silence rather than an error: the same contract AudioDirector has always
    /// had, and the reason the game is playable with no audio in the project at all.
    ///
    /// Nothing here touches Unity's audio API, so the whole mapping is testable in the editor.
    /// </summary>
    public static class AudioLibrary
    {
        public const string EffectsFolder = "Audio/FX";
        public const string MusicFolder = "Audio/Music";

        public const string AmbienceFirst = "Ambience 1";
        public const string AmbienceSecond = "Ambience 2";
        public const string AmbienceFocus = "Ambience Focus 2";

        /// <summary>The four takes an answer button draws from; one is chosen per press.</summary>
        public static readonly string[] SelectVariations =
        {
            "Select flowerpot 1",
            "Select flowerpot 2",
            "Select flowerpot 3",
            "Select flowerpot 4"
        };

        /// <summary>The file name, without extension, that carries each moment.</summary>
        private static readonly Dictionary<Sound, string> Names = new Dictionary<Sound, string>
        {
            { Sound.Click, "UI button" },
            { Sound.Correct, "Correct Choice" },
            { Sound.Wrong, "Wrong Choice" },
            { Sound.Reward, "Finish test" },
            { Sound.Water, "Watering the planet" },
            { Sound.Growth, "Blob" },
            { Sound.Encourage, "Cat" },

            // The four moments with no file of their own borrow the nearest one, detuned and quietened so
            // they never sound like the event they borrowed from. One line each to change if a file arrives.
            { Sound.Morning, "Correct Choice" },
            { Sound.Thirst, "Watering the planet" },
            { Sound.ConnectionLost, "Wrong Choice" },
            { Sound.ConnectionUp, "Correct Choice" }
        };

        /// <summary>The file name for a moment, or null when it is one of the randomised ones.</summary>
        public static string FileNameFor(Sound sound)
        {
            string name;
            return Names.TryGetValue(sound, out name) ? name : null;
        }

        /// <summary>Every file name this library knows about, for the test that checks none is forgotten.</summary>
        public static IEnumerable<string> KnownFileNames()
        {
            foreach (KeyValuePair<Sound, string> pair in Names)
            {
                yield return pair.Value;
            }

            for (int index = 0; index < SelectVariations.Length; index++)
            {
                yield return SelectVariations[index];
            }

            yield return AmbienceFirst;
            yield return AmbienceSecond;
            yield return AmbienceFocus;
        }

        /// <summary>
        /// How a moment is voiced. The reused clips are told apart by pitch and level rather than by being
        /// four near-identical files: a wrong answer is the same recording as a lost connection, but at
        /// 0.85x and quieter it reads as a different, softer event.
        /// </summary>
        public static void Voicing(Sound sound, out float pitch, out float volume)
        {
            pitch = 1f;
            volume = 1f;

            switch (sound)
            {
                case Sound.Morning:
                    pitch = 1.12f;
                    volume = 0.6f;
                    break;
                case Sound.Thirst:
                    pitch = 0.82f;
                    volume = 0.5f;
                    break;
                case Sound.ConnectionLost:
                    pitch = 0.85f;
                    volume = 0.6f;
                    break;
                case Sound.ConnectionUp:
                    pitch = 1.2f;
                    volume = 0.5f;
                    break;
            }
        }

        /// <summary>
        /// Which take to use next: the roll, unless it would repeat the one just used. Kept pure, and apart
        /// from the audio API, so the rule can be tested without playing anything.
        /// </summary>
        public static int NextVariationIndex(int previous, int count, int roll)
        {
            if (count <= 0)
            {
                return -1;
            }

            int index = ((roll % count) + count) % count;
            if (count > 1 && index == previous)
            {
                index = (index + 1) % count;
            }

            return index;
        }

        /// <summary>The other general track, so the two beds take turns rather than one file repeating.</summary>
        public static string NextBoardTrack(string current) =>
            current == AmbienceFirst ? AmbienceSecond : AmbienceFirst;

        public static AudioClip LoadEffects(string fileName) => Load(EffectsFolder, fileName);

        public static AudioClip LoadMusic(string fileName) => Load(MusicFolder, fileName);

        private static AudioClip Load(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            return Resources.Load<AudioClip>(folder + "/" + fileName);
        }
    }
}
