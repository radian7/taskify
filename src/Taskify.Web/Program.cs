using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.RateLimiting;
using Taskify.ServiceDefaults;
using Taskify.Web.Components;
using Taskify.Web.Services;

// Taskify web app: Blazor Server UI, the only resource reachable from outside the Aspire network (research R2).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();
// Antiforgery would add its own "X-Frame-Options: SAMEORIGIN", which contradicts our "DENY" (SecurityHeadersMiddleware).
builder.Services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);
builder.Services.AddOptions<ApiKeyOptions>().Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
builder.Services.AddTaskifyWebRateLimiter();
builder.AddTaskifyDataProtection();
builder.Services.AddTaskifyWebServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Tell browsers to use HTTPS only (HSTS), as required by the plan's TLS-everywhere rule.
    app.UseHsts();
}

// An address that matches no page gets the "not found" page with a way back (spec Edge Cases), not an empty 404.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseMiddleware<SelectedUserMiddleware>();
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapSessionEndpoints();
// Blazor adds its own frame-ancestors policy to interactive pages; make it agree with our "frame-ancestors 'none'".
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = "'none'");

app.Run();

/// <summary>Entry point marker so tests can reference the host.</summary>
public partial class Program;
