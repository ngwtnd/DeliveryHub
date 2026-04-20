namespace DeliveryHubWeb.Models
{
    public class ShipperViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string ShipperCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Vehicle { get; set; } = string.Empty;
        public double Rating { get; set; } = 5.0;
        public int DeliveredOrders { get; set; } = 0;
        public decimal Income { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public bool IsDelivering { get; set; } = false;
        public int? ActiveOrderId { get; set; }
        public string? ActiveOrderCode { get; set; }
        public bool IsLocked { get; set; } = false;
        public bool PendingLock { get; set; } = false;
    }
}
