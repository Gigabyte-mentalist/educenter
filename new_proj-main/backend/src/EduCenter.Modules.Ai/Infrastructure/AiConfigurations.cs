using EduCenter.Modules.Ai.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Ai.Infrastructure;

public class AiRequestConfiguration : IEntityTypeConfiguration<AiRequest>
{
    public void Configure(EntityTypeBuilder<AiRequest> builder)
    {
        builder.ToTable("ai_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Feature).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.TaskKind).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.PromptVersion).HasMaxLength(80).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(40);
        builder.Property(r => r.Detail).HasMaxLength(AiRequest.MaxDetailLength);
        builder.Property(r => r.Provider).HasMaxLength(60);
        builder.Property(r => r.Model).HasMaxLength(120);
        builder.Property(r => r.CostUsd).HasPrecision(18, 6);

        // Daily limit checks (user × feature × today) and usage reports (organization × period).
        builder.HasIndex(r => new { r.UserId, r.Feature, r.CreatedAt });
        builder.HasIndex(r => new { r.OrganizationId, r.CreatedAt });
    }
}

public class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).HasMaxLength(KnowledgeDocument.MaxTitleLength).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.StorageKey).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Visibility).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Error).HasMaxLength(KnowledgeDocument.MaxErrorLength);
        builder.Property(d => d.EmbeddingModel).HasMaxLength(120);
        builder.HasIndex(d => new { d.OrganizationId, d.CreatedAt });
    }
}

public class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Text).HasMaxLength(KnowledgeChunk.MaxTextLength).IsRequired();
        builder.Property(c => c.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Visibility).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.EmbeddingModel).HasMaxLength(120);
        builder.Property(c => c.Embedding).HasColumnType($"vector({KnowledgeDocument.EmbeddingDimensions})");
        builder.HasIndex(c => c.DocumentId);
        // Retrieval always filters by organization first; the vector (HNSW) and full-text (GIN) indexes are added in the
        // migration, since neither is expressible through the model builder.
        builder.HasIndex(c => c.OrganizationId);
    }
}

public class SpeakingSessionConfiguration : IEntityTypeConfiguration<SpeakingSession>
{
    public void Configure(EntityTypeBuilder<SpeakingSession> builder)
    {
        builder.ToTable("speaking_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Mode).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Topic).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Error).HasMaxLength(SpeakingSession.MaxErrorLength);
        builder.Property(s => s.EstimatedBand).HasPrecision(3, 1);
        builder.Property(s => s.FluencyBand).HasPrecision(3, 1);
        builder.Property(s => s.LexicalBand).HasPrecision(3, 1);
        builder.Property(s => s.GrammarBand).HasPrecision(3, 1);
        builder.Property(s => s.RelevanceBand).HasPrecision(3, 1);
        builder.Ignore(s => s.IsOverridden);
        builder.Property(s => s.TeacherBand).HasPrecision(3, 1);
        builder.Property(s => s.TeacherFluencyBand).HasPrecision(3, 1);
        builder.Property(s => s.TeacherLexicalBand).HasPrecision(3, 1);
        builder.Property(s => s.TeacherGrammarBand).HasPrecision(3, 1);
        builder.Property(s => s.TeacherComment).HasMaxLength(SpeakingSession.MaxCommentLength);
        builder.HasIndex(s => new { s.UserId, s.CreatedAt });
    }
}

public class SpeakingTurnConfiguration : IEntityTypeConfiguration<SpeakingTurn>
{
    public void Configure(EntityTypeBuilder<SpeakingTurn> builder)
    {
        builder.ToTable("speaking_turns");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Question).HasMaxLength(SpeakingTurn.MaxQuestionLength).IsRequired();
        builder.Property(t => t.Transcript).HasMaxLength(SpeakingTurn.MaxTranscriptLength);
        builder.Property(t => t.AudioKey).HasMaxLength(100);
        builder.HasIndex(t => new { t.SessionId, t.Ordinal }).IsUnique();
    }
}

public class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable("ai_conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Module).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Title).HasMaxLength(AiConversation.MaxTitleLength).IsRequired();
        builder.HasIndex(c => new { c.UserId, c.Module, c.LastMessageAt });
    }
}

public class AiConversationMessageConfiguration : IEntityTypeConfiguration<AiConversationMessage>
{
    public void Configure(EntityTypeBuilder<AiConversationMessage> builder)
    {
        builder.ToTable("ai_conversation_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Content).HasMaxLength(AiConversationMessage.MaxContentLength).IsRequired();
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}

public class WritingEvaluationConfiguration : IEntityTypeConfiguration<WritingEvaluation>
{
    public void Configure(EntityTypeBuilder<WritingEvaluation> builder)
    {
        builder.ToTable("writing_evaluations");
        builder.HasKey(e => e.Id);
        builder.Ignore(e => e.IsOverridden);
        builder.Property(e => e.TaskType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TaskPrompt).HasMaxLength(WritingEvaluation.MaxTaskPromptLength).IsRequired();
        builder.Property(e => e.Essay).HasMaxLength(WritingEvaluation.MaxEssayLength).IsRequired();
        builder.Property(e => e.PromptVersion).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Error).HasMaxLength(WritingEvaluation.MaxErrorLength);
        builder.Property(e => e.Model).HasMaxLength(120);
        builder.Property(e => e.TeacherComment).HasMaxLength(WritingEvaluation.MaxCommentLength);

        builder.Property(e => e.EstimatedBand).HasPrecision(3, 1);
        builder.Property(e => e.TaskResponseBand).HasPrecision(3, 1);
        builder.Property(e => e.CoherenceBand).HasPrecision(3, 1);
        builder.Property(e => e.LexicalBand).HasPrecision(3, 1);
        builder.Property(e => e.GrammarBand).HasPrecision(3, 1);
        builder.Property(e => e.TeacherBand).HasPrecision(3, 1);
        builder.Property(e => e.TeacherTaskResponseBand).HasPrecision(3, 1);
        builder.Property(e => e.TeacherCoherenceBand).HasPrecision(3, 1);
        builder.Property(e => e.TeacherLexicalBand).HasPrecision(3, 1);
        builder.Property(e => e.TeacherGrammarBand).HasPrecision(3, 1);

        builder.HasIndex(e => new { e.StudentId, e.CreatedAt });
        builder.HasIndex(e => new { e.RequestedBy, e.CreatedAt });
        builder.HasIndex(e => e.HomeworkId);
        // A homework submission is assessed once (a retry reuses the same row).
        builder.HasIndex(e => e.HomeworkSubmissionId).IsUnique().HasFilter("\"HomeworkSubmissionId\" IS NOT NULL");
    }
}

public class AiUsageConfiguration : IEntityTypeConfiguration<AiUsage>
{
    public void Configure(EntityTypeBuilder<AiUsage> builder)
    {
        builder.ToTable("ai_usages");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Provider).HasMaxLength(60).IsRequired();
        builder.Property(u => u.Model).HasMaxLength(120).IsRequired();
        builder.Property(u => u.ErrorCode).HasMaxLength(60);
        builder.Property(u => u.CostUsd).HasPrecision(18, 6);
        builder.HasIndex(u => u.RequestId);
        builder.HasIndex(u => new { u.OrganizationId, u.CreatedAt });
    }
}
