using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryHubWeb.Models
{
    public enum StoreActivityState { Active, Inactive, LockedByBranch, LockedByPartner, PendingApproval }

    public class Store
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? Address { get; set; }

        public string? ImageUrl { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        [ForeignKey("OwnerId")]
        public ApplicationUser? Owner { get; set; }

        public virtual ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsMainBranch { get; set; } = false;

        public bool IsOpen { get; set; } = true;
        // Bỏ IsLocked nếu ko dùng hoặc map lại, ở đây em thêm ActivityState
        public StoreActivityState ActivityState { get; set; } = StoreActivityState.Active;
        public double Rating { get; set; } = 5.0;
        public int ReviewCount { get; set; } = 0;
        public bool HasOneStarReview { get; set; } = false;
        public string CustomCategories { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string StoreCategory { get; set; } = "Món ăn";
    }

    public class MenuItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StoreId { get; set; }

        [ForeignKey("StoreId")]
        public Store? Store { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public string? ImageUrl { get; set; }

        [Required, MaxLength(50)]
        public string Category { get; set; } = "Chung";

        public bool IsAvailable { get; set; } = true;
    }
}
