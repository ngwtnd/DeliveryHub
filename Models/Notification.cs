using System;
using System.ComponentModel.DataAnnotations;

namespace DeliveryHubWeb.Models
{
    public enum NotificationType
    {
        StoreRegistration = 0,      // Admin: Duyệt đăng ký chi nhánh
        PartnerRegistration = 1,    // Admin: Đăng ký cho đối tác
        MenuItemApproval = 2,       // Both: Duyệt món ăn (menu item)
        ShipperRegistration = 3,    // Admin: Duyệt đăng ký shipper
        CustomerReview = 4,         // Both: Đánh giá từ khách
        ShipperReview = 5,          // Both: Đánh giá từ shipper về chi nhánh
        VoucherExpiring = 14,       // Partner: Sắp hết hạn
        VoucherExpired = 15,        // Partner: Đã hết hạn
        VoucherLimitSoon = 16,      // Partner: Gần hết lượt dùng
        VoucherLimitReached = 17    // Partner: Đã hết lượt dùng
    }

    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        [Required]
        public NotificationType Type { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // URL to redirect when clicked
        public string? TargetUrl { get; set; }

        // Reference to related entity (StoreId, UserId, ReviewId etc)
        public string? RelatedId { get; set; }

        // Targeted user (NULL for Admin notifications, specific UserId for Partner)
        public string? RecipientId { get; set; }
    }
}
