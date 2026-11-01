using System.Linq;
using ArcanoCore.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArcanoCore.Infrastructure.Jobs;

// Sweeps abandoned upload sessions and expired trash for all users, so nothing depends on the owner returning.
public static class CleanupJobExtensions
{
    public static IServiceCollection AddCleanupJob(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue("Cleanup:Enabled", true))
        {
            services.AddHostedService<CleanupService>();
        }

        return services;
    }
}

public class CleanupService : BackgroundService
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(30);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CleanupService> _logger;
    private readonly TimeSpan _interval;

    public CleanupService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<CleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Cleanup:IntervalMinutes", 60)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            using var timer = new PeriodicTimer(_interval);
            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sp = scope.ServiceProvider;
                    await RunOnceAsync(
                        sp.GetRequiredService<IApplicationDbContext>(),
                        sp.GetRequiredService<IStorageService>(),
                        sp.GetRequiredService<IMultipartStorage>(),
                        _logger,
                        stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Cleanup pass failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
    }

    public static async Task RunOnceAsync(
        IApplicationDbContext db,
        IStorageService storage,
        IMultipartStorage multipart,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sessionCutoff = now - SessionLifetime;
        var trashCutoff = now - TrashRetention;

        // Sessions whose abort failed stay in the table and are retried on every pass (shortcut: no attempt cap, so a permanently failing abort is retried hourly and logged; add a counter column if that gets noisy).
        var failed = new List<Guid>();
        while (true)
        {
            var stale = await db.UploadSessions
                .Where(s => s.CreatedAtUtc < sessionCutoff && !failed.Contains(s.Id))
                .OrderBy(s => s.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (stale.Count == 0) break;

            foreach (var session in stale)
            {
                try
                {
                    await multipart.AbortAsync(session.StorageKey, session.StorageUploadId, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Abort of stale upload {SessionId} failed; keeping the row to retry.", session.Id);
                    failed.Add(session.Id);
                    continue;
                }

                db.UploadSessions.Remove(session);
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Cleanup removed {Count} stale upload sessions.", stale.Count - stale.Count(s => failed.Contains(s.Id)));
        }

        while (true)
        {
            var expired = await db.FileMetadatas
                .IgnoreQueryFilters()
                .Where(f => f.IsDeleted && (f.DeletedAtUtc ?? f.LastModifiedAtUtc ?? f.CreatedAtUtc) < trashCutoff)
                .OrderBy(f => f.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (expired.Count == 0) break;

            foreach (var file in expired)
            {
                db.FileMetadatas.Remove(file);
            }

            // Rows first: a failed save throws (ends the pass) and no object is deleted for a row that still exists.
            await db.SaveChangesAsync(cancellationToken);

            foreach (var file in expired)
            {
                try
                {
                    await storage.DeleteAsync(file.StorageKey, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Deleting object {Key} of purged file {FileId} failed; object is orphaned.", file.StorageKey, file.Id);
                }
            }

            logger.LogInformation("Cleanup purged {Count} expired trashed files.", expired.Count);
        }

        // Leaf folders only (no file or child row left, deleted ones included), so no FK is violated; the loop
        // then reaches parents once their children are gone. LastModifiedAtUtc is the delete time (set on soft delete).
        while (true)
        {
            var emptyFolders = await db.Folders
                .IgnoreQueryFilters()
                .Where(f => f.IsDeleted && (f.LastModifiedAtUtc ?? f.CreatedAtUtc) < trashCutoff
                    && !db.FileMetadatas.IgnoreQueryFilters().Any(x => x.FolderId == f.Id)
                    && !db.Folders.IgnoreQueryFilters().Any(c => c.ParentId == f.Id))
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (emptyFolders.Count == 0) break;

            db.Folders.RemoveRange(emptyFolders);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Cleanup purged {Count} expired deleted folders.", emptyFolders.Count);
        }
    }
}
