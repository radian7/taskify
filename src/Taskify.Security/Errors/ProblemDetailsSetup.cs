using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Taskify.Security.Errors;

/// <summary>
/// RFC 9457 Problem Details for every error response (research R7). Responses carry a <c>traceId</c> for
/// support and never carry stack traces, type names, SQL text or rejected input values (constitution Principle I).
/// </summary>
public static class ProblemDetailsSetup
{
    /// <summary>Registers Problem Details, the trace ID customization and the global exception handler.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTaskifyProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] = TraceId(context.HttpContext);
                // Defense in depth: even if a developer exception page were enabled, never expose the exception.
                context.ProblemDetails.Extensions.Remove("exception");
            });
        services.AddExceptionHandler<TaskifyExceptionHandler>();
        return services;
    }

    internal static string? TraceId(HttpContext? context) => Activity.Current?.TraceId.ToString() ?? context?.TraceIdentifier;
}

/// <summary>Writes Problem Details responses from middleware, where no endpoint result is available.</summary>
public static class ProblemResponses
{
    /// <summary>Writes a Problem Details response and ends the request.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">A short, generic summary. It never contains input values or internal details.</param>
    /// <param name="detail">Optional extra text, equally generic.</param>
    /// <param name="errors">Optional field errors (field name to messages).</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string title,
        string? detail = null,
        IDictionary<string, string[]>? errors = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        ProblemDetails problem = errors is null
            ? new ProblemDetails()
            : new HttpValidationProblemDetails(errors);
        problem.Status = statusCode;
        problem.Title = title;
        problem.Detail = detail;
        problem.Type = $"https://www.rfc-editor.org/rfc/rfc9110#name-{statusCode}";
        problem.Extensions["traceId"] = ProblemDetailsSetup.TraceId(context);

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problem, problem.GetType(), options: null, contentType: "application/problem+json");
    }
}

/// <summary>Endpoint results for the standard error responses, shared by every API (contracts/*-api.yaml).</summary>
public static class Problems
{
    /// <summary>The target does not exist (HTTP 404).</summary>
    /// <param name="what">What was not found, for example <c>Task</c>. Never a user-supplied value.</param>
    /// <returns>The result.</returns>
    public static IResult NotFound(string what) => Create(StatusCodes.Status404NotFound, "Not found", $"{what} was not found.");

    /// <summary>The acting user may not do this (HTTP 403).</summary>
    /// <param name="detail">A generic explanation.</param>
    /// <returns>The result.</returns>
    public static IResult Forbidden(string detail) => Create(StatusCodes.Status403Forbidden, "Forbidden", detail);

    /// <summary>The target is in a state that does not allow this (HTTP 409).</summary>
    /// <param name="detail">A generic explanation.</param>
    /// <returns>The result.</returns>
    public static IResult Conflict(string detail) => Create(StatusCodes.Status409Conflict, "Conflict", detail);

    /// <summary>The request names a user, project or other entity that does not exist (HTTP 422).</summary>
    /// <param name="detail">A generic explanation that does not echo the supplied value.</param>
    /// <returns>The result.</returns>
    public static IResult UnknownReference(string detail) => Create(StatusCodes.Status422UnprocessableEntity, "Unknown reference", detail);

    /// <summary>The input failed validation (HTTP 400).</summary>
    /// <param name="errors">Field name to messages. Messages never contain the rejected value.</param>
    /// <returns>The result.</returns>
    public static IResult Validation(IDictionary<string, string[]> errors)
    {
        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
        problem.Extensions["traceId"] = ProblemDetailsSetup.TraceId(null);
        return Results.Problem(problem);
    }

    private static IResult Create(int status, string title, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["traceId"] = ProblemDetailsSetup.TraceId(null);
        return Results.Problem(problem);
    }
}
