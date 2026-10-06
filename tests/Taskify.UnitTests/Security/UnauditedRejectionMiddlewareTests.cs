using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taskify.Security.Audit;

namespace Taskify.UnitTests.Security;

/// <summary>
/// The audit step for refusals the framework answers itself (spec FR-022, T148): audited once, never twice, and never
/// for a successful response.
/// </summary>
public sealed class UnauditedRejectionMiddlewareTests
{
    private sealed class Lines : ILogger<AuditLogger>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class Host : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "tasks-api";

        public string ContentRootPath { get; set; } = "/";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<List<string>> RunAsync(int status, bool downstreamAudits)
    {
        var lines = new Lines();
        var accessor = new HttpContextAccessor();
        var audit = new AuditLogger(lines, accessor, new Host());
        var context = new DefaultHttpContext();
        accessor.HttpContext = context;

        var middleware = new UnauditedRejectionMiddleware(
            _ =>
            {
                context.Response.StatusCode = status;
                if (downstreamAudits)
                {
                    audit.Rejected(AuditOutcomes.FromStatusCode(status));
                }

                return Task.CompletedTask;
            },
            audit);

        await middleware.InvokeAsync(context);
        return lines.Messages;
    }

    [Fact]
    public async Task A_413_nobody_audited_is_audited_once_as_TooLarge()
    {
        var messages = await RunAsync(StatusCodes.Status413PayloadTooLarge, downstreamAudits: false);

        var line = Assert.Single(messages);
        Assert.Contains("RequestRejected", line);
        Assert.Contains("outcome=TooLarge", line);
    }

    [Theory]
    [InlineData(StatusCodes.Status405MethodNotAllowed)]
    [InlineData(StatusCodes.Status415UnsupportedMediaType)]
    public async Task A_405_or_415_nobody_audited_is_audited_once(int status)
    {
        Assert.Single(await RunAsync(status, downstreamAudits: false));
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    public async Task A_rejection_that_was_already_audited_is_not_audited_again(int status)
    {
        Assert.Single(await RunAsync(status, downstreamAudits: true));
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status201Created)]
    [InlineData(StatusCodes.Status204NoContent)]
    public async Task A_successful_response_is_not_audited(int status)
    {
        Assert.Empty(await RunAsync(status, downstreamAudits: false));
    }
}
