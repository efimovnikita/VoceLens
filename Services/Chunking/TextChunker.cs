using System.Text.RegularExpressions;

namespace VoceLens.Services.Chunking;

public interface ITextChunker
{
    List<string> SplitIntoChunks(string text, int maxChunkLength = 1000, bool enableTurboStart = false, int minTurboChunkLength = 85);
}

/// <summary>
/// Intelligent text chunker implementing text splitting respecting sentence boundaries (. ! ?) and word boundaries.
/// Includes Turbo Start adaptive first-chunk sizing for near-instant speech initiation.
/// </summary>
public class TextChunker : ITextChunker
{
    private static readonly Regex SentenceRegex = new(@"[^.!?]+[.!?]*|[^.!?]+", RegexOptions.Compiled);

    public List<string> SplitIntoChunks(string text, int maxChunkLength = 1000, bool enableTurboStart = false, int minTurboChunkLength = 85)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        text = TextSanitizer.SanitizeOcrTranscript(text);
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        if (maxChunkLength <= 0)
            maxChunkLength = 1000;

        if (minTurboChunkLength <= 0)
            minTurboChunkLength = 85;

        var matches = SentenceRegex.Matches(text);
        var sentences = matches.Select(m => m.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (sentences.Count == 0)
        {
            return new List<string> { text.Trim() };
        }

        var chunks = new List<string>();
        int sentenceIndex = 0;

        // 1. Turbo Start: create an initial chunk with a safe minimum character threshold.
        // This ensures chunk 0 has sufficient spoken duration (~5-7 sec) for chunk 1 (1000 chars)
        // to finish background preloading without any dead pause, even if the first sentence
        // is just a single word (e.g. "Статья.") or a short tail from the previous page.
        if (enableTurboStart && sentences.Count > 1)
        {
            string firstChunk = sentences[0];
            sentenceIndex = 1;

            // Keep appending sentences as long as firstChunk is shorter than minTurboChunkLength
            while (sentenceIndex < sentences.Count && firstChunk.Length < minTurboChunkLength)
            {
                string nextSentence = sentences[sentenceIndex];
                firstChunk = $"{firstChunk} {nextSentence}";
                sentenceIndex++;

                if (firstChunk.Length >= minTurboChunkLength)
                {
                    break;
                }
            }

            if (firstChunk.Length > maxChunkLength)
            {
                chunks.AddRange(SplitLongTextByWords(firstChunk, maxChunkLength));
            }
            else
            {
                chunks.Add(firstChunk.Trim());
            }
        }
        else if (enableTurboStart && sentences.Count == 1 && sentences[0].Length > 120)
        {
            // If there is only one long sentence without periods, split at the first clause mark (comma, semicolon, dash, colon, newline)
            // around minTurboChunkLength so playback can start immediately in ~300ms
            string singleSentence = sentences[0];
            int clauseBreak = -1;
            char[] clauseDelimiters = { ',', ';', ':', '—', '-', '\n' };
            int minSearch = Math.Min(35, minTurboChunkLength / 2);
            int maxSearch = Math.Min(singleSentence.Length, Math.Max(minTurboChunkLength + 30, 100));
            for (int ci = minSearch; ci < maxSearch; ci++)
            {
                if (clauseDelimiters.Contains(singleSentence[ci]))
                {
                    clauseBreak = ci + 1;
                    break;
                }
            }

            if (clauseBreak > 0)
            {
                string firstClause = singleSentence.Substring(0, clauseBreak).Trim();
                string remainingClause = singleSentence.Substring(clauseBreak).Trim();
                chunks.Add(firstClause);
                sentences[0] = remainingClause;
                sentenceIndex = 0;
            }
        }

        // 2. Standard chunking for remaining sentences
        string currentChunk = string.Empty;

        for (int i = sentenceIndex; i < sentences.Count; i++)
        {
            var sentence = sentences[i];

            // If a single sentence is longer than maxChunkLength, split it by character limit at word boundaries
            if (sentence.Length > maxChunkLength)
            {
                if (!string.IsNullOrEmpty(currentChunk))
                {
                    chunks.Add(currentChunk.Trim());
                    currentChunk = string.Empty;
                }

                chunks.AddRange(SplitLongTextByWords(sentence, maxChunkLength));
                continue;
            }

            // Check if adding the next sentence exceeds the limit
            string potentialCombined = string.IsNullOrEmpty(currentChunk)
                ? sentence
                : $"{currentChunk} {sentence}".Trim();

            if (potentialCombined.Length > maxChunkLength)
            {
                if (!string.IsNullOrEmpty(currentChunk))
                {
                    chunks.Add(currentChunk.Trim());
                }
                currentChunk = sentence;
            }
            else
            {
                currentChunk = potentialCombined;
            }
        }

        if (!string.IsNullOrEmpty(currentChunk))
        {
            chunks.Add(currentChunk.Trim());
        }

        return chunks;
    }

    private static List<string> SplitLongTextByWords(string text, int maxChunkLength)
    {
        var result = new List<string>();
        string remaining = text;
        while (remaining.Length > 0)
        {
            int takeLength = Math.Min(remaining.Length, maxChunkLength);
            string chunkPart = remaining.Substring(0, takeLength);

            if (remaining.Length > maxChunkLength)
            {
                int lastSpace = chunkPart.LastIndexOf(' ');
                if (lastSpace > 0)
                {
                    chunkPart = remaining.Substring(0, lastSpace);
                }
            }

            result.Add(chunkPart.Trim());
            remaining = remaining.Substring(chunkPart.Length).Trim();
        }
        return result;
    }
}
