using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Notifications.Domain;

namespace EduCenter.Api.Notifications;

public record NotificationTypeInfo(string Type, string Label, string Description, string[] Roles);

/// <summary>What each notification type is, and which roles receive it — drives the settings screen, so a user
/// only sees switches for messages they can actually get.</summary>
public static class NotificationCatalog
{
    public static readonly NotificationTypeInfo[] All =
    [
        new(NotificationType.PaymentDue, "To'lov muddati", "To'lov kuniga 3 kun qolganda va to'lov kuni.", [RoleCode.Student, RoleCode.Parent]),
        new(NotificationType.PaymentOverdue, "To'lov kechikdi", "To'lov muddatidan 3 kun o'tganda.", [RoleCode.Student, RoleCode.Parent]),
        new(NotificationType.Absent, "Darsga kelmadi", "Farzandingiz darsga kelmagan deb belgilanganda.", [RoleCode.Parent]),
        new(NotificationType.AbsenceStreak, "Ketma-ket yo'qlik", "Guruhingizdagi student ketma-ket 2 ta darsga kelmaganda.", [RoleCode.Teacher]),
        new(NotificationType.ExamResult, "Imtihon natijasi", "Imtihon natijasi kiritilganda.", [RoleCode.Student, RoleCode.Parent]),
        new(NotificationType.HomeworkCreated, "Yangi homework", "Darsga homework berilganda.", [RoleCode.Student]),
        new(NotificationType.HomeworkChanged, "Homework muddati o'zgardi", "Homework topshirish muddati o'zgartirilganda.", [RoleCode.Student]),
        new(NotificationType.HomeworkDeadline, "Homework muddati", "Topshirilmagan homework muddatiga 24 soat qolganda.", [RoleCode.Student]),
        new(NotificationType.HomeworkReviewed, "Homework baholandi", "O'qituvchi homeworkni baholaganda.", [RoleCode.Student, RoleCode.Parent]),
        new(NotificationType.LessonReminder, "Dars eslatmasi", "Dars boshlanishiga 2 soat qolganda.", [RoleCode.Student, RoleCode.Teacher]),
        new(NotificationType.ScheduleChanged, "Jadval o'zgarishi", "Guruh jadvaliga yangi dars qo'shilganda, dars ko'chirilganda yoki bekor qilinganda.", [RoleCode.Student, RoleCode.Parent, RoleCode.Teacher]),
        new(NotificationType.TeacherAssigned, "O'qituvchi biriktirildi", "Guruhga o'qituvchi biriktirilganda.", [RoleCode.Student, RoleCode.Teacher, RoleCode.SupportTeacher]),
        new(NotificationType.GroupClosed, "Guruh yopildi", "Guruh yakunlanganda yoki bekor qilinganda.", [RoleCode.Student, RoleCode.Teacher, RoleCode.SupportTeacher]),
        new(NotificationType.LearningSignal, "O'zlashtirish signali", "Guruhingizdagi student Writing grammatikasi, Listening, Speaking fluency yoki Reading bo'yicha past natija ko'rsatganda.", [RoleCode.Teacher]),
        new(NotificationType.SupportRecommended, "Support tavsiyasi", "Filialdagi student uchun support tavsiya qilinganda.", [RoleCode.Administrator]),
        new(NotificationType.SupportAssigned, "Support biriktirildi", "Yordamchi o'qituvchi biriktirilganda.", [RoleCode.SupportTeacher, RoleCode.Student]),
        new(NotificationType.TaskAssigned, "Yangi vazifa", "Qo'ng'iroq navbatiga avtomatik vazifa tushganda (qarz, yo'qlik, trial).", [RoleCode.Operator, RoleCode.Administrator]),
        new(NotificationType.SpeakingReviewed, "Speaking bahosi ko'rib chiqildi", "O'qituvchi speaking mashqingizni eshitib, o'z bahosini qo'yganda.", [RoleCode.Student]),
        new(NotificationType.CredentialSubmitted, "Tasdiqlash kutilmoqda", "O'qituvchi portfoliosiga yangi sertifikat yoki ball qo'shganda.", [RoleCode.Administrator]),
        new(NotificationType.CredentialVerified, "Portfolio tasdiqlandi", "Administrator sertifikatingizni tasdiqlaganda.", [RoleCode.Teacher, RoleCode.SupportTeacher]),
        new(NotificationType.WritingEvaluated, "AI writing tahlili", "Inshongiz AI tomonidan baholanganda yoki o'qituvchi bahoni ko'rib chiqqanda.", [RoleCode.Student]),
        new(NotificationType.Announcement, "E'lon", "Markaz, filial yoki guruhingiz uchun yangi e'lon chiqarilganda.",
            [RoleCode.SuperAdmin, RoleCode.Administrator, RoleCode.Operator, RoleCode.Teacher, RoleCode.SupportTeacher, RoleCode.Student, RoleCode.Parent]),
        new(NotificationType.ComplaintCreated, "Yangi shikoyat", "Filialingizga yangi shikoyat kelganda.", [RoleCode.Administrator]),
        new(NotificationType.ComplaintAssigned, "Shikoyat biriktirildi", "Sizga shikoyat ko'rib chiqish uchun biriktirilganda.", [RoleCode.SuperAdmin, RoleCode.Administrator, RoleCode.Operator]),
        new(NotificationType.ComplaintResolved, "Shikoyatingiz ko'rib chiqildi", "Yuborgan shikoyatingizga yechim yozilib, bahoingiz so'ralganda.",
            [RoleCode.Administrator, RoleCode.Operator, RoleCode.Teacher, RoleCode.Student, RoleCode.Parent]),
    ];

    public static NotificationTypeInfo? Find(string type) => All.FirstOrDefault(t => t.Type == type);

    public static IEnumerable<NotificationTypeInfo> ForRoles(IReadOnlyCollection<string> roles) =>
        All.Where(t => t.Roles.Any(roles.Contains));
}
