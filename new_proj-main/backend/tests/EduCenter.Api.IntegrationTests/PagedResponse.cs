namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The envelope every paged list endpoint returns. Tests only ever want the rows, so <see cref="Items"/> is
/// what they read; the rest is here so deserialization does not have to ignore it.
/// </summary>
public record PagedResponse<T>(List<T> Items, int Total, int PageNumber, int PageSize, int PageCount);
