using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using EduCenter.Api.Ai;
using EduCenter.Api.Ai.Knowledge;
using EduCenter.Api.Chat;
using EduCenter.Api.Deployment;
using EduCenter.Api.Files;
using EduCenter.Api.Lms;
using EduCenter.Api.Students;
using EduCenter.Api.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// What an uploaded file has to pass before it is written anywhere (docs/21.09-audit-jurnali S08.03, docs/00_TZ.md §38 steps 1–2): its content
/// must be what its extension says, it must not be an archive built to explode, and the virus scanner must not object — and when the scanner
/// cannot be asked, only a developer machine lets the file in. The suite's scanner is <see cref="FakeFileScanner"/>: the EICAR test string is
/// "infected". ClamAV's own wire protocol is checked against a stand-in daemon at the bottom of this file.
/// </summary>
[Collection("Api")]
public class FileSecurityTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    // ---- Every upload path asks the scanner -----------------------------------------------------------------------------------

    [Fact]
    public async Task AnInfectedFile_IsRefusedOnEveryUploadPath_AndNothingStaysOnDisk()
    {
        foreach (var path in await UploadPathsAsync())
        {
            var filesBefore = path.FilesOnDisk();
            var scannedBefore = factory.FakeScanner.Scanned;

            var response = await path.Upload($"report{path.Extension}", path.WithEicar());

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path.Name}: expected 400, got {(int)response.StatusCode}");
            Assert.Equal(FileGuard.Infected, (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
            Assert.True(filesBefore == path.FilesOnDisk(), $"{path.Name}: the refused file was left on disk");
            Assert.True(scannedBefore + 1 == factory.FakeScanner.Scanned, $"{path.Name}: the file never reached the scanner");
        }
    }

    [Fact]
    public async Task ACleanFile_IsScanned_ThenStored()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var scannedBefore = factory.FakeScanner.Scanned;

        // Real files of each kind a lesson resource may be: a PDF, an image, a Word document and a text file.
        (string Name, byte[] Content)[] genuine =
        [
            ("worksheet.pdf", TestFiles.Pdf()), ("board.png", TestFiles.Png()), ("notes.docx", TestFiles.Docx("Unit 4")),
            ("vocabulary.txt", TestFiles.Text("apple, banana, cherry — meva"))
        ];
        foreach (var (name, content) in genuine)
            Assert.Equal(HttpStatusCode.Created, (await UploadResourceAsync(teacher, c.LessonId, name, content)).StatusCode);

        Assert.Equal(scannedBefore + genuine.Length, factory.FakeScanner.Scanned);
    }

    // ---- The content is what the extension says ------------------------------------------------------------------------------

    [Fact]
    public async Task AFileWhoseContentIsNotItsExtension_IsRefusedOnEveryUploadPath()
    {
        foreach (var path in await UploadPathsAsync())
        {
            var filesBefore = path.FilesOnDisk();

            // A program renamed to whatever the path accepts — the classic way past an extension allow-list.
            var response = await path.Upload($"invoice{path.Extension}", TestFiles.Executable());

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path.Name}: expected 400, got {(int)response.StatusCode}");
            Assert.Equal(FileSignatures.ContentMismatch, (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
            Assert.True(filesBefore == path.FilesOnDisk(), $"{path.Name}: the refused file was left on disk");
        }
    }

    [Fact]
    public async Task OneKindOfFile_CannotPassForAnother()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        // A photo must be the image it is named as: a JPEG under a .png name, a PDF under .jpg, a text file under .png all fail…
        foreach (var (name, content) in new (string, byte[])[]
                 { ("me.png", TestFiles.Jpeg()), ("me.jpg", TestFiles.Png()), ("me.jpg", TestFiles.Pdf()), ("me.png", TestFiles.Text("not an image")) })
            Assert.Equal(HttpStatusCode.BadRequest, (await UploadPhotoAsync(student, c.First.UserId, name, content)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadPhotoAsync(student, c.First.UserId, "me.png", TestFiles.Png())).StatusCode);

        // …and so does a document: a PDF named .docx, or a .docx that is not the archive Word writes.
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadResourceAsync(teacher, c.LessonId, "plan.docx", TestFiles.Pdf())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await UploadResourceAsync(teacher, c.LessonId, "plan.docx", TestFiles.Archive(entries: 2, entryBytes: 4).Take(64).ToArray())).StatusCode);

        // A .docx is an archive: without the [Content_Types].xml part every Office document carries, it is only a zip.
        using var plainZip = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(plainZip, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("readme.txt").Open().Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadResourceAsync(teacher, c.LessonId, "plan.docx", plainZip.ToArray())).StatusCode);
    }

    [Theory]
    [InlineData(".pdf", "%PDF-1.7", true)]
    [InlineData(".pdf", "MZ", false)]
    [InlineData(".png", "\u0089PNG", true)] // a PNG starts with the byte 0x89, written here as the Latin-1 character with that code
    [InlineData(".png", "PNG", false)]
    [InlineData(".gif", "GIF89a", true)]
    [InlineData(".gif", "GIF88a", false)]
    [InlineData(".txt", "plain text — with Uzbek: o'zbek tili", true)]
    [InlineData(".md", "# Title\n\n- item", true)]
    [InlineData(".csv", "a,b,c\n1,2,3", true)]
    [InlineData(".txt", "text\0with a NUL", false)]
    [InlineData(".exe", "MZ", false)] // no signature is known for it, so it can never pass
    public void TheSignatureOfEachKind_IsChecked(string extension, string start, bool accepted)
    {
        var header = extension == ".png" ? Latin1(start) : Encoding.UTF8.GetBytes(start);
        Assert.Equal(accepted, FileSignatures.HasSignature(header, extension));
    }

    [Fact]
    public void TheRemainingBinarySignatures_AreRecognised()
    {
        Assert.True(FileSignatures.HasSignature([0xFF, 0xD8, 0xFF, 0xE0], ".jpeg"));
        Assert.True(FileSignatures.HasSignature(TestFiles.Webp(), ".webp"));
        Assert.False(FileSignatures.HasSignature("RIFF\0\0\0\0WAVE"u8, ".webp"));
        Assert.True(FileSignatures.HasSignature("RIFF\0\0\0\0WAVEfmt "u8, ".wav"));
        Assert.True(FileSignatures.HasSignature("ID3\u0003"u8, ".mp3"));
        Assert.True(FileSignatures.HasSignature([0xFF, 0xFB, 0x90], ".mp3"));
        Assert.True(FileSignatures.HasSignature(Latin1("\0\0\0\u0018ftypM4A "), ".m4a"));
        Assert.True(FileSignatures.HasSignature([0x1A, 0x45, 0xDF, 0xA3], ".webm"));
        Assert.True(FileSignatures.HasSignature("OggS"u8, ".ogg"));
        Assert.True(FileSignatures.HasSignature([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], ".doc"));
        Assert.True(FileSignatures.HasSignature([0x50, 0x4B, 0x03, 0x04], ".pptx"));
        Assert.False(FileSignatures.HasSignature([0x50, 0x4B, 0x03, 0x04], ".doc"));
        Assert.False(FileSignatures.HasSignature([], ".pdf")); // nothing to read is nothing to recognise
    }

    [Fact]
    public async Task AnArchive_WithTooManyFiles_OrTooMuchOnceUnpacked_IsRefused()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        async Task<string> RefusedAsync(byte[] archive) =>
            (await (await UploadResourceAsync(teacher, c.LessonId, "deck.docx", archive)).Content.ReadFromJsonAsync<ErrorDto>())!.Error;

        // A thousand files is the most a document is allowed to hold; one more is a bomb waiting to be unpacked.
        Assert.Equal(HttpStatusCode.Created, (await UploadResourceAsync(teacher, c.LessonId, "deck.docx", TestFiles.Archive(ZipLimits.MaxEntries - 1))).StatusCode);
        Assert.Equal(ZipLimits.TooLarge, await RefusedAsync(TestFiles.Archive(ZipLimits.MaxEntries)));

        // One file that is a few kilobytes as uploaded and 60 MB once unpacked: the declared size gives it away before anything opens it.
        var bomb = TestFiles.Archive(entries: 1, entryBytes: 60 * 1024 * 1024);
        Assert.True(bomb.Length < 1024 * 1024, "the test archive has to be small on the wire, or it is not a bomb");
        Assert.Equal(ZipLimits.TooLarge, await RefusedAsync(bomb));
    }

    // ---- The scanner cannot answer -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenTheScannerCannotAnswer_UploadsAreRefused_UnlessTheHostIsADeveloperMachine(bool required)
    {
        var c = await world.CreateAsync();
        var scanner = new FakeFileScanner(available: false);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileScanner>();
            services.AddSingleton<IFileScanner>(scanner);
            services.PostConfigure<FileScannerOptions>(o => o.Required = required);
        }));
        var student = await world.ClientOnAsync(host, c.First.Email);

        var response = await UploadPhotoAsync(student, c.First.UserId, "me.png", TestFiles.Png());

        if (required)
        {
            // Fail-closed: no scanner answer, no file. The message says it is a wait, not a rejection of the file itself.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(FileGuard.ScannerUnavailable, (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, scanner.Scanned);
    }

    [Fact]
    public async Task AFileThatSlippedInUnscanned_IsQuarantinedTheNextTimeItIsIndexed_AndCannotBeApproved()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");

        // On a developer machine with no scanner the upload goes in unchecked — the document is indexed as usual…
        await using var noScanner = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileScanner>();
            services.AddSingleton<IFileScanner>(new FakeFileScanner(available: false));
            services.PostConfigure<FileScannerOptions>(o => o.Required = false);
        }));
        var unscanned = await world.ClientOnAsync(noScanner, "superadmin@demo-center.test");
        var uploaded = await UploadKnowledgeAsync(unscanned, "old-notes.txt", TestFiles.Text($"Present perfect notes.\n{FakeFileScanner.Eicar}\n"));
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var document = (await uploaded.Content.ReadFromJsonAsync<DocumentDto>())!;
        Assert.Equal("Ready", document.Status);

        // …and when the same file is indexed again on a host whose scanner knows the signature, it is held, for the reason "malware".
        var again = await superAdmin.PostAsync($"/api/ai/knowledge/documents/{document.Id}/reingest", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var held = (await superAdmin.GetFromJsonAsync<DocumentDto>($"/api/ai/knowledge/documents/{document.Id}"))!;
        Assert.Equal("Quarantined", held.Status);
        Assert.Equal(0, held.ChunkCount);
        Assert.StartsWith(KnowledgeIngestor.MalwareReason, held.Error);
        Assert.Contains(FakeFileScanner.Signature, held.Error);

        // A hold for prompt injection is a judgement a person may overrule; this one is not.
        Assert.Equal(HttpStatusCode.Conflict, (await superAdmin.PostAsync($"/api/ai/knowledge/documents/{document.Id}/approve", null)).StatusCode);
        Assert.Equal("Quarantined", (await superAdmin.GetFromJsonAsync<DocumentDto>($"/api/ai/knowledge/documents/{document.Id}"))!.Status);

        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync($"/api/ai/knowledge/documents/{document.Id}")).StatusCode);
    }

    // ---- New formats in the knowledge base -------------------------------------------------------------------------------------

    [Fact]
    public async Task PowerPointSlides_AndPlainText_AreParsedIntoTheKnowledgeBase()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var uploaded = new List<Guid>();

        try
        {
            var deck = await UploadKnowledgeAsync(superAdmin, "grammar-slides.pptx",
                TestFiles.Pptx("Present perfect: have plus the past participle", "Used for experiences up to now"));
            Assert.Equal(HttpStatusCode.Created, deck.StatusCode);
            var slides = (await deck.Content.ReadFromJsonAsync<DocumentDto>())!;
            uploaded.Add(slides.Id);
            Assert.Equal("Ready", slides.Status);
            Assert.True(slides.ChunkCount > 0);

            // Slide by slide, in the order of the presentation.
            var text = (await superAdmin.GetFromJsonAsync<PreviewDto>($"/api/ai/knowledge/documents/{slides.Id}/preview"))!.Text;
            Assert.Contains("Present perfect: have plus the past participle", text);
            Assert.True(text.IndexOf("Present perfect", StringComparison.Ordinal) < text.IndexOf("experiences up to now", StringComparison.Ordinal));

            foreach (var (name, content, fragment) in new[]
                     {
                         ("word-list.txt", "Vocabulary: reluctant, meticulous, ubiquitous.", "meticulous"),
                         ("unit-notes.md", "# Unit 4\n\n- Conditionals: zero, first, second.", "Conditionals")
                     })
            {
                var response = await UploadKnowledgeAsync(superAdmin, name, TestFiles.Text(content));
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var document = (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
                uploaded.Add(document.Id);
                Assert.Equal("Ready", document.Status);
                Assert.Contains(fragment, (await superAdmin.GetFromJsonAsync<PreviewDto>($"/api/ai/knowledge/documents/{document.Id}/preview"))!.Text);
            }
        }
        finally
        {
            foreach (var id in uploaded) await superAdmin.DeleteAsync($"/api/ai/knowledge/documents/{id}");
        }
    }

    // ---- Nothing is stored that the guard has not seen ---------------------------------------------------------------------------

    [Fact]
    public void EveryFileStorage_ChecksWhatItStores()
    {
        // Every class in the API that stores an upload has to ask the guard before it writes: adding a tenth storage that forgets is what this
        // test is for.
        var storages = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Storage", StringComparison.Ordinal)
                        && t.GetMethod("SaveAsync") is not null)
            .ToList();

        Assert.True(storages.Count >= 9, $"expected the nine known storages, found {storages.Count}");
        foreach (var storage in storages)
        {
            Assert.True(storage.GetMethod("ValidateAsync") is not null, $"{storage.Name} has no ValidateAsync");
            Assert.True(storage.GetConstructors().Single().GetParameters().Any(p => p.ParameterType == typeof(IFileGuard)),
                $"{storage.Name} does not take an IFileGuard");
        }
    }

    [Fact]
    public void ProductionRefusesToStart_WithTheScanSwitchedOff_OrANamedScannerNobodyCanReach()
    {
        static IConfiguration Settings(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

        // A host that says nothing is fine: the scanner is required by default, and with none configured it just takes no uploads.
        Assert.DoesNotContain(ProductionSettingsGuard.Problems(Settings()), p => p.Contains("Files:Scanner"));

        Assert.Contains(ProductionSettingsGuard.Problems(Settings(("Files:Scanner:Required", "false"))), p => p.Contains("Files:Scanner:Required"));
        Assert.Contains(ProductionSettingsGuard.Problems(Settings(("Files:Scanner:Provider", "clamav"))), p => p.Contains("Files:Scanner:Host"));
        Assert.Contains(ProductionSettingsGuard.Problems(Settings(("Files:Scanner:Provider", "sophos"))), p => p.Contains("not known"));
        Assert.DoesNotContain(
            ProductionSettingsGuard.Problems(Settings(("Files:Scanner:Provider", "clamav"), ("Files:Scanner:Host", "clamav"), ("Files:Scanner:Required", "true"))),
            p => p.Contains("Files:Scanner"));
    }

    // ---- ClamAV's wire protocol, against a stand-in daemon -------------------------------------------------------------------------

    [Fact]
    public async Task ClamAvScanner_SpeaksInstream_AndReadsTheVerdict()
    {
        using var daemon = new StandInClamd();
        var scanner = new ClamAvScanner(daemon.Options(), NullLogger<ClamAvScanner>.Instance);

        // A file larger than one chunk, so the length-prefixed framing is exercised more than once.
        var clean = new byte[200_000];
        Random.Shared.NextBytes(clean);
        Assert.Equal(ScanResult.Clean, await scanner.ScanAsync(new MemoryStream(clean), "big.bin", CancellationToken.None));
        Assert.Equal(clean.Length, daemon.LastStreamLength);
        Assert.True(daemon.LastChunkCount > 1);

        var infected = await scanner.ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes(FakeFileScanner.Eicar)), "eicar.com", CancellationToken.None);
        Assert.Equal(ScanOutcome.Infected, infected.Outcome);
        Assert.Equal("Eicar-Test-Signature", infected.Signature);

        // An empty stream still ends with the zero-length chunk and gets an answer.
        Assert.Equal(ScanResult.Clean, await scanner.ScanAsync(new MemoryStream(), "empty.txt", CancellationToken.None));
    }

    [Fact]
    public async Task ClamAvScanner_NeverSaysClean_WhenTheDaemonDoesNotAnswerProperly()
    {
        // Nothing listens: connection refused.
        var unreachable = new ClamAvScanner(new FileScannerOptions { Provider = "clamav", Host = "127.0.0.1", Port = FreePort(), TimeoutSeconds = 2 },
            NullLogger<ClamAvScanner>.Instance);
        Assert.Equal(ScanOutcome.Unavailable, (await unreachable.ScanAsync(new MemoryStream([1, 2, 3]), "a.bin", CancellationToken.None)).Outcome);

        // clamd refuses the stream (its size limit), or answers something that is not a verdict.
        foreach (var reply in new[] { "INSTREAM size limit exceeded. ERROR", "", "stream: OK but not really" })
        {
            using var refusing = new StandInClamd(fixedReply: reply);
            var scanner = new ClamAvScanner(refusing.Options(), NullLogger<ClamAvScanner>.Instance);
            Assert.Equal(ScanOutcome.Unavailable, (await scanner.ScanAsync(new MemoryStream([1, 2, 3]), "a.bin", CancellationToken.None)).Outcome);
        }

        // It accepts the connection and then says nothing: the timeout ends the wait, and the answer is "could not check".
        using var silent = new StandInClamd(silent: true);
        var slow = new ClamAvScanner(silent.Options(timeoutSeconds: 1), NullLogger<ClamAvScanner>.Instance);
        var started = DateTime.UtcNow;
        Assert.Equal(ScanOutcome.Unavailable, (await slow.ScanAsync(new MemoryStream([1, 2, 3]), "a.bin", CancellationToken.None)).Outcome);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));

        // The caller giving up is not the scanner's failure: it stays a cancellation.
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ClamAvScanner(silent.Options(timeoutSeconds: 30), NullLogger<ClamAvScanner>.Instance)
                .ScanAsync(new MemoryStream([1, 2, 3]), "a.bin", cancelled.Token));
    }

    /// <summary>The same scanner against the real ClamAV of the compose stack. Skipped unless <c>EDUCENTER_CLAMAV_HOST</c> names it
    /// (<c>educenter-clamav</c> from inside the compose network), because the suite must not need a 1.5 GB daemon to run.</summary>
    [LiveClamAvFact]
    public async Task ClamAvScanner_WorksAgainstARealDaemon()
    {
        var scanner = new ClamAvScanner(
            new FileScannerOptions { Provider = "clamav", Host = Environment.GetEnvironmentVariable("EDUCENTER_CLAMAV_HOST")!, TimeoutSeconds = 60 },
            NullLogger<ClamAvScanner>.Instance);

        Assert.Equal(ScanResult.Clean, await scanner.ScanAsync(new MemoryStream(TestFiles.Pdf(2048)), "clean.pdf", CancellationToken.None));

        var big = new byte[5 * 1024 * 1024]; // several chunks, well within clamd's default stream limit
        Random.Shared.NextBytes(big);
        Assert.Equal(ScanResult.Clean, await scanner.ScanAsync(new MemoryStream(big), "big.bin", CancellationToken.None));

        var eicar = await scanner.ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes(FakeFileScanner.Eicar)), "eicar.com", CancellationToken.None);
        Assert.Equal(ScanOutcome.Infected, eicar.Outcome);
        Assert.Contains("EICAR", eicar.Signature, StringComparison.OrdinalIgnoreCase);

        // …and through the whole guard, the way an upload meets it: a text file that is the test string is refused. (ClamAV flags the EICAR file itself —
        // the string on its own, as the standard defines it — not the same string buried in other text, unlike the suite's fake scanner.)
        var guard = new FileGuard(scanner, Options.Create(new FileScannerOptions { Required = true }), new HttpContextAccessor(), NullLogger<FileGuard>.Instance);
        Assert.Equal(FileGuard.Infected,
            await guard.CheckAsync(new MemoryStream(TestFiles.Text(FakeFileScanner.Eicar)), "eicar.txt", CancellationToken.None));
        Assert.Null(await guard.CheckAsync(new MemoryStream(TestFiles.Text("just notes")), "notes.txt", CancellationToken.None));
    }

    // ---- upload paths -------------------------------------------------------------------------------------------------------------

    /// <summary>One way to upload a file, the extension it accepts (so the file passes the extension list and only its content is at issue),
    /// and where the files it stores end up.</summary>
    private sealed record UploadPath(
        string Name, string Extension, Func<string, byte[], Task<HttpResponseMessage>> Upload, Func<string> Root, Func<byte[]> Header)
    {
        /// <summary>A file of the kind this path accepts that carries the EICAR test string: right extension, right header — only the scanner can tell.</summary>
        public byte[] WithEicar() => [.. Header(), .. Encoding.ASCII.GetBytes($"\n{FakeFileScanner.Eicar}\n")];

        public int FilesOnDisk() => Directory.Exists(Root()) ? Directory.GetFiles(Root()).Length : 0;
    }

    private async Task<List<UploadPath>> UploadPathsAsync()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var student = await world.ClientAsync(c.First.Email);
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");

        var chatId = (await teacher.GetFromJsonAsync<List<ChatDto>>("/api/chats"))!.Single(chat => chat.GroupId == c.GroupId).Id;
        var session = await (await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart1" })).Content.ReadFromJsonAsync<SessionDto>();

        string Root(string configured) => Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath, configured);
        T Options<T>() where T : class => factory.Services.GetRequiredService<IOptions<T>>().Value;

        byte[] Text() => [];

        return
        [
            new("resource", ".txt", (name, bytes) => UploadResourceAsync(teacher, c.LessonId, name, bytes),
                () => Root(Options<ResourceFileOptions>().StoragePath), Text),
            new("student document", ".pdf", (name, bytes) => UploadStudentDocumentAsync(superAdmin, c.First.Id, name, bytes),
                () => Root(Options<StudentDocumentFileOptions>().StoragePath), () => TestFiles.Pdf()),
            new("chat", ".txt", (name, bytes) => UploadChatAttachmentAsync(teacher, chatId, name, bytes),
                () => Root(Options<ChatFileOptions>().StoragePath), Text),
            new("knowledge base", ".txt", (name, bytes) => UploadKnowledgeAsync(superAdmin, name, bytes),
                () => Root(Options<AiOptions>().StoragePath), Text),
            new("profile photo", ".png", (name, bytes) => UploadPhotoAsync(student, c.First.UserId, name, bytes),
                () => Root(Options<UserPhotoOptions>().StoragePath), () => TestFiles.Png()),
            new("homework", ".pdf", (name, bytes) => UploadHomeworkAsync(teacher, c.LessonId, name, bytes),
                () => Root(Options<HomeworkFileOptions>().StoragePath), () => TestFiles.Pdf()),
            new("speaking answer", ".webm", (name, bytes) => UploadSpeakingAnswerAsync(student, session!.Id, name, bytes),
                () => Root(Options<AiOptions>().SpeakingStoragePath), () => TestFiles.Webm(32)),
        ];
    }

    private static Task<HttpResponseMessage> PostFileAsync(HttpClient client, string url, string field, string fileName, byte[] bytes,
        params (string Key, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (key, value) in fields) form.Add(new StringContent(value), key);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, field, fileName);
        return client.PostAsync(url, form);
    }

    private static Task<HttpResponseMessage> UploadResourceAsync(HttpClient client, Guid lessonId, string fileName, byte[] bytes) =>
        PostFileAsync(client, "/api/resources/upload", "File", fileName, bytes, ("LessonId", lessonId.ToString()), ("Title", "Material"));

    private static Task<HttpResponseMessage> UploadStudentDocumentAsync(HttpClient client, Guid studentId, string fileName, byte[] bytes) =>
        PostFileAsync(client, "/api/student-documents/upload", "File", fileName, bytes, ("StudentId", studentId.ToString()), ("Title", "Hujjat"));

    private static Task<HttpResponseMessage> UploadChatAttachmentAsync(HttpClient client, Guid chatId, string fileName, byte[] bytes) =>
        PostFileAsync(client, $"/api/chats/{chatId}/messages", "File", fileName, bytes, ("Body", "Fayl"));

    private static Task<HttpResponseMessage> UploadKnowledgeAsync(HttpClient client, string fileName, byte[] bytes) =>
        PostFileAsync(client, "/api/ai/knowledge/documents", "file", fileName, bytes,
            ("title", Path.GetFileNameWithoutExtension(fileName)), ("scope", "Organization"), ("visibility", "Students"));

    private static Task<HttpResponseMessage> UploadPhotoAsync(HttpClient client, Guid userId, string fileName, byte[] bytes) =>
        PostFileAsync(client, $"/api/users/{userId}/photo", "file", fileName, bytes);

    private static Task<HttpResponseMessage> UploadHomeworkAsync(HttpClient client, Guid lessonId, string fileName, byte[] bytes) =>
        PostFileAsync(client, "/api/homework/upload", "file", fileName, bytes, ("lessonId", lessonId.ToString()), ("title", "Worksheet"),
            ("type", "Grammar"), ("deadline", DateTimeOffset.UtcNow.AddDays(7).ToString("O")), ("maxScore", "10"));

    private static Task<HttpResponseMessage> UploadSpeakingAnswerAsync(HttpClient client, Guid sessionId, string fileName, byte[] bytes) =>
        PostFileAsync(client, $"/api/ai/speaking/sessions/{sessionId}/turns", "audio", fileName, bytes, ("durationSeconds", "12"));

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);

    private static int FreePort()
    {
        var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private record ErrorDto(string Error);
    private record ChatDto(Guid Id, Guid? GroupId);
    private record SessionDto(Guid Id);
    private record DocumentDto(Guid Id, string Status, int ChunkCount, string? Error);
    private record PreviewDto(string Text);

    /// <summary>A clamd that speaks just enough INSTREAM to be scanned by: it reads the command, the length-prefixed chunks and the closing zero
    /// chunk, then answers "OK" — or "FOUND" when the stream carries the EICAR string — NUL-terminated as the <c>z</c> commands are.</summary>
    private sealed class StandInClamd : IDisposable
    {
        private readonly TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        private readonly string? fixedReply;
        private readonly bool silent;
        private readonly CancellationTokenSource stop = new();

        public StandInClamd(string? fixedReply = null, bool silent = false)
        {
            this.fixedReply = fixedReply;
            this.silent = silent;
            listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public int LastStreamLength { get; private set; }
        public int LastChunkCount { get; private set; }

        public FileScannerOptions Options(int timeoutSeconds = 5) => new()
        {
            Provider = "clamav", Host = "127.0.0.1", Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port, TimeoutSeconds = timeoutSeconds
        };

        private async Task AcceptAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                    _ = ServeAsync(await listener.AcceptTcpClientAsync(stop.Token));
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var stream = client.GetStream();
                    var command = new byte[10];
                    await stream.ReadExactlyAsync(command, stop.Token);
                    if (!command.AsSpan().SequenceEqual("zINSTREAM\0"u8)) return;

                    using var received = new MemoryStream();
                    var chunks = 0;
                    var length = new byte[4];
                    while (true)
                    {
                        await stream.ReadExactlyAsync(length, stop.Token);
                        var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(length);
                        if (size == 0) break;
                        var chunk = new byte[size];
                        await stream.ReadExactlyAsync(chunk, stop.Token);
                        received.Write(chunk);
                        chunks++;
                    }

                    LastStreamLength = (int)received.Length;
                    LastChunkCount = chunks;
                    if (silent)
                    {
                        await Task.Delay(Timeout.Infinite, stop.Token);
                        return;
                    }

                    var reply = fixedReply
                                ?? (received.ToArray().AsSpan().IndexOf(Encoding.ASCII.GetBytes(FakeFileScanner.Eicar)) >= 0
                                    ? $"stream: {FakeFileScanner.Signature} FOUND"
                                    : "stream: OK");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(reply).Append((byte)0).ToArray(), stop.Token);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
        }

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
            stop.Dispose();
        }
    }
}
