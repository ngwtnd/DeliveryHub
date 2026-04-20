using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using DeliveryHubWeb.Data;
using DeliveryHubWeb.Models;

namespace DeliveryHubWeb.Services
{
    public class PlatformBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;

        public PlatformBackgroundService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessBackgroundTasks();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PlatformBackgroundService Error] {ex.Message}");
                }

                // Chạy mỗi 1 phút
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task ProcessBackgroundTasks()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.Now;

            // 1. Tự động hủy đơn "Tìm Shipper" nếu treo quá 1 tiếng
            var stuckOrders = await context.Orders
                .Where(o => o.Status == OrderStatus.SearchingShipper && o.CreatedAt <= now.AddHours(-1))
                .ToListAsync();

            foreach (var order in stuckOrders)
            {
                order.Status = OrderStatus.Cancelled;
                order.CancelledAt = now;
            }

            // 2. Trao Voucher Hàng Tháng (Dựa trên Hạng Thành viên)
            // Chỉ kiểm tra những User có hạng từ Bạc trở lên và chưa nhận Voucher trong tháng này
            int currentMonth = now.Month;
            var eligibleUsers = await context.Users
                .Where(u => u.Role == UserRole.User 
                            && (u.UserTier == "Bạc" || u.UserTier == "Vàng" || u.UserTier == "Kim Cương")
                            && u.LastVoucherMonth != currentMonth)
                .ToListAsync();

            foreach (var user in eligibleUsers)
            {
                // Reset số đơn hủy hàng tháng mỗi đầu tháng (khi kiểm tra trao voucher)
                user.MonthlyFailedOrdersCount = 0;
                user.LastVoucherMonth = currentMonth;

                // Tạo mới Voucher cá nhân
                decimal discount = 0;
                if (user.UserTier == "Bạc") discount = 0.03m; // 3%
                else if (user.UserTier == "Vàng") discount = 0.05m; // 5%
                else if (user.UserTier == "Kim Cương") discount = 0.10m; // 10%

                if (discount > 0)
                {
                    var voucher = new Voucher
                    {
                        OwnerId = user.Id,
                        Code = $"TIER-{user.UserTier.ToUpper().Replace(" ", "")}-{now:MMyy}-{Guid.NewGuid().ToString().Substring(0, 4)}",
                        ProgramName = $"Voucher Hội Viên {user.UserTier}",
                        Description = $"Giảm {discount * 100}% tổng hóa đơn, hạng {user.UserTier}",
                        LimitPerUser = 1,
                        AppliesToShipping = false,
                        IsPercentage = true,
                        DiscountValue = discount * 100,
                        MaxDiscountValue = null,
                        MinOrderValue = 0,
                        UsedCount = 0,
                        MaxUsageCount = 1,
                        StartDate = now,
                        EndDate = now.AddMonths(1),
                        IsActive = true
                    };

                    context.Vouchers.Add(voucher);
                }
            }

            if (stuckOrders.Any() || eligibleUsers.Any())
            {
                await context.SaveChangesAsync();
                Console.WriteLine($"[Platform Tracker] Cancelled {stuckOrders.Count} orders. Granted {eligibleUsers.Count} monthly vouchers.");
            }
        }
    }
}
