using System.Text.RegularExpressions;

namespace VoceLens.Services.Chunking;

public interface ITextChunker
{
    List<string> SplitIntoChunks(string text, int maxChunkLength = 1000);
}

/// <summary>
/// Intelligent text chunker implementing the exact same algorithm as Voce (splitIntoChunks).
/// Splits long texts into chunks respecting sentence boundaries (. ! ?) and word boundaries.
/// </summary>
public class TextChunker : ITextChunker
{
    private static readonly Regex SentenceRegex = new(@"[^.!?]+[.!?]*|[^.!?]+", RegexOptions.Compiled);

    public List<string> SplitIntoChunks(string text, int maxChunkLength = 1000)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        text = TextSanitizer.SanitizeOcrTranscript(text);
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        if (maxChunkLength <= 0)
            maxChunkLength = 1000;

        var matches = SentenceRegex.Matches(text);
        var sentences = matches.Select(m => m.Value).ToList();

        var chunks = new List<string>();
        string currentChunk = string.Empty;

        foreach (var rawSentence in sentences)
        {
            var sentence = rawSentence.Trim();
            if (string.IsNullOrEmpty(sentence))
                continue;

            // If a single sentence is longer than maxChunkLength, split it by character limit at word boundaries
            if (sentence.Length > maxChunkLength)
            {
                if (!string.IsNullOrEmpty(currentChunk))
                {
                    chunks.Add(currentChunk.Trim());
                    currentChunk = string.Empty;
                }

                string remaining = sentence;
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

                    chunks.Add(chunkPart.Trim());
                    remaining = remaining.Substring(chunkPart.Length).Trim();
                }

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
}
