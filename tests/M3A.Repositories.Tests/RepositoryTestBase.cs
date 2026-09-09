using M3A.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;

namespace M3A.Repositories.Tests;

/// <summary>
/// Base class giving each test its own in-memory database, so tests stay isolated
/// and parallelizable with no ordering dependency.
/// </summary>
public abstract class RepositoryTestBase : IDisposable
{
    private bool _disposed;

    /// <summary>Context bound to a database unique to this test instance.</summary>
    protected M3ADbContext DbContext { get; } = new(
        new DbContextOptionsBuilder<M3ADbContext>()
            .UseInMemoryDatabase($"m3a-{Guid.NewGuid():N}")
            .Options);

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the per-test database.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            DbContext.Dispose();
        }

        _disposed = true;
    }
}
