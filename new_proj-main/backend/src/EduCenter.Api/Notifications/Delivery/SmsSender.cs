using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduCenter.Modules.Notifications.Domain;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Notifications.Delivery;

/// <summary>SMS through Eskiz (notify.eskiz.uz): log in for a token, then send. Configure <c>Notifications:Sms</c>; it is off by default because
/// every message costs money. The daily allowance is enforced by <see cref="NotificationDeliveryJob"/>, which can count what went out.
/// (Eskiz only delivers texts whose template it has approved — the standard "title + link" text has to be registered with them.)</summary>
public sealed partial class SmsSender(IOptionsMonitor<SmsOptions> options, IHttpClientFactory httpClients) : INotificationSender
{
    public const string HttpClientName = "sms";

    private readonly SemaphoreSlim loginLock = new(1, 1);
    private string? token;

    public NotificationChannel Channel => NotificationChannel.Sms;

    public bool IsEnabled
    {
        get
        {
            var o = options.CurrentValue;
            var hasCredentials = !string.IsNullOrWhiteSpace(o.ApiKey) || (!string.IsNullOrWhiteSpace(o.Login) && !string.IsNullOrWhiteSpace(o.Password));
            return o.Enabled && o.Provider.Equals("Eskiz", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(o.BaseUrl) && hasCredentials;
        }
    }

    public async Task<SendResult> SendAsync(NotificationTarget target, NotificationEnvelope message, CancellationToken ct)
    {
        if (!IsEnabled) return SendResult.Skip("SMS o'chirilgan.");
        if (NormalizePhone(target.Phone) is not { } phone) return SendResult.Skip("Telefon raqami yo'q yoki yaroqsiz.");

        var o = options.CurrentValue;
        using var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, o.TimeoutSeconds));

        // A token lives for weeks; when Eskiz says it is stale, log in once more and try again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var bearer = await TokenAsync(http, o, refresh: attempt > 0, ct);
            if (bearer is null) return SendResult.Failed("Eskiz'ga kirib bo'lmadi (login yoki parol noto'g'ri).");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/message/sms/send")
            {
                Content = new MultipartFormDataContent
                {
                    { new StringContent(phone), "mobile_phone" },
                    { new StringContent(message.Body), "message" },
                    { new StringContent(o.Sender), "from" },
                },
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
            if (response.IsSuccessStatusCode) return SendResult.Ok();

            var detail = await response.Content.ReadAsStringAsync(ct);
            return SendResult.Failed($"Eskiz {(int)response.StatusCode}: {(detail.Length <= 120 ? detail : detail[..120])}");
        }

        return SendResult.Failed("Eskiz tokenni qabul qilmadi.");
    }

    private async Task<string?> TokenAsync(HttpClient http, SmsOptions o, bool refresh, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(o.ApiKey)) return o.ApiKey;

        await loginLock.WaitAsync(ct);
        try
        {
            if (token is not null && !refresh) return token;

            using var login = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/auth/login")
            {
                Content = new MultipartFormDataContent { { new StringContent(o.Login), "email" }, { new StringContent(o.Password), "password" } },
            };
            using var response = await http.SendAsync(login, ct);
            if (!response.IsSuccessStatusCode) return token = null;

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            token = body.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var value) ? value.GetString() : null;
            return token;
        }
        finally
        {
            loginLock.Release();
        }
    }

    /// <summary>Uzbek mobile numbers as the provider wants them: 998 and nine digits, no plus or spaces. Null when it is not one.</summary>
    public static string? NormalizePhone(string? phone)
    {
        var digits = NonDigits().Replace(phone ?? "", "");
        if (digits.Length == 9) digits = "998" + digits;
        return digits.Length == 12 && digits.StartsWith("998", StringComparison.Ordinal) ? digits : null;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}
