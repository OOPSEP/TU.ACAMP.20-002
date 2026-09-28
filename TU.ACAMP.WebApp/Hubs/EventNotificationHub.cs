using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace TU.ACAMP.WebApp.Hubs;

public class EventNotificationHub : Hub
{
    #region hide [2026-08-24]
    public async Task JoinSubscriptionGroup(int subscriptionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"sub_{subscriptionId}");
    }

    public async Task LeaveSubscriptionGroup(int subscriptionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"sub_{subscriptionId}");
    }
    #endregion
}
