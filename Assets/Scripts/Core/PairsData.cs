using System;
using System.Collections.Generic;

namespace SmallPot
{
    /// <summary>One entry of data/pairs.json: a word, its antonym, and the wrong options to offer.</summary>
    [Serializable]
    public sealed class PairEntry
    {
        public string Word;
        public string Opposite;
        public readonly List<string> Wrong = new List<string>();
    }

    /// <summary>One prepared question: the prompt word, three (or choicesShown) options, and the answer.</summary>
    public sealed class PairRound
    {
        public string Word;
        public string Answer;
        public readonly List<string> Choices = new List<string>();
        public int AnswerIndex;

        public bool IsAnswer(int index) => index == AnswerIndex;
    }

    /// <summary>
    /// data/pairs.json, loaded tolerantly.
    ///
    /// The supplied file is a bare JSON array of { word, opposite, wrong[] }; an object wrapper is
    /// also accepted so the file can grow a version field without breaking the game. Entries that are
    /// missing a word or an opposite are dropped with a warning rather than shown as a broken round.
    /// </summary>
    public sealed class PairsData
    {
        public const string FileName = "pairs.json";

        public readonly List<PairEntry> Pairs = new List<PairEntry>();
        public readonly List<string> Warnings = new List<string>();

        public bool HasUsablePairs => UsableCount() > 0;

        public static PairsData FromJsonText(string text, out string error)
        {
            error = null;
            if (!JsonValue.TryParse(text, out JsonValue root, out string parseError))
            {
                error = $"{FileName}: {parseError}";
                return new PairsData();
            }

            return FromJson(root);
        }

        public static PairsData FromJson(JsonValue root)
        {
            var data = new PairsData();
            if (root == null || root.IsNull)
            {
                data.Warnings.Add($"{FileName}: file was empty.");
                return data;
            }

            JsonValue array = root;

            // The supplied file is a bare array. Accept { "pairs": [...] } as well.
            if (root.IsObject)
            {
                array = root["pairs"];
                if (!array.IsArray)
                {
                    data.Warnings.Add($"{FileName}: expected an array, or an object with a 'pairs' array.");
                    return data;
                }
            }

            if (!array.IsArray)
            {
                data.Warnings.Add($"{FileName}: root is neither an array nor an object.");
                return data;
            }

            int index = 0;
            foreach (JsonValue item in array.Items)
            {
                index++;
                if (!item.IsObject)
                {
                    data.Warnings.Add($"{FileName}: entry {index} is not an object; it was skipped.");
                    continue;
                }

                string word = (item["word"].AsString() ?? string.Empty).Trim();
                string opposite = (item["opposite"].AsString() ?? string.Empty).Trim();
                if (word.Length == 0 || opposite.Length == 0)
                {
                    data.Warnings.Add($"{FileName}: entry {index} has no word or no opposite; it was skipped.");
                    continue;
                }

                var entry = new PairEntry { Word = word, Opposite = opposite };
                JsonValue wrong = item["wrong"];
                if (wrong.IsArray)
                {
                    foreach (JsonValue candidate in wrong.Items)
                    {
                        string value = (candidate.AsString() ?? string.Empty).Trim();
                        if (value.Length == 0)
                        {
                            continue;
                        }

                        entry.Wrong.Add(value);
                    }
                }
                else if (!wrong.IsNull)
                {
                    data.Warnings.Add($"{FileName}: entry {index} ('{word}') has no 'wrong' array.");
                }

                data.Pairs.Add(entry);
            }

            if (data.Pairs.Count == 0)
            {
                data.Warnings.Add($"{FileName}: no usable pairs were found.");
            }

            return data;
        }

        private int UsableCount()
        {
            int count = 0;
            foreach (PairEntry entry in Pairs)
            {
                if (IsUsable(entry))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsUsable(PairEntry entry) =>
            entry != null &&
            !string.IsNullOrEmpty(entry.Word) &&
            !string.IsNullOrEmpty(entry.Opposite);

        /// <summary>
        /// Builds the rounds for one play-through.
        ///
        /// Rounds never repeat a pair while there are enough pairs, options are always shuffled, and
        /// the correct answer is guaranteed to be present and reachable even when the data file is
        /// thinner than the config asks for. Deterministic for a given <paramref name="random"/>.
        /// </summary>
        public List<PairRound> BuildRounds(int roundCount, int choicesShown, Random random)
        {
            random = random ?? new Random();
            var rounds = new List<PairRound>();
            var usable = new List<PairEntry>();
            foreach (PairEntry entry in Pairs)
            {
                if (IsUsable(entry))
                {
                    usable.Add(entry);
                }
            }

            if (usable.Count == 0 || roundCount <= 0)
            {
                return rounds;
            }

            int options = Math.Max(2, choicesShown);

            // Shuffle the pair order, then walk it, reshuffling when the list runs out so a short data
            // file still produces the configured number of rounds without repeating in a pattern.
            var order = new List<int>();
            for (int i = 0; i < usable.Count; i++)
            {
                order.Add(i);
            }

            Shuffle(order, random);
            int cursor = 0;
            int shuffledOnce = 0;

            while (rounds.Count < roundCount)
            {
                if (cursor >= order.Count)
                {
                    cursor = 0;
                    Shuffle(order, random);
                    shuffledOnce++;

                    // With a single pair there is nothing to reshuffle; allow the repeat but stop
                    // after the first full cycle to avoid an accidental infinite loop.
                    if (shuffledOnce > roundCount + 2)
                    {
                        break;
                    }
                }

                PairEntry entry = usable[order[cursor]];
                cursor++;

                PairRound round = BuildRound(entry, usable, options, random);
                if (round != null)
                {
                    rounds.Add(round);
                }
                else if (rounds.Count == 0 && cursor >= order.Count && shuffledOnce > 1)
                {
                    break;
                }
            }

            return rounds;
        }

        private static PairRound BuildRound(PairEntry entry, List<PairEntry> pool, int options, Random random)
        {
            var candidates = new List<string> { entry.Opposite };

            foreach (string wrong in entry.Wrong)
            {
                if (IsAcceptableOption(wrong, entry, candidates))
                {
                    candidates.Add(wrong);
                }
            }

            // Top up from other entries when the data file supplies too few wrong options, so
            // choicesShown can be raised in config.json without breaking the round.
            if (candidates.Count < options)
            {
                var topUps = new List<string>();
                foreach (PairEntry other in pool)
                {
                    if (!ReferenceEquals(other, entry))
                    {
                        topUps.Add(other.Opposite);
                        topUps.Add(other.Word);
                    }
                }

                Shuffle(topUps, random);
                foreach (string candidate in topUps)
                {
                    if (candidates.Count >= options)
                    {
                        break;
                    }

                    if (IsAcceptableOption(candidate, entry, candidates))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            Shuffle(candidates, random);

            int take = Math.Min(options, candidates.Count);
            if (take < 2)
            {
                // Not enough distinct options to ask a question with.
                return null;
            }

            // Shuffling may have pushed the answer outside the visible slice; swap it back in.
            int answerIndex = candidates.IndexOf(entry.Opposite);
            if (answerIndex >= take)
            {
                (candidates[take - 1], candidates[answerIndex]) = (candidates[answerIndex], candidates[take - 1]);
                answerIndex = take - 1;
            }

            var round = new PairRound { Word = entry.Word, Answer = entry.Opposite };
            for (int i = 0; i < take; i++)
            {
                round.Choices.Add(candidates[i]);
            }

            round.AnswerIndex = round.Choices.IndexOf(entry.Opposite);
            return round.AnswerIndex < 0 ? null : round;
        }

        private static bool IsAcceptableOption(string candidate, PairEntry entry, List<string> existing)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            if (string.Equals(candidate, entry.Opposite, StringComparison.Ordinal) ||
                string.Equals(candidate, entry.Word, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (string value in existing)
            {
                if (string.Equals(value, candidate, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Shuffle<T>(List<T> list, Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
