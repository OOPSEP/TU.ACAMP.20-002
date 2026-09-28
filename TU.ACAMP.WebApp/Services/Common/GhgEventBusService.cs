using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TU.ACAMP.WebApp.Services.Common;

public record GhgEventIngestedNotification(
    int SubscriptionId,
    int TopicId,
    string TopicKey,
    string? EventKey,
    decimal Quantity,
    DateTimeOffset OccurredAt
);

public class GhgEventBusService
{
    private readonly ILogger<GhgEventBusService> _logger;

    public GhgEventBusService(ILogger<GhgEventBusService> logger)
    {
        _logger = logger;
    }

    public event Func<GhgEventIngestedNotification, Task>? OnEventIngested;

    public async Task PublishEventIngestedAsync(GhgEventIngestedNotification notification)
    {
        if (OnEventIngested == null) return;

        var handlers = OnEventIngested.GetInvocationList();
        foreach (var handler in handlers)
        {
            if (handler is Func<GhgEventIngestedNotification, Task> asyncCallback)
            {
                try
                {
                    await asyncCallback(notification);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error handling in-memory GHG event notification for Subscription {SubscriptionId}, Topic {TopicId}",
                        notification.SubscriptionId, notification.TopicId);
                }
            }
        }
    }
}
