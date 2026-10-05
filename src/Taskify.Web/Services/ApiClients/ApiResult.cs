namespace Taskify.Web.Services.ApiClients;

/// <summary>
/// A failed call to an API, in a form the UI can show. The text is always generic and safe to display as plain
/// text: it never contains stack traces, internal addresses or the rejected input (constitution Principle I).
/// </summary>
/// <param name="StatusCode">The HTTP status code, or 0 when the service could not be reached.</param>
/// <param name="Message">A short message for the user.</param>
/// <param name="FieldErrors">Field name to validation messages (empty unless the status is 400).</param>
public sealed record ApiError(int StatusCode, string Message, IReadOnlyDictionary<string, string[]> FieldErrors)
{
    /// <summary>The message shown when a rate limit is hit (spec FR-031).</summary>
    public const string TooManyRequestsMessage = "Too many requests, please wait a moment";

    /// <summary>Gets a value indicating whether the target does not exist (HTTP 404).</summary>
    public bool IsNotFound => StatusCode == 404;

    /// <summary>Gets a value indicating whether the request failed validation (HTTP 400).</summary>
    public bool IsValidation => StatusCode == 400;

    /// <summary>Creates an error with no field errors.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="message">The message for the user.</param>
    /// <returns>The error.</returns>
    public static ApiError Simple(int statusCode, string message) =>
        new(statusCode, message, new Dictionary<string, string[]>());
}

/// <summary>The result of a call to an API that returns no body.</summary>
public class ApiResult
{
    /// <summary>Initializes a new instance of the <see cref="ApiResult"/> class.</summary>
    /// <param name="error">The error, or <see langword="null"/> when the call succeeded.</param>
    protected ApiResult(ApiError? error) => Error = error;

    /// <summary>Gets the error, or <see langword="null"/> when the call succeeded.</summary>
    public ApiError? Error { get; }

    /// <summary>Gets a value indicating whether the call succeeded.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Creates a successful result.</summary>
    /// <returns>The result.</returns>
    public static ApiResult Success() => new(null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The result.</returns>
    public static ApiResult Failure(ApiError error) => new(error);

    /// <summary>Creates a successful result that carries a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The returned value.</param>
    /// <returns>The result.</returns>
    public static ApiResult<T> Success<T>(T value) => new(value, null);

    /// <summary>Creates a failed result for a call that was expected to return a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="error">The error.</param>
    /// <returns>The result.</returns>
    public static ApiResult<T> Failure<T>(ApiError error) => new(default, error);
}

/// <summary>The result of a call to an API that returns a value. Create it with <see cref="ApiResult.Success{T}"/> or <see cref="ApiResult.Failure{T}"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class ApiResult<T> : ApiResult
{
    internal ApiResult(T? value, ApiError? error)
        : base(error) => Value = value;

    /// <summary>Gets the value. Only meaningful when <see cref="ApiResult.IsSuccess"/> is <see langword="true"/>.</summary>
    public T? Value { get; }
}
