namespace EduCenter.Api.Notifications.Delivery;

/// <summary>Where the product lives on the internet. A message that leaves the app links back to it with an absolute address.</summary>
public class AppOptions
{
    public string PublicUrl { get; set; } = "";
}

/// <summary><c>Notifications:Email</c>. Off until a mail server is configured; the password belongs in <c>appsettings.Local.json</c> or the environment, never in git.</summary>
public class EmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "EduCenter";
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary><c>Notifications:Sms</c>. SMS costs money, so it is off by default and capped per day even when on.</summary>
public class SmsOptions
{
    public bool Enabled { get; set; }

    /// <summary>Only "Eskiz" (notify.eskiz.uz) is implemented.</summary>
    public string Provider { get; set; } = "Eskiz";

    public string BaseUrl { get; set; } = "https://notify.eskiz.uz/api";
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>A token issued in advance; used instead of logging in with <see cref="Login"/> and <see cref="Password"/>.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The approved sender name or short number the message comes from.</summary>
    public string Sender { get; set; } = "4546";

    public int DailyLimit { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 15;
}
