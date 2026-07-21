namespace Subtitles.Api.Contracts;

/// <summary>The standard error envelope from docs/API.md "Conventions".</summary>
public sealed record ApiErrorDetail(string Code, string Message);

public sealed record ApiError(ApiErrorDetail Error)
{
    public static ApiError Of(string code, string message) => new(new ApiErrorDetail(code, message));
}
