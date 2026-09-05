using System.Text.RegularExpressions;

namespace VoceLens.Services.Chunking;

/// <summary>
/// Utility for sanitizing text before chunking and TTS synthesis.
/// Removes OCR image artifacts (e.g. ![img-0.jpeg](img-0.jpeg)) that Mistral OCR produces
/// when detecting floating overlays, icons, illustrations, or graphics.
/// </summary>
public static class TextSanitizer
{
    // Matches standard markdown image syntax: ![alt text](url)
    private static readonly Regex MarkdownImageRegex = new(@"!\[[^\]]*\]\([^\)]*\)", RegexOptions.Compiled);

    // Matches standalone unlinked markdown image syntax: ![img-0.jpeg]
    private static readonly Regex UnlinkedMarkdownImageRegex = new(@"!\[[^\]]*\]", RegexOptions.Compiled);

    // Matches standalone bracketed image tags: [img-0.jpeg](img-0.jpeg) or [img-0.jpeg]
    private static readonly Regex StandaloneImageTagRegex = new(@"\[img-\d+\.(?:jpe?g|png|webp|gif|bmp|svg)\](?:\([^\)]*\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches lines containing only image filenames: e.g. "img-0.jpeg"
    private static readonly Regex ImageFilenameLineRegex = new(@"^\s*img-\d+\.(?:jpe?g|png|webp|gif|bmp|svg)\s*$", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // Collapses three or more consecutive line breaks into two line breaks (preserving paragraph spacing)
    private static readonly Regex MultipleNewlinesRegex = new(@"(\r?\n\s*){3,}", RegexOptions.Compiled);

    /// <summary>
    /// Removes markdown image tags and OCR graphic artifacts from text so they are not read aloud by TTS.
    /// </summary>
    public static string SanitizeOcrTranscript(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 1. Remove markdown images: ![img-0.jpeg](img-0.jpeg) or any ![alt](url)
        string cleaned = MarkdownImageRegex.Replace(text, string.Empty);

        // 2. Remove unlinked markdown image tags: ![img-0.jpeg]
        cleaned = UnlinkedMarkdownImageRegex.Replace(cleaned, string.Empty);

        // 3. Remove standalone image tags: [img-0.jpeg] or [img-0.jpeg](...)
        cleaned = StandaloneImageTagRegex.Replace(cleaned, string.Empty);

        // 4. Remove standalone image filenames on their own lines
        cleaned = ImageFilenameLineRegex.Replace(cleaned, string.Empty);

        // 5. Clean up excessive consecutive newlines created by removing lines
        cleaned = MultipleNewlinesRegex.Replace(cleaned, "\n\n");

        return cleaned.Trim();
    }
}
