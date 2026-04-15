using System.Collections.Generic;
using System;

namespace DeliveryHubWeb.Models
{
    public class UserDetailViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Rank { get; set; } = "Đồng";
        
        public int TotalOrders { get; set; }
        public int TotalCompletedOrders { get; set; }
        public int TotalFailedOrders { get; set; }
        public int TotalDeliveringOrders { get; set; }
        
        public int TotalPendingOrders { get; set; }
        public int TotalSearchingShipperOrders { get; set; }
        public int TotalCancelledOrders { get; set; }
        
        public decimal TotalSpent { get; set; }
        
        public List<UserRecentOrderViewModel> RecentOrders { get; set; } = new();
    }

    public class UserRecentOrderViewModel
    {
        public int Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string DeliveryAddress { get; set; } = string.Empty;
        public decimal TotalPrice { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
    }
}
