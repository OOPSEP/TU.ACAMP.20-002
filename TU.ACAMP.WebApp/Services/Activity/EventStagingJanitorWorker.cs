using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Activity;

/// <summary>
/// Background janitor service for retrying failed event staging records, moving dead letters,
/// and cleaning up old promoted staging logs (> 90 days). Runs on PeriodicTimer every 60s.
/// </summary>
public class EventStagingJanitorWorker : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<EventStagingJanitorWorker> _logger;

    public EventStagingJanitorWorker(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<EventStagingJanitorWorker> logger)
    {
        #region hide [2026-08-24]
        _dbFactory = dbFactory;
        _logger = logger;
        #endregion
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        #region hide [2026-08-24]
        _logger.LogInformation("EventStagingJanitorWorker started.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessFailedStagingRecordsAsync(stoppingToken);
                await CleanupOldPromotedRecordsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in EventStagingJanitorWorker iteration.");
            }
        }

        _logger.LogInformation("EventStagingJanitorWorker stopping.");
        #endregion
    }

    private async Task ProcessFailedStagingRecordsAsync(CancellationToken ct)
    {
        #region hide [2026-08-24]
        using var db = await _dbFactory.CreateDbContextAsync(ct);

        var failedRows = await db.EventStagings
            .Where(s => s.Status == "Failed" && s.RetryCount < 5)
            .OrderBy(s => s.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var row in failedRows)
        {
            row.RetryCount++;
            if (row.RetryCount >= 5)
            {
                row.Status = "DeadLetter";
                _logger.LogError("Staging event ID {StagingId} marked as DeadLetter after 5 retries. Batch: {BatchId}", row.Id, row.BatchId);
            }
            else
            {
                _logger.LogWarning("Retrying staging event ID {StagingId} (Attempt {Count}/5)", row.Id, row.RetryCount);
            }
        }

        if (failedRows.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        #endregion
    }

    private async Task CleanupOldPromotedRecordsAsync(CancellationToken ct)
    {
        #region hide [2026-08-24]
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = AppTimeZone.UtcNow.AddDays(-90);

        var deletedCount = await db.EventStagings
            .Where(s => s.Status == "Promoted" && s.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deletedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} promoted staging records older than 90 days.", deletedCount);
        }
        #endregion
    }
}
