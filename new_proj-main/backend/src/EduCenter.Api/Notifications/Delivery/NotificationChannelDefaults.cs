using EduCenter.Modules.Notifications.Domain;

namespace EduCenter.Api.Notifications.Delivery;

/// <summary>What a person gets without ever opening the settings screen. A row in <c>notification_preferences</c> overrides it.
/// The inbox is on for everything; mail only for the four things a family should not miss; SMS for nothing (it costs money).
/// An organization's own defaults arrive with the organization settings (S07.01).</summary>
public static class NotificationChannelDefaults
{
    private static readonly HashSet<string> EmailByDefault =
    [
        NotificationType.PaymentDue, NotificationType.PaymentOverdue, NotificationType.AbsenceStreak, NotificationType.ExamResult,
    ];

    /// <summary>The channels beyond the inbox that a message can be delivered on.</summary>
    public static readonly NotificationChannel[] Outside = [NotificationChannel.Email, NotificationChannel.Sms];

    public static bool IsOnByDefault(string type, NotificationChannel channel) => channel switch
    {
        NotificationChannel.InApp => true,
        NotificationChannel.Email => EmailByDefault.Contains(type),
        _ => false,
    };
}
