// [2026-09-25][Antigravity] Background worker ตรวจสอบเกณฑ์การแจ้งเตือนค่า GHG ประจำปีแบบ Hybrid Mode (Scheduled + Event-Driven)
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TU.ACAMP.WebApp.Services.Common;

namespace TU.ACAMP.WebApp.Services.Dashboard;

public class GhgTargetAlertWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GhgEventBusService _eventBus;
    private readonly ILogger<GhgTargetAlertWorker> _logger;

    // คิวรวบรวม Subscription IDs ที่มี Event ใหม่เข้ามา (Debounce Queue)
    private readonly ConcurrentDictionary<int, DateTime> _pendingSubscriptionChecks = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromSeconds(20);
    private readonly TimeSpan _periodicInterval = TimeSpan.FromHours(6);

    public GhgTargetAlertWorker(
        IServiceScopeFactory scopeFactory,
        GhgEventBusService eventBus,
        ILogger<GhgTargetAlertWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GhgTargetAlertWorker started. Monitoring GHG target thresholds...");

        // ลงทะเบียนรับ Event จาก Ingestion Bus
        _eventBus.OnEventIngested += HandleEventIngestedAsync;

        using var periodicTimer = new PeriodicTimer(_periodicInterval);
        using var debounceCheckTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        var debounceTask = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested && await debounceCheckTimer.WaitForNextTickAsync(stoppingToken))
            {
                await ProcessPendingDebounceChecksAsync(stoppingToken);
            }
        }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("GhgTargetAlertWorker: Running scheduled periodic check across all subscriptions...");
                using var scope = _scopeFactory.CreateScope();
                var alertService = scope.ServiceProvider.GetRequiredService<GhgTargetAlertService>();
                var count = await alertService.CheckAndSendAlertsAsync(null, null, stoppingToken);
                _logger.LogInformation("GhgTargetAlertWorker: Scheduled check completed. Sent {Count} alerts.", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during GhgTargetAlertWorker periodic execution.");
            }
        }

        try
        {
            await debounceTask;
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        _eventBus.OnEventIngested -= HandleEventIngestedAsync;
        _logger.LogInformation("GhgTargetAlertWorker stopped.");
    }

    /// <summary>
    /// Event handler เมื่อมี Event กิจกรรมใหม่ถูกบันทึกเข้ามาในระบบ
    /// </summary>
    private Task HandleEventIngestedAsync(GhgEventIngestedNotification notification)
    {
        if (notification.SubscriptionId > 0)
        {
            _pendingSubscriptionChecks[notification.SubscriptionId] = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// ประมวลผลคิวที่มีการ Debounce ครบกำหนดเวลาแล้ว
    /// </summary>
    private async Task ProcessPendingDebounceChecksAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var dueSubscriptionIds = new List<int>();

        foreach (var (subId, lastQueued) in _pendingSubscriptionChecks)
        {
            if (now - lastQueued >= _debounceInterval)
            {
                dueSubscriptionIds.Add(subId);
            }
        }

        foreach (var subId in dueSubscriptionIds)
        {
            if (ct.IsCancellationRequested) break;

            _pendingSubscriptionChecks.TryRemove(subId, out _);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var alertService = scope.ServiceProvider.GetRequiredService<GhgTargetAlertService>();
                await alertService.CheckAndSendAlertsAsync(subId, null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing debounced GHG target alert check for Subscription {SubId}", subId);
            }
        }
    }
}
