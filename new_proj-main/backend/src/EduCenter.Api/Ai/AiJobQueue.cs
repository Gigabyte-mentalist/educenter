using EduCenter.Api.Ai.Knowledge;
using EduCenter.Api.Ai.Writing;
using Hangfire;

namespace EduCenter.Api.Ai;

/// <summary>Long AI work (a writing assessment takes tens of seconds on a real model) runs outside the HTTP request.</summary>
public interface IAiJobQueue
{
    Task EnqueueWritingEvaluationAsync(Guid evaluationId);

    /// <summary>Parsing, scanning, chunking and embedding a document takes far longer than an HTTP request.</summary>
    Task EnqueueKnowledgeIngestionAsync(Guid documentId);
}

/// <summary>Production/development: a Hangfire background job (docs/00_TZ.md §44).</summary>
public class HangfireAiJobQueue(IBackgroundJobClient jobs) : IAiJobQueue
{
    public Task EnqueueWritingEvaluationAsync(Guid evaluationId)
    {
        jobs.Enqueue<WritingEvaluationJob>(job => job.RunAsync(evaluationId, CancellationToken.None));
        return Task.CompletedTask;
    }

    public Task EnqueueKnowledgeIngestionAsync(Guid documentId)
    {
        jobs.Enqueue<KnowledgeIngestionJob>(job => job.RunAsync(documentId, CancellationToken.None));
        return Task.CompletedTask;
    }
}

/// <summary>When Hangfire is off (Automation__Enabled=false, the test host) the job runs immediately in its own scope,
/// so a caller sees the finished evaluation as soon as the request returns.</summary>
public class InlineAiJobQueue(IServiceScopeFactory scopes) : IAiJobQueue
{
    public async Task EnqueueWritingEvaluationAsync(Guid evaluationId)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WritingEvaluator>().RunAsync(evaluationId);
    }

    public async Task EnqueueKnowledgeIngestionAsync(Guid documentId)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KnowledgeIngestor>().IngestAsync(documentId);
    }
}

public class KnowledgeIngestionJob(KnowledgeIngestor ingestor)
{
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid documentId, CancellationToken cancellationToken) => ingestor.IngestAsync(documentId, cancellationToken);
}

public class WritingEvaluationJob(WritingEvaluator evaluator)
{
    /// <summary>No automatic retry: a failed assessment is visible to the student, who retries it (and spends allowance) deliberately.</summary>
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid evaluationId, CancellationToken cancellationToken) => evaluator.RunAsync(evaluationId, cancellationToken);
}
