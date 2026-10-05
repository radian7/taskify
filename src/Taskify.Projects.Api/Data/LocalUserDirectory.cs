using Microsoft.EntityFrameworkCore;
using Taskify.Security.Users;

namespace Taskify.Projects.Api.Data;

/// <summary>
/// The user directory backed by this service's own database. The five users never change in phase 1, so the
/// list is read once and kept in memory (research R4).
/// </summary>
/// <param name="scopes">Creates the scope that owns the database context.</param>
public sealed class LocalUserDirectory(IServiceScopeFactory scopes) : IUserDirectory, IDisposable
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private IReadOnlyList<UserInfo>? cache;

    /// <inheritdoc />
    public void Dispose() => loadLock.Dispose();

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(cancellationToken)).Any(u => u.Id == userId);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (cache is not null)
        {
            return cache;
        }

        await loadLock.WaitAsync(cancellationToken);
        try
        {
            if (cache is null)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();

                // Product Manager first, then the engineers alphabetically: a stable order for the selection screen.
                // Sorted in memory by the enum's order: the database stores the role as text, and as text
                // "Engineer" would sort before "ProductManager".
                var rows = await db.Users.AsNoTracking()
                    .Select(u => new UserInfo(u.Id, u.DisplayName, u.Role))
                    .ToListAsync(cancellationToken);
                var users = rows.OrderBy(u => u.Role).ThenBy(u => u.DisplayName, StringComparer.CurrentCulture).ToList();

                // Only cache a complete result; an empty list means the database is not ready yet.
                if (users.Count > 0)
                {
                    cache = users;
                }

                return users;
            }

            return cache;
        }
        finally
        {
            loadLock.Release();
        }
    }
}
