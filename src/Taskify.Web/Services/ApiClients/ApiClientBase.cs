using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.Security;

namespace Taskify.Web.Services.ApiClients;

/// <summary>
/// Shared plumbing for the typed clients that call the three APIs. It adds the per-request headers
/// (<c>X-Taskify-User</c>, <c>X-Taskify-Client-Ip</c>) from the circuit's identity, and turns Problem Details
/// responses into <see cref="ApiError"/> values.
/// </summary>
/// <remarks>
/// The headers are added here, not in a <see cref="DelegatingHandler"/>, because handlers live in the HTTP client
/// factory's own scope, which cannot see the per-circuit state. The API key is a default header of the HTTP client
/// (see <c>AddTaskifyHttpClient</c>).
/// </remarks>
/// <param name="http">The HTTP client for one API.</param>
/// <param name="identity">The identity of the circuit making the call.</param>
public abstract class ApiClientBase(HttpClient http, CircuitIdentity identity)
{
    private const int MaxMessageLength = 300;

    /// <summary>
    /// Settings for reading responses. Request bodies use the strict <see cref="ContractJson"/> settings, but a
    /// response that gains a field must not break the UI, so unknown fields are skipped when reading.
    /// </summary>
    protected static readonly JsonSerializerOptions ResponseJson = CreateResponseOptions();

    /// <summary>Sends a GET request and reads the JSON response.</summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="path">The route, for example <c>/api/projects</c>.</param>
    /// <param name="actingUser"><see langword="false"/> for the routes that need no acting user.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The value, or the error.</returns>
    protected Task<ApiResult<T>> GetAsync<T>(string path, bool actingUser = true, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, path, body: null, actingUser, cancellationToken);

    /// <summary>Sends a request with an optional JSON body and reads the JSON response.</summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route.</param>
    /// <param name="body">The request body, or <see langword="null"/>.</param>
    /// <param name="actingUser"><see langword="false"/> for the routes that need no acting user.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The value, or the error.</returns>
    protected async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method, string path, object? body, bool actingUser = true, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await BuildAsync(method, path, body, actingUser);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ApiResult.Failure<T>(await ReadErrorAsync(response, cancellationToken));
            }

            var value = await response.Content.ReadFromJsonAsync<T>(ResponseJson, cancellationToken);
            return value is null
                ? ApiResult.Failure<T>(ApiError.Simple(0, "The service returned an unexpected answer."))
                : ApiResult.Success<T>(value);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            return ApiResult.Failure<T>(Unreachable());
        }
    }

    /// <summary>Sends a request whose successful response has no body (for example <c>204</c>).</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route.</param>
    /// <param name="body">The request body, or <see langword="null"/>.</param>
    /// <param name="actingUser"><see langword="false"/> for the routes that need no acting user.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the error.</returns>
    protected async Task<ApiResult> SendAsync(
        HttpMethod method, string path, object? body = null, bool actingUser = true, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await BuildAsync(method, path, body, actingUser);
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? ApiResult.Success()
                : ApiResult.Failure(await ReadErrorAsync(response, cancellationToken));
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            return ApiResult.Failure(Unreachable());
        }
    }

    private static JsonSerializerOptions CreateResponseOptions()
    {
        var options = ContractJson.Create();
        options.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip;
        return options;
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested;

    private static ApiError Unreachable() =>
        ApiError.Simple(0, "A required service is temporarily unavailable. Try again in a moment.");

    private async Task<HttpRequestMessage> BuildAsync(HttpMethod method, string path, object? body, bool actingUser)
    {
        var snapshot = await identity.GetAsync();
        var request = new HttpRequestMessage(method, path);

        if (actingUser)
        {
            // Never call an acting-user route without a selected user; the UI redirects to the selection screen first.
            var userId = snapshot.UserId ?? throw new InvalidOperationException("No user is selected.");
            request.Headers.Add(TaskifyHeaders.ActingUser, userId.ToString("D"));
        }

        if (snapshot.ClientIp is { Length: > 0 } ip)
        {
            request.Headers.Add(TaskifyHeaders.ClientIp, ip);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: ContractJson.Options);
        }

        return request;
    }

    private static async Task<ApiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;

        // Generic messages for everything except validation, where the field messages are the point.
        switch (response.StatusCode)
        {
            case HttpStatusCode.TooManyRequests:
                return ApiError.Simple(status, ApiError.TooManyRequestsMessage);
            case HttpStatusCode.NotFound:
                return ApiError.Simple(status, "That item was not found.");
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.Unauthorized:
                return ApiError.Simple(status, "That action is not allowed.");
            case HttpStatusCode.ServiceUnavailable:
                return ApiError.Simple(status, "A required service is temporarily unavailable. Try again in a moment.");
            case HttpStatusCode.RequestEntityTooLarge:
                return ApiError.Simple(status, "That request is too large.");
        }

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var root = document.RootElement;

                var fields = new Dictionary<string, string[]>();
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in errors.EnumerateObject().Take(20))
                    {
                        fields[property.Name] = property.Value.ValueKind == JsonValueKind.Array
                            ? property.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => Truncate(e.GetString()!)).Take(5).ToArray()
                            : [];
                    }
                }

                var title = root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
                    ? detail.GetString()!
                    : root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                        ? titleElement.GetString()!
                        : "The request was not accepted.";
                return new ApiError(status, Truncate(title), fields);
            }
            catch (JsonException)
            {
                return ApiError.Simple(status, "The request was not accepted.");
            }
        }

        return ApiError.Simple(status, "Something went wrong. Please try again.");
    }

    private static string Truncate(string text) => text.Length <= MaxMessageLength ? text : text[..MaxMessageLength];
}
