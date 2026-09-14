using System.Text.RegularExpressions;

namespace VoceLens.Services.Chunking;

public interface ITextChunker
{
    List<string> SplitIntoChunks(string text, int maxChunkLength = 1000, bool enableTurboStart = false);
}

/// <summary>
/// Intelligent text chunker implementing text splitting respecting sentence boundaries (. ! ?) and word boundaries.
/// Includes Turbo Start adaptive first-chunk sizing for near-instant speech initiation.
/// </summary>
public class TextChunker : ITextChunker
{
    private static readonly Regex SentenceRegex = new(@"[^.!?]+[.!?]*|[^.!?]+", RegexOptions.Compiled);

    public List<string> SplitIntoChunks(string text, int maxChunkLength = 1000, bool enableTurboStart = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        text = TextSanitizer.SanitizeOcrTranscript(text);
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        if (maxChunkLength <= 0)
            maxChunkLength = 1000;

        var matches = SentenceRegex.Matches(text);
        var sentences = matches.Select(m => m.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (sentences.Count == 0)
        {
            return new List<string> { text.Trim() };
        }

        var chunks = new List<string>();
        int sentenceIndex = 0;

        // 1. Turbo Start: create an ultra-fast initial chunk (strictly first sentence, ~30-80 chars) for instant audio start
        const int TurboTargetMaxLength = 80;
        const int TurboMinLength = 30;

        if (enableTurboStart && sentences.Count > 1)
        {
            string firstChunk = sentences[0];
            sentenceIndex = 1;

            // If the first sentence is very short (< 30 chars), append the next sentence up to ~80 chars
            while (sentenceIndex < sentences.Count && firstChunk.Length < TurboMinLength)
            {
                string nextSentence = sentences[sentenceIndex];
                if (firstChunk.Length + 1 + nextSentence.Length <= TurboTargetMaxLength)
                {
                    firstChunk = $"{firstChunk} {nextSentence}";
                    sentenceIndex++;
                }
                else
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
            // so playback can start immediately in ~200ms
            string singleSentence = sentences[0];
            int clauseBreak = -1;
            char[] clauseDelimiters = { ',', ';', ':', '—', '-', '\n' };
            for (int ci = 35; ci < Math.Min(singleSentence.Length, 100); ci++)
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
