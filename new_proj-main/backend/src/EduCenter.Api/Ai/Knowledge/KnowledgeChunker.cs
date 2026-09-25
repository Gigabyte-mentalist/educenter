using System.Text;
using System.Text.RegularExpressions;
using EduCenter.Modules.Ai.Domain;

namespace EduCenter.Api.Ai.Knowledge;

/// <summary>Step 6 of the ingestion pipeline (docs/00_TZ.md §38): split text into retrievable pieces. Paragraphs stay
/// together where they fit, and neighbouring chunks overlap a little so an answer that straddles a boundary is still
/// found whole.</summary>
public static class KnowledgeChunker
{
    public const int TargetChunkChars = 1200;
    public const int OverlapChars = 150;

    public static List<string> Split(string text)
    {
        var normalized = Regex.Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n{3,}", "\n\n").Trim();
        if (normalized.Length == 0) return [];

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var piece in SplitLongParagraph(paragraph))
            {
                if (current.Length > 0 && current.Length + piece.Length + 2 > TargetChunkChars)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                    current.Append(Tail(chunks[^1]));
                }

                if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }
        }

        if (current.Length > 0) chunks.Add(current.ToString().Trim());
        return chunks.Where(c => c.Length > 0).Select(Cap).ToList();
    }

    /// <summary>
    /// Rebuilds the readable text from the stored chunks by dropping the overlap <see cref="Split"/> copied from each
    /// chunk into the next. Used when the uploaded file is gone from disk but the document is still indexed.
    /// </summary>
    public static string Reassemble(IReadOnlyList<string> chunks)
    {
        var text = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (i > 0)
            {
                var overlap = Tail(chunks[i - 1]);
                if (overlap.Length > 0 && chunk.StartsWith(overlap, StringComparison.Ordinal))
                    chunk = chunk[overlap.Length..].TrimStart();
                text.Append("\n\n");
            }

            text.Append(chunk);
        }

        return text.ToString();
    }

    /// <summary>A paragraph longer than a chunk is cut on sentence boundaries.</summary>
    private static IEnumerable<string> SplitLongParagraph(string paragraph)
    {
        if (paragraph.Length <= TargetChunkChars) return [paragraph];

        var pieces = new List<string>();
        var current = new StringBuilder();
        foreach (var sentence in Regex.Split(paragraph, @"(?<=[.!?])\s+"))
        {
            if (current.Length > 0 && current.Length + sentence.Length + 1 > TargetChunkChars)
            {
                pieces.Add(current.ToString().Trim());
                current.Clear();
            }

            if (sentence.Length > TargetChunkChars)
            {
                // A single sentence that long is usually a table or a wall of text: cut it by length.
                for (var i = 0; i < sentence.Length; i += TargetChunkChars)
                    pieces.Add(sentence.Substring(i, Math.Min(TargetChunkChars, sentence.Length - i)));
                continue;
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
        }

        if (current.Length > 0) pieces.Add(current.ToString().Trim());
        return pieces;
    }

    private static string Tail(string chunk) =>
        chunk.Length <= OverlapChars ? "" : chunk[^OverlapChars..];

    private static string Cap(string chunk) =>
        chunk.Length <= KnowledgeChunk.MaxTextLength ? chunk : chunk[..KnowledgeChunk.MaxTextLength];
}
