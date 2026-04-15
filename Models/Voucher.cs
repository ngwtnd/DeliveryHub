using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryHubWeb.Models
{
    public class Voucher
    {
        [Key]
        public int Id { get; set; }

        public string? OwnerId { get; set; } // Null if Admin, UserId if Partner

        [Required]
        [StringLength(50)]
        public string Code { get; set; }

        [Required]
        [StringLength(200)]
        public string ProgramName { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        public int LimitPerUser { get; set; } = 1;

        // Logic: true = Applies to Shipping Fee (Freeship/Giảm phí ship), false = Applies to Order Total
        public bool AppliesToShipping { get; set; }

        // Logic: true = %, false = fixed amount (VND)
        public bool IsPercentage { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountValue { get; set; }

        // Only explicitly used for percentage (e.g. 10% Tối đa 80k)
        [Column(TypeName = "decimal(18,2)")]
        public decimal? MaxDiscountValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MinOrderValue { get; set; }

        public int UsedCount { get; set; }

        public int MaxUsageCount { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;
        
        [NotMapped]
        public bool IsValid => IsActive && DateTime.Now >= StartDate && DateTime.Now <= EndDate && UsedCount < MaxUsageCount;
    }
}
