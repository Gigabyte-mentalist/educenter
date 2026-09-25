using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EduCenter.Api.Files;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Slides = DocumentFormat.OpenXml.Presentation;

namespace EduCenter.Api.Ai.Knowledge;

/// <summary>Step 3 of the ingestion pipeline (docs/00_TZ.md §38): plain text out of an uploaded file — a PDF, a Word document, the slides
/// of a presentation, or plain text (.txt, .md, .csv). Parsing runs on untrusted input, so a malformed file is a failed document, never an
/// unhandled exception, and an archive-based format (docx, pptx) is held to <see cref="ZipLimits"/> before anything unpacks it.</summary>
public static class DocumentTextExtractor
{
    public static string Extract(Stream content, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".pdf" => FromPdf(content),
                ".docx" => FromWord(content),
                ".pptx" => FromPresentation(content),
                _ => new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true).ReadToEnd()
            };
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Faylni o'qib bo'lmadi ({extension}): {e.Message}", e);
        }
    }

    private static string FromPdf(Stream content)
    {
        using var pdf = PdfDocument.Open(content);
        var builder = new StringBuilder();
        foreach (var page in pdf.GetPages())
            builder.Append(ContentOrderTextExtractor.GetText(page)).Append("\n\n");
        return builder.ToString();
    }

    /// <summary>OpenXml needs a seekable stream it may read repeatedly, and an archive is checked against the unpacking limits first.</summary>
    private static MemoryStream OpenArchive(Stream content)
    {
        var buffer = new MemoryStream();
        content.CopyTo(buffer);

        if (ZipLimits.Inspect(buffer, officeDocument: true) is { } problem)
        {
            buffer.Dispose();
            throw new InvalidDataException(problem);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>The text of every slide, in presentation order: titles, body text and table cells alike.</summary>
    private static string FromPresentation(Stream content)
    {
        using var buffer = OpenArchive(content);
        using var presentation = PresentationDocument.Open(buffer, isEditable: false);
        var part = presentation.PresentationPart;
        if (part?.Presentation?.SlideIdList is not { } slides) return "";

        var builder = new StringBuilder();
        foreach (var slideId in slides.Elements<Slides.SlideId>())
        {
            if (slideId.RelationshipId?.Value is not { } relationshipId
                || part.GetPartById(relationshipId) is not SlidePart slide
                || slide.Slide is null)
                continue;

            foreach (var paragraph in slide.Slide.Descendants<Drawing.Paragraph>())
            {
                var text = paragraph.InnerText;
                if (!string.IsNullOrWhiteSpace(text)) builder.Append(text).Append('\n');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string FromWord(Stream content)
    {
        using var buffer = OpenArchive(content);

        using var word = WordprocessingDocument.Open(buffer, isEditable: false);
        var body = word.MainDocumentPart?.Document?.Body;
        if (body is null) return "";

        var builder = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text)) builder.Append(text).Append("\n\n");
        }

        return builder.ToString();
    }
}
