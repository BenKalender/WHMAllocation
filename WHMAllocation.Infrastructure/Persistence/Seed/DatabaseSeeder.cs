using Microsoft.EntityFrameworkCore;

namespace WHMAllocation.Infrastructure.Persistence.Seed;

/// <summary>
/// Persists the demo dataset from <see cref="SeedData"/>.
/// </summary>
/// <remarks>
/// Registered in DI so a UI component can offer a "reset demo data" action: allocation,
/// cancellation and correction all mutate stock, so the demo needs a way back to a known state.
/// </remarks>
public class DatabaseSeeder
{
    private readonly AppDbContext _dbContext;

    public DatabaseSeeder(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Seeds only when the database is empty, so restarting the app never duplicates or
    /// silently reverts work in progress.
    /// </summary>
    /// <returns><c>true</c> when data was written, <c>false</c> when the database already had orders.</returns>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _dbContext.Orders.AnyAsync(cancellationToken))
            return false;

        await InsertAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Clears every table and re-seeds, returning the demo to its documented starting point.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        // Deleted child-first: Allocations and OrderLines cascade, but SKUs are held by
        // RESTRICT foreign keys and cannot go until the allocations referencing them do.
        await _dbContext.Allocations.ExecuteDeleteAsync(cancellationToken);
        await _dbContext.OrderLines.ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Orders.ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Skus.ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Products.ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Locations.ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete bypasses the change tracker, which may still hold entities that no
        // longer exist. Clearing it prevents the insert below from resurrecting them.
        _dbContext.ChangeTracker.Clear();

        await InsertAsync(cancellationToken);
    }

    private async Task InsertAsync(CancellationToken cancellationToken)
    {
        var graph = SeedData.Build();

        await _dbContext.Locations.AddRangeAsync(graph.Locations, cancellationToken);
        await _dbContext.Products.AddRangeAsync(graph.Products, cancellationToken);
        await _dbContext.Skus.AddRangeAsync(graph.Skus, cancellationToken);

        // Order lines travel with their order through the navigation collection.
        await _dbContext.Orders.AddRangeAsync(graph.Orders, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
