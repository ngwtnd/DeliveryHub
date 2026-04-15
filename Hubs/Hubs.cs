using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace DeliveryHubWeb.Hubs
{
    public class OrderHub : Hub
    {
        // Khi shipper nhận đơn, báo cho User
        public async Task NotifyOrderAccepted(string userId, string orderCode)
        {
            await Clients.User(userId).SendAsync("OrderAccepted", orderCode);
        }

        // Báo cho các shipper trong khu vực có đơn mới
        public async Task BroadcastNewOrder(string orderId, double lat, double lon)
        {
            await Clients.All.SendAsync("NewOrderAvailable", orderId, lat, lon);
        }

        // Cập nhật vị trí Shipper realtime
        public async Task UpdateShipperLocation(string orderId, double lat, double lon)
        {
            await Clients.Group(orderId).SendAsync("LocationUpdated", lat, lon);
        }

        public async Task JoinOrderGroup(string orderId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, orderId);
        }
    }

    public class ChatHub : Hub
    {
        public async Task SendMessage(string orderId, string senderId, string message)
        {
            await Clients.Group(orderId).SendAsync("ReceiveMessage", senderId, message);
        }

        public async Task JoinChat(string orderId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, orderId);
        }
    }
}
