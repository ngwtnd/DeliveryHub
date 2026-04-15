using System.Collections.Generic;
using System;

namespace DeliveryHubWeb.Models
{
    public class ShipperDetailViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string ShipperCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public double TotalDistance { get; set; }
        public string CitizenId { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Vehicle { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        
        public double AvgRating { get; set; }
        public int TotalReviews { get; set; }
        public int TotalOrders { get; set; }
        public decimal TotalIncome { get; set; }
        
        public List<ShipperRecentOrderViewModel> RecentOrders { get; set; } = new();
    }

    public class ShipperRecentOrderViewModel
    {
        public int Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string DeliveryAddress { get; set; } = string.Empty;
        public decimal ShippingFee { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
    }
}
