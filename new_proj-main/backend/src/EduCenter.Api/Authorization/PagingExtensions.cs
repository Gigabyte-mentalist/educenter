using EduCenter.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Authorization;

public static class PagingExtensions
{
    /// <summary>
    /// Counts and slices in the database, so a list screen loads one page rather than the whole table. The count runs first
    /// and an empty result skips the second query — the common case for a filtered search.
    /// </summary>
    public static async Task<Page<T>> ToPageAsync<T>(this IQueryable<T> query, PageRequest? paging, CancellationToken ct = default)
    {
        var page = paging ?? new PageRequest();
        var total = await query.CountAsync(ct);
        IReadOnlyList<T> items = total == 0 ? [] : await query.Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new Page<T>(items, total, page.Number, page.Take);
    }

    /// <summary>
    /// <c>WHERE selector IN (values)</c> as a chain of equality tests. <c>values.Contains(x.Status)</c> reads better but EF cannot
    /// translate it for an enum stored as text (the array parameter carries no value converter), and the failure only shows up as a 500
    /// at run time.
    /// </summary>
    public static IQueryable<T> WhereIn<T, TValue>(this IQueryable<T> query, System.Linq.Expressions.Expression<Func<T, TValue>> selector, IEnumerable<TValue> values)
    {
        var wanted = values.Distinct().ToList();
        if (wanted.Count == 0) return query.Where(_ => false);

        System.Linq.Expressions.Expression? body = null;
        foreach (var value in wanted)
        {
            var equal = System.Linq.Expressions.Expression.Equal(selector.Body, System.Linq.Expressions.Expression.Constant(value, typeof(TValue)));
            body = body is null ? equal : System.Linq.Expressions.Expression.OrElse(body, equal);
        }

        return query.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body!, selector.Parameters));
    }

    /// <summary>An <c>ILIKE '%term%'</c> pattern, with the wildcard characters in the term itself escaped.</summary>
    public static string ContainsPattern(string term) =>
        $"%{term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}
