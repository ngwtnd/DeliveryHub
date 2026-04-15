using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryHubWeb.Models
{
    public enum OrderStatus
    {
        Pending,          // Mới tạo
        SearchingShipper, // Đang tìm shipper
        Accepted,         // Đã có shipper nhận
        Preparing,        // Nhà hàng đang chuẩn bị
        Delivering,       // Đang giao
        Completed,        // Hoàn thành
        Cancelled,        // Đã hủy
        Failed            // Thất bại
    }

    public enum PaymentMethod { Cash, Wallet }

    public class Order
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string OrderCode { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        public string? ShipperId { get; set; }

        [ForeignKey("ShipperId")]
        public virtual ApplicationUser? Shipper { get; set; }

        [Required]
        public OrderStatus Status { get; set; } = OrderStatus.Pending;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalPrice { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal ShippingFee { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Required]
        public string PickupAddress { get; set; } = string.Empty;

        [Required]
        public string DeliveryAddress { get; set; } = string.Empty;

        public double PickupLatitude { get; set; }
        public double PickupLongitude { get; set; }
        public double DeliveryLatitude { get; set; }
        public double DeliveryLongitude { get; set; }

        public double Distance { get; set; } = 0.0; // Khoảng cách (km)

        public int? ServiceId { get; set; }
        [ForeignKey("ServiceId")]
        public virtual DeliveryService? DeliveryService { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ShipperIncome { get; set; } // Thu nhập của shipper cho đơn này

        public int? StoreId { get; set; }
        [ForeignKey("StoreId")]
        public virtual Store? Store { get; set; }

        public DateTime? AcceptedAt { get; set; }   // Shipper nhận đơn
        public DateTime? PickedUpAt { get; set; }   // Shipper lấy hàng
        public DateTime? CompletedAt { get; set; }  // Hoàn thành
        public DateTime? CancelledAt { get; set; }  // Hủy đơn

        public bool IsPinned { get; set; } = false; // Ghim đơn lên đầu
        public string? Note { get; set; }           // Ghi chú khách hàng

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        public int? VoucherId { get; set; }
        [ForeignKey("VoucherId")]
        public virtual Voucher? Voucher { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; } = 0.0m;

        public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }

    public class OrderItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public virtual Order? Order { get; set; }

        [Required]
        public int MenuItemId { get; set; }

        [ForeignKey("MenuItemId")]
        public virtual MenuItem? MenuItem { get; set; }

        [Required]
        public int Quantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; } // Giá tại thời điểm đặt
    }
}
