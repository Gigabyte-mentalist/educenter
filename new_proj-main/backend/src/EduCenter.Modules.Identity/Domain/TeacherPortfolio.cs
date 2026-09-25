using EduCenter.SharedKernel;

namespace EduCenter.Modules.Identity.Domain;

/// <summary>"About" on a teacher's portfolio (docs/21.09-audit-jurnali S05.02). A teacher is a <see cref="User"/> — there is no
/// separate Teacher entity — so this hangs off the user, one row per teacher, created when they first fill it in.</summary>
public class TeacherProfile : TenantEntity
{
    public const int MaxBio = 2000;
    public const int MaxSpecialization = 200;
    public const int MaxEducation = 500;
    public const int MaxLanguages = 200;
    public const int MaxYearsOfExperience = 60;

    private TeacherProfile() { } // EF Core

    public TeacherProfile(Guid organizationId, Guid userId)
    {
        OrganizationId = organizationId;
        UserId = userId;
    }

    public Guid UserId { get; private set; }
    public string? Bio { get; private set; }
    /// <summary>What they teach best — "IELTS Writing".</summary>
    public string? Specialization { get; private set; }
    public int? YearsOfExperience { get; private set; }
    public string? Education { get; private set; }
    public string? Languages { get; private set; }
    public DateOnly? TeachingSince { get; private set; }

    public void Update(string? bio, string? specialization, int? yearsOfExperience, string? education, string? languages, DateOnly? teachingSince)
    {
        Bio = bio;
        Specialization = specialization;
        YearsOfExperience = yearsOfExperience;
        Education = education;
        Languages = languages;
        TeachingSince = teachingSince;
        MarkUpdated();
    }
}

public enum CredentialType
{
    /// <summary>A score on an exam: IELTS, TOEFL, a CEFR level.</summary>
    ExamScore,
    /// <summary>A teaching qualification: CELTA, TESOL.</summary>
    Certificate,
    Degree,
    Course,
    Award,
    Other
}

/// <summary>
/// One line of a teacher's qualifications — an IELTS band, a CELTA, a degree. They add it themselves, so it starts <b>unverified</b>;
/// an administrator looks at the document (the scan, or the reference number) and confirms it. Confirmation belongs to what was
/// looked at: change any of it afterwards and the confirmation is gone, so "verify, then swap in something better" cannot happen.
/// </summary>
public class TeacherCredential : TenantEntity
{
    public const int MaxPerTeacher = 50;
    public const int MaxTitle = 200;
    public const int MaxIssuer = 200;
    public const int MaxScore = 50;
    public const int MaxReference = 500;
    public const int MaxNote = 500;

    private TeacherCredential() { } // EF Core

    public TeacherCredential(Guid organizationId, Guid userId, CredentialType type, string title, string? issuer, string? score,
        DateOnly? issuedOn, DateOnly? expiresOn, string? reference, string? note)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Type = type;
        Title = title;
        Issuer = issuer;
        Score = score;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Reference = reference;
        Note = note;
    }

    public Guid UserId { get; private set; }
    public CredentialType Type { get; private set; }
    /// <summary>"IELTS Academic", "CELTA".</summary>
    public string Title { get; private set; } = default!;
    /// <summary>"British Council".</summary>
    public string? Issuer { get; private set; }
    /// <summary>Text, because it holds an IELTS "8.0 (L 8.5, R 8.0, W 7.5, S 8.0)", a CEFR "C1" and a "Pass A" alike.</summary>
    public string? Score { get; private set; }
    public DateOnly? IssuedOn { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    /// <summary>Where the administrator can check it: a TRF or certificate number, a verification link. Never checked automatically.</summary>
    public string? Reference { get; private set; }
    /// <summary>The teacher's own remark. The only field that is not part of what an administrator confirms.</summary>
    public string? Note { get; private set; }

    /// <summary>A scan or PDF of the document — optional.</summary>
    public string? StorageKey { get; private set; }
    public string? FileName { get; private set; }
    public string? FileContentType { get; private set; }
    public long? FileSize { get; private set; }

    public bool IsVerified { get; private set; }
    public Guid? VerifiedBy { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }

    public bool HasFile => StorageKey is not null;

    public bool IsExpired(DateOnly today) => ExpiresOn is { } expires && expires < today;

    /// <summary>Applies an edit. Returns true when it took a confirmation away — that is, the credential had been verified and something
    /// the administrator looked at has changed (the note is the only thing that may).</summary>
    public bool Update(CredentialType type, string title, string? issuer, string? score,
        DateOnly? issuedOn, DateOnly? expiresOn, string? reference, string? note)
    {
        var changesWhatWasChecked = type != Type || title != Title || issuer != Issuer || score != Score
            || issuedOn != IssuedOn || expiresOn != ExpiresOn || reference != Reference;

        Type = type;
        Title = title;
        Issuer = issuer;
        Score = score;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Reference = reference;
        Note = note;
        MarkUpdated();

        return changesWhatWasChecked && WithdrawVerification();
    }

    /// <summary>Puts the (new) file in. Returns whether the credential lost its confirmation — the file is part of what was checked.</summary>
    public bool AttachFile(string key, string name, string contentType, long size)
    {
        StorageKey = key;
        FileName = name;
        FileContentType = contentType;
        FileSize = size;
        MarkUpdated();
        return WithdrawVerification();
    }

    /// <summary>Takes the file out. Returns the key it was stored under (so the caller can delete it from disk), or null when there was none.</summary>
    public string? DetachFile(out bool verificationWithdrawn)
    {
        var key = StorageKey;
        StorageKey = null;
        FileName = null;
        FileContentType = null;
        FileSize = null;
        MarkUpdated();
        verificationWithdrawn = key is not null && WithdrawVerification();
        return key;
    }

    public void Verify(Guid verifiedBy)
    {
        IsVerified = true;
        VerifiedBy = verifiedBy;
        VerifiedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Unverify()
    {
        WithdrawVerification();
        MarkUpdated();
    }

    private bool WithdrawVerification()
    {
        if (!IsVerified) return false;
        IsVerified = false;
        VerifiedBy = null;
        VerifiedAt = null;
        return true;
    }
}

/// <summary>A job the teacher has held. <see cref="ToYear"/> empty means they still work there.</summary>
public class TeacherExperience : TenantEntity
{
    public const int MaxPerTeacher = 30;
    public const int MaxEmployer = 200;
    public const int MaxPosition = 200;
    public const int MaxDescription = 1000;

    private TeacherExperience() { } // EF Core

    public TeacherExperience(Guid organizationId, Guid userId, string employer, string position, int fromYear, int? toYear, string? description)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Employer = employer;
        Position = position;
        FromYear = fromYear;
        ToYear = toYear;
        Description = description;
    }

    public Guid UserId { get; private set; }
    public string Employer { get; private set; } = default!;
    public string Position { get; private set; } = default!;
    public int FromYear { get; private set; }
    public int? ToYear { get; private set; }
    public string? Description { get; private set; }

    public void Update(string employer, string position, int fromYear, int? toYear, string? description)
    {
        Employer = employer;
        Position = position;
        FromYear = fromYear;
        ToYear = toYear;
        Description = description;
        MarkUpdated();
    }
}
