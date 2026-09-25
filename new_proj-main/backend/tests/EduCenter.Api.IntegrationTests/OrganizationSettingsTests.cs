using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduCenter.Api.Organizations;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Organization settings (docs/21.09-audit-jurnali S07.01): the Super Admin sets the name, currency, working hours, brand colour and
/// logo; everyone signed in reads the public part of it. The demo organization is shared by the whole suite, so each test that
/// changes it puts it back.
/// </summary>
[Collection("Api")]
public class OrganizationSettingsTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public async Task SuperAdmin_ChangesTheSettings_AndEveryoneReadsThemBack()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var before = await CurrentAsync(superAdmin);
        try
        {
            var updated = await superAdmin.PatchAsJsonAsync($"/api/organizations/{before.Id}", new
            {
                name = "  Yangi Markaz  ", currency = "USD", workdayStart = "09:30", workdayEnd = "20:00", brandColor = "#1D4ED8",
            });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            var teacher = await world.ClientAsync("teacher@demo-center.test");
            var current = await CurrentAsync(teacher);
            Assert.Equal("Yangi Markaz", current.Name); // trimmed
            Assert.Equal("USD", current.Currency);
            Assert.Equal("09:30", current.WorkdayStart);
            Assert.Equal("20:00", current.WorkdayEnd);
            Assert.Equal("#1d4ed8", current.BrandColor); // stored lower-case
            Assert.Null(current.LogoVersion);

            // A partial change keeps the rest.
            Assert.Equal(HttpStatusCode.OK, (await superAdmin.PatchAsJsonAsync($"/api/organizations/{before.Id}", new { currency = "EUR" })).StatusCode);
            var partial = await CurrentAsync(superAdmin);
            Assert.Equal("EUR", partial.Currency);
            Assert.Equal("Yangi Markaz", partial.Name);
            Assert.Equal("#1d4ed8", partial.BrandColor);

            // An empty colour goes back to the product's own.
            Assert.Equal(HttpStatusCode.OK, (await superAdmin.PatchAsJsonAsync($"/api/organizations/{before.Id}", new { brandColor = "" })).StatusCode);
            Assert.Null((await CurrentAsync(superAdmin)).BrandColor);
        }
        finally
        {
            await RestoreAsync(superAdmin, before);
        }
    }

    [Fact]
    public async Task OnlyTheSuperAdmin_MayChangeTheSettings()
    {
        var id = (await CurrentAsync(await world.ClientAsync("superadmin@demo-center.test"))).Id;

        foreach (var email in new[] { "admin@demo-center.test", "operator@demo-center.test", "teacher@demo-center.test", "student@demo-center.test", "developer@demo-center.test" })
        {
            var client = await world.ClientAsync(email);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/organizations/{id}", new { name = "Hack" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await UploadLogoAsync(client, id, "logo.png", Png)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/organizations/{id}/logo")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PatchAsJsonAsync($"/api/organizations/{id}", new { name = "Hack" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/organizations/current")).StatusCode);
    }

    [Fact]
    public async Task InvalidValues_AreRefused_AndNothingIsChanged()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var before = await CurrentAsync(superAdmin);
        var url = $"/api/organizations/{before.Id}";

        var bad = new object[]
        {
            new { brandColor = "1d4ed8" },                     // no #
            new { brandColor = "#12345" },                     // short
            new { brandColor = "#zzzzzz" },                    // not hex
            new { brandColor = "red" },
            new { brandColor = "#ffff00" },                    // yellow: white text on it is unreadable
            new { brandColor = "#ffffff" },
            new { currency = "GBP" },                          // not a currency the product formats
            new { currency = "uzs" },
            new { name = "   " },
            new { name = new string('x', 201) },
            new { workdayStart = "8am" },
            new { workdayEnd = "25:00" },
            new { workdayStart = "18:00", workdayEnd = "09:00" }, // ends before it starts
            new { workdayStart = "22:00" },                       // later than the stored end (21:00)
        };
        foreach (var body in bad)
            Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.PatchAsJsonAsync(url, body)).StatusCode);

        var tooLight = await superAdmin.PatchAsJsonAsync(url, new { brandColor = "#ffff00" });
        Assert.Contains("kontrast", await tooLight.Content.ReadAsStringAsync());

        Assert.Equal(before, await CurrentAsync(superAdmin)); // none of it stuck
    }

    [Fact]
    public async Task Logo_IsUploadedFetchedReplacedAndRemoved_AndOnlyASmallRasterImageIsAccepted()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var student = await world.ClientAsync("student@demo-center.test");
        var id = (await CurrentAsync(superAdmin)).Id;
        var url = $"/api/organizations/{id}/logo";
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(url)).StatusCode); // none yet

            var uploaded = await UploadLogoAsync(superAdmin, id, "logo.png", Png);
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
            var firstVersion = (await uploaded.Content.ReadFromJsonAsync<CurrentDto>())!.LogoVersion;
            Assert.NotNull(firstVersion);

            // A student sees the logo too — it is on every screen and on the sign-in page of the centre.
            var fetched = await student.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
            Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Png, await fetched.Content.ReadAsByteArrayAsync());
            Assert.Contains("nosniff", fetched.Headers.GetValues("X-Content-Type-Options"));
            Assert.Contains("private", fetched.Headers.CacheControl!.ToString());
            Assert.Contains("max-age=3600", fetched.Headers.CacheControl!.ToString());
            Assert.Equal(firstVersion, (await CurrentAsync(student)).LogoVersion);

            var second = await UploadLogoAsync(superAdmin, id, "newer.webp", TestFiles.Webp());
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.NotEqual(firstVersion, (await second.Content.ReadFromJsonAsync<CurrentDto>())!.LogoVersion);
            Assert.Equal("image/webp", (await student.GetAsync(url)).Content.Headers.ContentType?.MediaType);

            // 1 MB is the limit; one byte more is a readable 400, and none of the refusals touched the logo.
            var tooBig = await UploadLogoAsync(superAdmin, id, "big.png", new byte[1024 * 1024 + 1]);
            Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
            Assert.Contains("1 MB", await tooBig.Content.ReadAsStringAsync());
            foreach (var name in new[] { "logo.svg", "logo.html", "logo.png.exe", "noextension" })
                Assert.Equal(HttpStatusCode.BadRequest, (await UploadLogoAsync(superAdmin, id, name, Png)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await UploadLogoAsync(superAdmin, id, "empty.png", [])).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.PostAsync(url, new MultipartFormDataContent())).StatusCode);
            Assert.Equal(TestFiles.Webp(), await (await student.GetAsync(url)).Content.ReadAsByteArrayAsync());

            Assert.Equal(HttpStatusCode.OK, (await UploadLogoAsync(superAdmin, id, "max.png", TestFiles.Png(1024 * 1024))).StatusCode);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync(url)).StatusCode); // nothing to remove is fine
        }

        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(url)).StatusCode);
        Assert.Null((await CurrentAsync(student)).LogoVersion);
    }

    [Fact]
    public async Task Everyone_SeesOnlyThePublicFields_OfTheirOrganization()
    {
        foreach (var email in new[] { "student@demo-center.test", "parent@demo-center.test", "teacher@demo-center.test", "superadmin@demo-center.test" })
        {
            var response = await (await world.ClientAsync(email)).GetAsync("/api/organizations/current");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var fields = json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
            // No slug, no branch count, no AI limits: those are the office's business.
            Assert.Equal(new[] { "brandColor", "currency", "id", "logoVersion", "name", "workdayEnd", "workdayStart" }, fields);
        }
    }

    [Fact]
    public async Task AnotherOrganization_IsNeitherChangedNorRead()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var other = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await superAdmin.PatchAsJsonAsync($"/api/organizations/{other}", new { name = "Boshqa" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadLogoAsync(superAdmin, other, "logo.png", Png)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await superAdmin.DeleteAsync($"/api/organizations/{other}/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync($"/api/organizations/{other}/logo")).StatusCode);
    }

    [Fact]
    public void AHandEditedRow_FallsBackToTheDefaults_InsteadOfBreakingThePage()
    {
        foreach (var json in new[] { null, "", "not json", "{}", """{"currency":"XXX","workdayStart":"soon","workdayEnd":"21:00"}""", """{"workdayStart":"20:00","workdayEnd":"08:00"}""" })
        {
            var resolved = OrganizationSettingsCatalog.Resolve(json);
            Assert.Equal("UZS", resolved.Currency);
            Assert.Equal(new TimeOnly(8, 0), resolved.WorkdayStart);
            Assert.Equal(new TimeOnly(21, 0), resolved.WorkdayEnd);
        }

        Assert.Null(OrganizationSettingsCatalog.ResolveBranding("""{"brandColor":"blue","logoKey":null}""").BrandColor);
        Assert.Equal("#26645a", OrganizationSettingsCatalog.ResolveBranding("""{"brandColor":"#26645A"}""").BrandColor);
        Assert.Null(OrganizationSettingsCatalog.ResolveBranding("nonsense").LogoKey);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static async Task<CurrentDto> CurrentAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CurrentDto>("/api/organizations/current"))!;

    private static async Task RestoreAsync(HttpClient superAdmin, CurrentDto original)
    {
        var restored = await superAdmin.PatchAsJsonAsync($"/api/organizations/{original.Id}", new
        {
            name = original.Name, currency = original.Currency, workdayStart = original.WorkdayStart, workdayEnd = original.WorkdayEnd,
            brandColor = original.BrandColor ?? "",
        });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    private static Task<HttpResponseMessage> UploadLogoAsync(HttpClient client, Guid organizationId, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PostAsync($"/api/organizations/{organizationId}/logo", form);
    }

    private record CurrentDto(Guid Id, string Name, string Currency, string WorkdayStart, string WorkdayEnd, string? BrandColor, string? LogoVersion);
}
