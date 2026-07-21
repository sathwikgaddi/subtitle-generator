namespace Subtitles.Api.Contracts;

/// <summary>List-endpoint envelope from docs/API.md "Conventions".</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
