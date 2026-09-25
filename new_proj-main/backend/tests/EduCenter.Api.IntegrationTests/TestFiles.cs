using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace EduCenter.Api.IntegrationTests;

/// <summary>Files that are what they claim to be — the start of a real PNG, PDF, JPEG or WebM, a real docx or pptx — for tests of upload paths,
/// which refuse a file whose content is not its extension (docs/21.09-audit-jurnali S08.03). The bodies are padding: only the header, and for
/// the Office formats the archive, is real. <paramref name="size"/> is the exact length, for the tests that upload right at a size limit.</summary>
public static class TestFiles
{
    public static byte[] Png(int size = 32) => Padded([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], size);

    public static byte[] Jpeg(int size = 32) => Padded([0xFF, 0xD8, 0xFF, 0xE0], size);

    public static byte[] Pdf(int size = 32) => Padded("%PDF-1.4\n"u8.ToArray(), size);

    public static byte[] Webm(int size = 2048) => Padded([0x1A, 0x45, 0xDF, 0xA3], size);

    /// <summary>An MP3 with an ID3 tag at its start.</summary>
    public static byte[] Mp3(int size = 32) => Padded("ID3"u8.ToArray(), size);

    /// <summary>A WebP: the RIFF container whose form is "WEBP".</summary>
    public static byte[] Webp(int size = 16) => Padded("RIFF\0\0\0\0WEBP"u8.ToArray(), size);

    /// <summary>A plain-text file. UTF-8, like every text file the product accepts.</summary>
    public static byte[] Text(string content) => Encoding.UTF8.GetBytes(content);

    /// <summary>An executable: the "MZ" header of a Windows program — what a virus wearing a .pdf name actually is.</summary>
    public static byte[] Executable() => Padded("MZ"u8.ToArray(), 256);

    /// <summary>A minimal but genuine Word document: the archive Word itself would write, with one paragraph.</summary>
    public static byte[] Docx(string text = "Hello")
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new DocumentFormat.OpenXml.Wordprocessing.Body(
                new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text(text)))));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>A minimal presentation with one slide per text — what PowerPoint would write, as far as a text extractor cares.</summary>
    public static byte[] Pptx(params string[] slides)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentation = document.AddPresentationPart();
            presentation.Presentation = new P.Presentation(new P.SlideIdList());

            uint id = 256;
            foreach (var text in slides)
            {
                var slide = presentation.AddNewPart<SlidePart>();
                slide.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                    new P.Shape(new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(text))))))));
                slide.Slide.Save();
                presentation.Presentation.SlideIdList!.Append(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(slide) });
            }

            presentation.Presentation.Save();
        }

        return stream.ToArray();
    }

    /// <summary>A zip holding the given number of tiny files (plus the <c>[Content_Types].xml</c> every Office document has), each of
    /// <paramref name="entryBytes"/> zeros — many files, or a few very large ones once they are unpacked.</summary>
    public static byte[] Archive(int entries, int entryBytes = 1)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("[Content_Types].xml").Open().Dispose();
            for (var i = 0; i < entries; i++)
            {
                using var entry = zip.CreateEntry($"file-{i}.txt", CompressionLevel.SmallestSize).Open();
                if (entryBytes > 0) entry.Write(new byte[entryBytes]);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Padded(byte[] header, int size)
    {
        var bytes = new byte[Math.Max(size, header.Length)];
        header.CopyTo(bytes, 0);
        return bytes;
    }
}
