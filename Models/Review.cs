using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryHubWeb.Models
{
    public enum ReviewType { Customer, Shipper }

    public class Review
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public virtual Order? Order { get; set; }

        // FK trực tiếp đến Shipper (query dễ hơn qua Order)
        public string? ShipperId { get; set; }

        [ForeignKey("ShipperId")]
        public virtual ApplicationUser? Shipper { get; set; }

        // FK trực tiếp đến Store
        public int? StoreId { get; set; }

        [ForeignKey("StoreId")]
        public virtual Store? ReviewedStore { get; set; }

        [Required, Range(1, 5)]
        public int RatingMenu { get; set; }

        [Required, Range(1, 5)]
        public int RatingShipper { get; set; }

        public string? Comment { get; set; }
        public string? CommentForShipper { get; set; }
        public ReviewType Type { get; set; } = ReviewType.Customer;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public virtual Order? Order { get; set; }

        [Required]
        public string SenderId { get; set; } = string.Empty;

        [Required]
        public string ReceiverId { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
