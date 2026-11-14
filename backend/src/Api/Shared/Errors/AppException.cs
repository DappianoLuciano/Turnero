namespace Api.Shared.Errors;

/// <summary>
/// Expected application error. Mapped to an RFC 9457 Problem Details response by
/// <see cref="GlobalExceptionHandler"/>. <see cref="Code"/> is a stable, machine-readable
/// identifier the frontend can use to pick a localized message.
/// </summary>
public class AppException(int statusCode, string code, string title, string? detail = null)
    : Exception(detail ?? title)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;

    public static AppException NotFound(string code, string title) =>
        new(StatusCodes.Status404NotFound, code, title);

    public static AppException Conflict(string code, string title) =>
        new(StatusCodes.Status409Conflict, code, title);

    public static AppException Unauthorized(string code, string title) =>
        new(StatusCodes.Status401Unauthorized, code, title);

    public static AppException Forbidden(string code, string title) =>
        new(StatusCodes.Status403Forbidden, code, title);
}
