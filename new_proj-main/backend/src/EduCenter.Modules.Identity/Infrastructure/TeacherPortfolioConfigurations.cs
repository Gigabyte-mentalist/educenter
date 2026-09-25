using EduCenter.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Identity.Infrastructure;

public class TeacherProfileConfiguration : IEntityTypeConfiguration<TeacherProfile>
{
    public void Configure(EntityTypeBuilder<TeacherProfile> builder)
    {
        builder.ToTable("teacher_profiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Bio).HasMaxLength(TeacherProfile.MaxBio);
        builder.Property(p => p.Specialization).HasMaxLength(TeacherProfile.MaxSpecialization);
        builder.Property(p => p.Education).HasMaxLength(TeacherProfile.MaxEducation);
        builder.Property(p => p.Languages).HasMaxLength(TeacherProfile.MaxLanguages);
        builder.HasIndex(p => p.UserId).IsUnique();
        // The portfolio goes with the account: a deleted teacher takes their rows with them.
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeacherCredentialConfiguration : IEntityTypeConfiguration<TeacherCredential>
{
    public void Configure(EntityTypeBuilder<TeacherCredential> builder)
    {
        builder.ToTable("teacher_credentials");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Title).HasMaxLength(TeacherCredential.MaxTitle).IsRequired();
        builder.Property(c => c.Issuer).HasMaxLength(TeacherCredential.MaxIssuer);
        builder.Property(c => c.Score).HasMaxLength(TeacherCredential.MaxScore);
        builder.Property(c => c.Reference).HasMaxLength(TeacherCredential.MaxReference);
        builder.Property(c => c.Note).HasMaxLength(TeacherCredential.MaxNote);
        builder.Property(c => c.StorageKey).HasMaxLength(64);
        builder.Property(c => c.FileName).HasMaxLength(255);
        builder.Property(c => c.FileContentType).HasMaxLength(128);
        builder.HasIndex(c => new { c.OrganizationId, c.UserId });
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeacherExperienceConfiguration : IEntityTypeConfiguration<TeacherExperience>
{
    public void Configure(EntityTypeBuilder<TeacherExperience> builder)
    {
        builder.ToTable("teacher_experiences");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Employer).HasMaxLength(TeacherExperience.MaxEmployer).IsRequired();
        builder.Property(e => e.Position).HasMaxLength(TeacherExperience.MaxPosition).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(TeacherExperience.MaxDescription);
        builder.HasIndex(e => new { e.OrganizationId, e.UserId });
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
