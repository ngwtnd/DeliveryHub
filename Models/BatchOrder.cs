using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryHubWeb.Models
{
    public enum BatchOrderStatus
    {
        Created,      // Vừa tạo, chưa bắt đầu
        InProgress,   // Đang giao (shipper đang đi lấy hàng)
        Completed,    // Hoàn thành tất cả
        Cancelled     // Đã hủy
    }

    public class BatchOrder
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string BatchCode { get; set; } = string.Empty;

        public string? ShipperId { get; set; }

        [ForeignKey("ShipperId")]
        public virtual ApplicationUser? Shipper { get; set; }

        // Optional User ID to track who created the multi-store batch order
        public string? UserId { get; set; }
        
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        public BatchOrderStatus Status { get; set; } = BatchOrderStatus.Created;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }

        // Tổng quãng đường tối ưu (km)
        public double TotalDistance { get; set; }

        // Thời gian ước tính (phút)
        public double EstimatedMinutes { get; set; }

        // Điểm giao cuối cùng
        [Required]
        public string DeliveryAddress { get; set; } = string.Empty;
        public double DeliveryLatitude { get; set; }
        public double DeliveryLongitude { get; set; }

        // GeoJSON geometry string (polyline đường đi thực tế)
        public string? RouteGeometry { get; set; }

        // JSON array thứ tự tối ưu (backup)
        public string? OptimizedRouteJson { get; set; }

        public virtual ICollection<BatchOrderItem> Items { get; set; } = new List<BatchOrderItem>();
    }

    public class BatchOrderItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BatchOrderId { get; set; }

        [ForeignKey("BatchOrderId")]
        public virtual BatchOrder? BatchOrder { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public virtual Order? Order { get; set; }

        // Thứ tự lấy hàng tối ưu (1 = đầu tiên)
        public int Sequence { get; set; }

        // Trạng thái lấy hàng của đơn này trong batch
        public bool IsPickedUp { get; set; } = false;
        public DateTime? PickedUpAt { get; set; }
    }
}
