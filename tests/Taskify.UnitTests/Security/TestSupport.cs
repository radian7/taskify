using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Security.Audit;
using Taskify.Security.Users;

namespace Taskify.UnitTests.Security;

/// <summary>An <see cref="IAuditLogger"/> that keeps entries in memory so tests can assert on them.</summary>
public sealed class RecordingAuditLogger : IAuditLogger
{
    /// <summary>Gets the entries written so far.</summary>
    public List<AuditEntry> Entries { get; } = [];

    /// <inheritdoc />
    public void Log(AuditEntry entry) => Entries.Add(entry);
}

/// <summary>A fixed user directory.</summary>
/// <param name="ids">The IDs that exist.</param>
public sealed class FakeUserDirectory(params Guid[] ids) : IUserDirectory
{
    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ids.Contains(userId));

    /// <inheritdoc />
    public Task<IReadOnlyList<UserInfo>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserInfo>>(ids.Select(id => new UserInfo(id, "User", Taskify.Contracts.UserRole.Engineer)).ToList());
}

/// <summary>Helpers for building request contexts for middleware tests.</summary>
public static class HttpContextFactory
{
    /// <summary>Creates a context that can write JSON Problem Details responses into memory.</summary>
    /// <param name="endpointMetadata">Metadata for the matched endpoint, or <see langword="null"/> for "no endpoint matched".</param>
    /// <returns>The context.</returns>
    public static DefaultHttpContext Create(params object[]? endpointMetadata)
    {
        var services = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        if (endpointMetadata is not null)
        {
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(endpointMetadata), "test"));
        }

        return context;
    }

    /// <summary>Reads what the middleware wrote to the response body.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The body text.</returns>
    public static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }
}
