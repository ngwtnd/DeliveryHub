using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryHubWeb.Models
{
    public class DeliveryService
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        [StringLength(50)]
        public string VehicleType { get; set; } = "Tất cả";

        [StringLength(50)]
        public string ServiceType { get; set; } = "Giao hàng";

        [Required]
        [StringLength(50)]
        public string Icon { get; set; } = "fa-box"; // Default icon, hardcoded logic in controller

        public bool IsActive { get; set; } = true;

        public bool IsPinned { get; set; } = false;

        public int MaxWeightKg { get; set; }

        public int EstimatedMinutesPerKm { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BaseFee { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FeePerKm { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ExtraFeePerKg { get; set; }
    }
}
