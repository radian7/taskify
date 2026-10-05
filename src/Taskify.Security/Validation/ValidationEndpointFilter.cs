using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Taskify.Security.Audit;
using Taskify.Security.Errors;

namespace Taskify.Security.Validation;

/// <summary>
/// Runs the <see cref="IValidator{T}"/> for the request body before the endpoint handler (constitution
/// Principle II: every API validates input server-side against an explicit schema). Invalid input is rejected
/// whole with a 400 Problem Details response that lists field errors but never echoes the rejected values,
/// and the rejection is audited.
/// </summary>
/// <typeparam name="T">The request body type.</typeparam>
/// <param name="validator">The validator for <typeparamref name="T"/>.</param>
/// <param name="audit">Records rejections.</param>
public sealed class ValidationEndpointFilter<T>(IValidator<T> validator, IAuditLogger audit) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var body = context.Arguments.OfType<T>().FirstOrDefault();
        if (body is null)
        {
            audit.Rejected(AuditOutcome.Validation);
            return Problems.Validation(new Dictionary<string, string[]> { ["body"] = ["A request body is required."] });
        }

        var result = await validator.ValidateAsync(body, context.HttpContext.RequestAborted);
        if (!result.IsValid)
        {
            audit.Rejected(AuditOutcome.Validation);

            // Field names are returned in camelCase to match the JSON contract. Messages never contain values.
            var errors = result.Errors
                .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
            return Problems.Validation(errors);
        }

        return await next(context);
    }
}

/// <summary>Endpoint builder helper for <see cref="ValidationEndpointFilter{T}"/>.</summary>
public static class ValidationFilterExtensions
{
    /// <summary>Validates the request body of type <typeparamref name="T"/> before the handler runs.</summary>
    /// <typeparam name="T">The request body type.</typeparam>
    /// <param name="builder">The endpoint builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static RouteHandlerBuilder RequireValidation<T>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationEndpointFilter<T>>();
}
