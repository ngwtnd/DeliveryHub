using DeliveryHubWeb.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DeliveryHubWeb.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Store> Stores { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<DeliveryService> DeliveryServices { get; set; }
        public DbSet<Voucher> Vouchers { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<BatchOrder> BatchOrders { get; set; }
        public DbSet<BatchOrderItem> BatchOrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Cấu hình quan hệ Order - User (Customer)
            builder.Entity<Order>()
                .HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình quan hệ Order - Shipper
            builder.Entity<Order>()
                .HasOne(o => o.Shipper)
                .WithMany()
                .HasForeignKey(o => o.ShipperId)
                .OnDelete(DeleteBehavior.Restrict);

            // Đảm bảo OrderCode là duy nhất
            builder.Entity<Order>()
                .HasIndex(o => o.OrderCode)
                .IsUnique();

            // Store - Owner mapping
            builder.Entity<Store>()
                .HasOne(s => s.Owner)
                .WithMany()
                .HasForeignKey(s => s.OwnerId)
                .OnDelete(DeleteBehavior.NoAction);

            // Review - Shipper (direct FK)
            builder.Entity<Review>()
                .HasOne(r => r.Shipper)
                .WithMany()
                .HasForeignKey(r => r.ShipperId)
                .OnDelete(DeleteBehavior.NoAction);

            // Review - Store (direct FK)
            builder.Entity<Review>()
                .HasOne(r => r.ReviewedStore)
                .WithMany()
                .HasForeignKey(r => r.StoreId)
                .OnDelete(DeleteBehavior.NoAction);

            // BatchOrder - Shipper
            builder.Entity<BatchOrder>()
                .HasOne(b => b.Shipper)
                .WithMany()
                .HasForeignKey(b => b.ShipperId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<BatchOrder>()
                .HasIndex(b => b.BatchCode)
                .IsUnique();

            // BatchOrderItem - BatchOrder
            builder.Entity<BatchOrderItem>()
                .HasOne(bi => bi.BatchOrder)
                .WithMany(b => b.Items)
                .HasForeignKey(bi => bi.BatchOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // BatchOrderItem - Order
            builder.Entity<BatchOrderItem>()
                .HasOne(bi => bi.Order)
                .WithMany()
                .HasForeignKey(bi => bi.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
