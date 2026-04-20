using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryHubWeb.Models
{
    public enum UserRole { Admin, Partner, Shipper, User, RestaurantManager }

    public class ApplicationUser : IdentityUser
    {
        [Required, MaxLength(100)]
        public string? FullName { get; set; }

        public string? Address { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        [Required]
        public UserRole Role { get; set; }

        public string? AvatarUrl { get; set; }

        // Ví tiền giả lập
        [Column(TypeName = "decimal(18,2)")]
        public decimal Balance { get; set; } = 0;

        // Trạng thái cho Shipper
        public bool IsActive { get; set; } = false;
        public bool IsDelivering { get; set; } = false;
        public string? Vehicle { get; set; } // Thông tin xe (Dành cho Shipper)
        public string? CitizenId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(7);

        // Approval state (separate from Active/Offline)
        public bool IsApproved { get; set; } = false;

        // --- NEW FIELDS FOR REGISTRATION ---
        // For Shipper
        public string? CitizenIdFrontImageUrl { get; set; }
        public string? CitizenIdBackImageUrl { get; set; }
        public string? DriverLicenseImageUrl { get; set; }
        public string? VehicleType { get; set; } // Loại xe (chỉ có trong dịch vụ của Admin)
        public bool HasOneStarReview { get; set; } = false;
        public string? ShipperCode { get; set; }

        // For Partner
        public string? ShopAvatarUrl { get; set; }
        public string? PartnerCode { get; set; }

        // For RestaurantManager
        public int? ManagedStoreId { get; set; }

        // --- NEW FIELDS FOR CUSTOMER TIERS & LOGIC ---
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalSpent { get; set; } = 0m;
        public int FailedOrdersCount { get; set; } = 0;
        public int MonthlyFailedOrdersCount { get; set; } = 0;
        public string UserTier { get; set; } = "Đồng"; // Đồng, Bạc, Vàng, Kim Cương
        public int LastVoucherMonth { get; set; } = 0;

        // --- NEW FIELDS FOR LOCKING LOGIC ---
        public bool IsLocked { get; set; } = false;
        public bool PendingLock { get; set; } = false;
    }
}
