using Microsoft.AspNetCore.Http;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryHubWeb.Data;
using DeliveryHubWeb.Models;
using Microsoft.AspNetCore.SignalR;

namespace DeliveryHubWeb.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        public AdminController(ApplicationDbContext context) => _context = context;

        private async Task SyncVoucherNotifications()
        {
            await _syncLock.WaitAsync();
            try {
                var now = DateTime.Now;
                var expiringThreshold = now.AddDays(3);

                var vouchers = await _context.Vouchers.Where(v => v.IsActive).ToListAsync();

                foreach (var v in vouchers)
                {
                    // Expiring soon (<= 3 days)
                    if (v.EndDate <= expiringThreshold && v.EndDate > now)
                    {
                        await CreateVoucherNotificationIfNotExist(v, NotificationType.VoucherExpiring, 
                            "Voucher sắp hết hạn", $"Mã '{v.Code}' sẽ hết hạn vào {v.EndDate:dd/MM/yyyy}.");
                    }
                    
                    // Expired
                    if (v.EndDate <= now)
                    {
                        await CreateVoucherNotificationIfNotExist(v, NotificationType.VoucherExpired, 
                            "Voucher đã hết hạn", $"Mã '{v.Code}' đã hết hạn vào {v.EndDate:dd/MM/yyyy}.");
                    }
                    
                    // Limit soon (Remaining <= 10)
                    if (v.MaxUsageCount > 0 && (v.MaxUsageCount - v.UsedCount) <= 10 && v.UsedCount < v.MaxUsageCount)
                    {
                        await CreateVoucherNotificationIfNotExist(v, NotificationType.VoucherLimitSoon, 
                            "Voucher sắp hết lượt", $"Mã '{v.Code}' chỉ còn {v.MaxUsageCount - v.UsedCount} lượt dùng.");
                    }

                    // Limit reached
                    if (v.MaxUsageCount > 0 && v.UsedCount >= v.MaxUsageCount)
                    {
                        await CreateVoucherNotificationIfNotExist(v, NotificationType.VoucherLimitReached, 
                            "Voucher đã hết lượt", $"Mã '{v.Code}' đã hết lượt dùng ({v.MaxUsageCount}).");
                    }
                }
            } finally {
                _syncLock.Release();
            }
        }

        private async Task CreateVoucherNotificationIfNotExist(Voucher v, NotificationType type, string title, string message)
        {
            // Check for existing Admin notification
            bool exists = await _context.Notifications.AnyAsync(n => 
                n.Type == type && n.RelatedId == v.Id.ToString() && n.RecipientId == null);

            if (!exists)
            {
                _context.Notifications.Add(new Notification
                {
                    Title = title,
                    Message = message,
                    Type = type,
                    RelatedId = v.Id.ToString(),
                    TargetUrl = "/Admin/Vouchers",
                    RecipientId = null,
                    CreatedAt = DateTime.Now,
                    IsRead = false
                });
                await _context.SaveChangesAsync();
            }
        }

        private async Task UpdateNotificationOnApproval(NotificationType type, string relatedId)
        {
            var notifs = await _context.Notifications.Where(n => n.Type == type && n.RelatedId == relatedId).ToListAsync();
            if (notifs.Any())
            {
                _context.Notifications.RemoveRange(notifs);
                await _context.SaveChangesAsync();
            }
        }

        private static readonly System.Threading.SemaphoreSlim _syncLock = new System.Threading.SemaphoreSlim(1, 1);
        private static bool _isFullSyncPerformDone = false;

        [HttpPost]
        public async Task<IActionResult> ForceRefreshSync()
        {
            _isFullSyncPerformDone = false;
            await EnsureDatabaseSchemaAndNotifications();
            try 
            {
                var allUserIds = await _context.Users.Select(u => u.Id).ToListAsync();
                var approvedUsers = await _context.Users.Where(u => u.IsApproved).Select(u => u.Id).ToListAsync();
                var userNotifs = await _context.Notifications
                    .Where(n => (n.Type == NotificationType.PartnerRegistration || n.Type == NotificationType.ShipperRegistration) && n.RelatedId != null && (!allUserIds.Contains(n.RelatedId) || approvedUsers.Contains(n.RelatedId)))
                    .ToListAsync();
                if (userNotifs.Any()) _context.Notifications.RemoveRange(userNotifs);

                var allStoreIds = await _context.Stores.Select(s => s.Id.ToString()).ToListAsync();
                var approvedStores = await _context.Stores.Where(s => s.IsOpen && s.ActivityState == StoreActivityState.Active).Select(s => s.Id.ToString()).ToListAsync();
                var storeNotifs = await _context.Notifications
                    .Where(n => n.Type == NotificationType.StoreRegistration && n.RelatedId != null && (!allStoreIds.Contains(n.RelatedId) || approvedStores.Contains(n.RelatedId)))
                    .ToListAsync();
                if (storeNotifs.Any()) _context.Notifications.RemoveRange(storeNotifs);

                var activeVouchers = await _context.Vouchers.Where(v => v.IsActive && v.EndDate > DateTime.Now).Select(v => v.Id.ToString()).ToListAsync();
                var voucherNotifs = await _context.Notifications
                    .Where(n => (n.Type == NotificationType.VoucherExpired || n.Type == NotificationType.VoucherExpiring || 
                                 n.Type == NotificationType.VoucherLimitReached || n.Type == NotificationType.VoucherLimitSoon) 
                                 && n.RelatedId != null && activeVouchers.Contains(n.RelatedId))
                    .ToListAsync();
                if (voucherNotifs.Any()) _context.Notifications.RemoveRange(voucherNotifs);

                var danglingNotifs = await _context.Notifications.Where(n => n.Title.Contains("đã được duyệt") || n.Title.Contains("đã duyệt")).ToListAsync();
                if (danglingNotifs.Any()) _context.Notifications.RemoveRange(danglingNotifs);

                await _context.SaveChangesAsync();
            } 
            catch (Exception ex) 
            { 
                Console.WriteLine($"[DEBUG] Clean up failed: {ex.Message}");
            }
            return Json(new { success = true, message = "Đồng bộ dữ liệu thông báo thành công!" });
        }

        private async Task EnsureDatabaseSchemaAndNotifications()
        {
            if (_isFullSyncPerformDone) return;
            
            await _syncLock.WaitAsync();
            try {
                if (_isFullSyncPerformDone) return;
                // This method used to aggressively mutate DB state (auto-approve users/stores)
                // and wipe/recreate the Notifications table. That behavior breaks the
                // "chờ duyệt" flow and causes newly-registered entities to appear approved.
                //
                // Keep it as a lightweight schema guard only.
                try
                {
                    // Ensure RecipientId column exists (older DBs)
                    string checkColumnSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Notifications]') AND name = 'RecipientId')
                        BEGIN
                            ALTER TABLE [Notifications] ADD [RecipientId] nvarchar(max) NULL;
                        END";
                    await _context.Database.ExecuteSqlRawAsync(checkColumnSql);

                    // Ensure IsApproved exists (older DBs)
                    string checkApprovedSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[AspNetUsers]') AND name = 'IsApproved')
                        BEGIN
                            ALTER TABLE [AspNetUsers] ADD [IsApproved] bit NOT NULL CONSTRAINT DF_AspNetUsers_IsApproved DEFAULT(0);
                            -- Khong duoc auto-approve nua.
                        END";
                    await _context.Database.ExecuteSqlRawAsync(checkApprovedSql);

                    // Ensure PartnerCode exists
                    string checkPartnerCodeSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[AspNetUsers]') AND name = 'PartnerCode')
                        BEGIN
                            ALTER TABLE [AspNetUsers] ADD [PartnerCode] nvarchar(max) NULL;
                        END";
                    await _context.Database.ExecuteSqlRawAsync(checkPartnerCodeSql);
                    }
                    catch
                    {
                        // Ignore schema checks if provider doesn't support it.
                    }

                    try 
                    {
                        // Fix old legacy URLs in notifications table
                        string fixUrlsSql = @"
                            UPDATE [Notifications] 
                            SET [TargetUrl] = REPLACE([TargetUrl], '/Admin/ShipperRegistration/', '/Admin/ShipperDetail/')
                            WHERE [TargetUrl] LIKE '/Admin/ShipperRegistration/%';

                            UPDATE [Notifications]
                            SET [TargetUrl] = '/Admin/Merchants?openPartnerId=' + REPLACE([TargetUrl], '/Admin/PartnerRegistration/', '')
                            WHERE [TargetUrl] LIKE '/Admin/PartnerRegistration/%';

                            UPDATE [Notifications]
                            SET [TargetUrl] = '/Admin/Merchants#store-' + REPLACE([TargetUrl], '/Admin/StoreRegistration/', '')
                            WHERE [TargetUrl] LIKE '/Admin/StoreRegistration/%';
                        ";
                        await _context.Database.ExecuteSqlRawAsync(fixUrlsSql);
                    }
                    catch { }

                    _isFullSyncPerformDone = true;
            } catch (Exception ex) {
                Console.WriteLine($"[CRITICAL-SYNC-ERROR] {ex.Message}");
            } finally {
                _syncLock.Release();
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            await SyncVoucherNotifications();
            await EnsureDatabaseSchemaAndNotifications();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            var query = _context.Notifications.AsQueryable();

            if (User.IsInRole("Admin")) {
                query = query.Where(n => n.RecipientId == null);
            } else if (user != null) {
                query = query.Where(n => n.RecipientId == user.Id);
            }

            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .ToListAsync();

            var unreadCount = await query.CountAsync(n => !n.IsRead);

            return Json(new { success = true, notifications, unreadCount });
        }

        [HttpPost]
        public async Task<IActionResult> MarkNotificationAsRead(int id)
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Không tìm thấy thông báo" });
        }

        [HttpPost]
        public async Task<IActionResult> MarkAllNotificationsAsRead()
        {
            var unread = await _context.Notifications.Where(n => !n.IsRead).ToListAsync();
            foreach (var n in unread) n.IsRead = true;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult AdminNotifications()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetFilteredNotifications(string category = "all", string status = "all", int page = 1)
        {
            await SyncVoucherNotifications();
            await EnsureDatabaseSchemaAndNotifications();

            // Auto-cleanup dangling application notifications
            var invalidNotifs = await _context.Notifications
                .Where(n => (n.Type == NotificationType.ShipperRegistration || n.Type == NotificationType.PartnerRegistration) && 
                            !_context.Users.Any(u => u.Id == n.RelatedId))
                .ToListAsync();
            var invalidStoreNotifs = await _context.Notifications
                .Where(n => n.Type == NotificationType.StoreRegistration && 
                            n.RelatedId != null && !_context.Stores.Any(s => s.Id.ToString() == n.RelatedId))
                .ToListAsync();

            // Auto-cleanup redundant StoreRegistrations that are Main branches
            var mainStoreIds = await _context.Stores.Where(s => s.IsMainBranch).Select(s => s.Id.ToString()).ToListAsync();
            var redundantMainStoreNotifs = await _context.Notifications
                .Where(n => n.Type == NotificationType.StoreRegistration && n.RelatedId != null && mainStoreIds.Contains(n.RelatedId))
                .ToListAsync();

            if (invalidNotifs.Any() || invalidStoreNotifs.Any() || redundantMainStoreNotifs.Any()) {
                if (invalidNotifs.Any()) _context.Notifications.RemoveRange(invalidNotifs);
                if (invalidStoreNotifs.Any()) _context.Notifications.RemoveRange(invalidStoreNotifs);
                if (redundantMainStoreNotifs.Any()) _context.Notifications.RemoveRange(redundantMainStoreNotifs);
                await _context.SaveChangesAsync();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            var baseQuery = _context.Notifications.AsQueryable();

            if (User.IsInRole("Admin")) {
                baseQuery = baseQuery.Where(n => n.RecipientId == null);
            } else if (user != null) {
                baseQuery = baseQuery.Where(n => n.RecipientId == user.Id);
            }

            var categoryCounts = new {
                all = await baseQuery.CountAsync(),
                shipper = await baseQuery.CountAsync(n => n.Type == NotificationType.ShipperRegistration),
                partner = await baseQuery.CountAsync(n => n.Type == NotificationType.PartnerRegistration),
                branch = await baseQuery.CountAsync(n => n.Type == NotificationType.StoreRegistration),
                voucher = await baseQuery.CountAsync(n => n.Type == NotificationType.VoucherExpiring || n.Type == NotificationType.VoucherExpired || n.Type == NotificationType.VoucherLimitSoon || n.Type == NotificationType.VoucherLimitReached),
                review = await baseQuery.CountAsync(n => n.Type == NotificationType.CustomerReview || n.Type == NotificationType.ShipperReview)
            };

            var query = baseQuery;

            // Category Filtering
            if (category != "all") {
                switch(category.ToLower()) {
                    case "voucher":
                        query = query.Where(n => n.Type == NotificationType.VoucherExpiring || 
                                               n.Type == NotificationType.VoucherExpired || 
                                               n.Type == NotificationType.VoucherLimitSoon || 
                                               n.Type == NotificationType.VoucherLimitReached);
                        break;
                    case "shipper":
                        query = query.Where(n => n.Type == NotificationType.ShipperRegistration);
                        break;
                    case "partner":
                        query = query.Where(n => n.Type == NotificationType.PartnerRegistration);
                        break;
                    case "branch":
                        query = query.Where(n => n.Type == NotificationType.StoreRegistration);
                        break;
                    case "review":
                        query = query.Where(n => n.Type == NotificationType.CustomerReview || n.Type == NotificationType.ShipperReview);
                        break;
                }
            }

            // Status Filtering
            if (status != "all") {
                if (status == "pending") {
                    query = query.Where(n => n.Title.Contains("Đăng ký") || n.Title.Contains("chờ duyệt"));
                } else if (status == "approved") {
                    query = query.Where(n => n.Title.Contains("đã được duyệt") || n.Title.Contains("thành công"));
                } else if (status == "v_expiring") {
                    query = query.Where(n => n.Type == NotificationType.VoucherExpiring);
                } else if (status == "v_limit") {
                    query = query.Where(n => n.Type == NotificationType.VoucherLimitSoon);
                } else if (status == "v_expired") {
                    query = query.Where(n => n.Type == NotificationType.VoucherExpired || n.Type == NotificationType.VoucherLimitReached);
                } else if (status == "r_shipper") {
                    // 1-star user review for shipper
                    query = query.Where(n => n.Type == NotificationType.CustomerReview && n.Title.ToLower().Contains("shipper") && (n.Message.Contains("1 sao") || n.Message.Contains("1 ⭐")));
                } else if (status == "r_branch") {
                    // 1-star shipper review for branch + 1-star user review for menu/branch
                    query = query.Where(n => (n.Type == NotificationType.ShipperReview || (n.Type == NotificationType.CustomerReview && !n.Title.ToLower().Contains("shipper"))) && (n.Message.Contains("1 sao") || n.Message.Contains("1 ⭐")));
                }
            }

            int pageSize = 10;
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Json(new { success = true, notifications, page, totalPages, totalItems, categoryCounts });
        }

        private string RemoveAccents(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
            var stringBuilder = new System.Text.StringBuilder();
            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D').ToLower();
        }

        private async Task LoadShipperCodes()
        {
            var allShippers = await _context.Users
                .Where(u => u.Role == UserRole.Shipper)
                .ToListAsync();
            
            var shipperCodes = new Dictionary<string, string>();
            foreach (var u in allShippers)
            {
                shipperCodes[u.Id] = u.ShipperCode ?? "SP-???";
            }
            ViewBag.ShipperCodes = shipperCodes;
        }

        public async Task<IActionResult> Index(int days = 7, int? storeId = null)
        {
            await EnsureDatabaseSchemaAndNotifications();
            await LoadShipperCodes();
            var startDate = DateTime.UtcNow.AddDays(-days);
            
            var ordersQuery = _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                    .ThenInclude(mi => mi!.Store)
                .AsQueryable();

            if (storeId.HasValue) {
                ordersQuery = ordersQuery.Where(o => o.OrderItems.Any(oi => oi.MenuItem != null && oi.MenuItem.StoreId == storeId.Value));
            }
            if (days > 0) {
                ordersQuery = startDate.Kind == DateTimeKind.Unspecified 
                    ? ordersQuery.Where(o => o.CreatedAt >= startDate) 
                    : ordersQuery.Where(o => o.CreatedAt >= startDate);
            }

            var ordersList = await ordersQuery.ToListAsync();
            var totalOrders = ordersList.Count;
            var totalRevenue = ordersList.Sum(o => o.TotalPrice + o.ShippingFee);
            var totalUsers = await _context.Users.CountAsync();
            var totalStores = await _context.Stores.CountAsync();
            var activeShippers = await _context.Users.CountAsync(u => u.Role == UserRole.Shipper && u.IsActive);
            
            var completedOrders = ordersList.Where(o => o.Status == OrderStatus.Completed).ToList();
            
            decimal adminIncome = 0;
            decimal totalCommission = 0;
            decimal totalShippingCollected = 0;
            decimal totalShipperPayout = 0;
            decimal totalDiscount = 0;

            foreach(var o in completedOrders) 
            {
                decimal itemsTotal = o.OrderItems?.Sum(oi => oi.Price * oi.Quantity) ?? 0m;
                decimal commission = itemsTotal * 0.20m;
                adminIncome += (commission + o.ShippingFee - o.ShipperIncome - o.DiscountAmount);

                totalCommission += commission;
                totalShippingCollected += o.ShippingFee;
                totalShipperPayout += o.ShipperIncome;
                totalDiscount += o.DiscountAmount;
            }

            double revenueTrend = 0;
            double adminIncomeTrend = 0;
            double countTrend = 0;

            if (days > 0) {
                var prevStartDate = startDate.AddDays(-days);
                var prevOrdersQuery = _context.Orders.Include(o => o.OrderItems).AsQueryable();
                
                if (storeId.HasValue) {
                    prevOrdersQuery = prevOrdersQuery.Where(o => o.OrderItems.Any(oi => oi.MenuItem != null && oi.MenuItem.StoreId == storeId.Value));
                }
                
                var prevOrdersList = await prevOrdersQuery
                    .Where(o => o.CreatedAt >= prevStartDate && o.CreatedAt < startDate)
                    .ToListAsync();
                
                decimal prevRevenue = prevOrdersList.Sum(o => o.TotalPrice + o.ShippingFee);
                int prevCount = prevOrdersList.Count;
                
                var prevCompleted = prevOrdersList.Where(o => o.Status == OrderStatus.Completed).ToList();
                decimal prevAdminIncome = prevCompleted.Sum(o => {
                    decimal items = o.OrderItems?.Sum(oi => oi.Price * oi.Quantity) ?? 0m;
                    return (items * 0.20m) + o.ShippingFee - o.ShipperIncome - o.DiscountAmount;
                });
                
                revenueTrend = prevRevenue == 0 ? (totalRevenue > 0 ? 100 : 0) : (double)((totalRevenue - prevRevenue) / prevRevenue * 100m);
                adminIncomeTrend = prevAdminIncome == 0 ? (adminIncome > 0 ? 100 : (adminIncome < 0 ? -100 : 0)) : (double)((adminIncome - prevAdminIncome) / Math.Abs(prevAdminIncome) * 100m);
                countTrend = prevCount == 0 ? (totalOrders > 0 ? 100 : 0) : (double)(totalOrders - prevCount) / prevCount * 100.0;
            }

            ViewBag.Stats = new {
                TotalOrders = totalOrders,
                TotalRevenue = totalRevenue,
                AdminIncome = adminIncome,
                AdminGrossRevenue = totalCommission + totalShippingCollected,
                AdminTotalExpense = totalShipperPayout + totalDiscount,
                TotalCommission = totalCommission,
                TotalShippingCollected = totalShippingCollected,
                TotalShipperPayout = totalShipperPayout,
                TotalDiscount = totalDiscount,
                RevenueTrend = revenueTrend,
                AdminIncomeTrend = adminIncomeTrend,
                CountTrend = countTrend,
                TotalUsers = totalUsers,
                TotalStores = totalStores,
                ActiveShippers = activeShippers,
                SelectedDays = days,
                SelectedStoreId = storeId
            };

            ViewBag.Stores = await _context.Stores.OrderBy(s => s.Name).ToListAsync();

            var recentOrders = ordersList
                .OrderBy(o => o.OrderCode)
                .Take(6)
                .ToList();

            var chartData = ordersList
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => {
                    var completed = g.Where(x => x.Status == OrderStatus.Completed).ToList();
                    decimal dailyAdminIncome = completed.Sum(o => {
                        decimal items = o.OrderItems?.Sum(oi => oi.Price * oi.Quantity) ?? 0m;
                        return (items * 0.20m) + o.ShippingFee; // Tổng doanh thu chảy về Admin
                    });
                    decimal dailyAdminExpense = completed.Sum(o => {
                        return o.ShipperIncome + o.DiscountAmount; // Admin chỉ chi trả cho Shipper và Voucher
                    });
                    
                    return new { 
                        Date = g.Key, 
                        Count = g.Count(), 
                        Revenue = g.Sum(o => o.TotalPrice + o.ShippingFee),
                        AdminIncome = dailyAdminIncome,
                        AdminExpense = dailyAdminExpense
                    };
                })
                .OrderBy(g => g.Date)
                .ToList();
            
            ViewBag.ChartData = chartData;

            // Thống kê trạng thái đơn hàng
            var statusStats = ordersList
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                .ToList();
            ViewBag.StatusStats = statusStats;

            return View(recentOrders);
        }

        public async Task<IActionResult> DashboardOrders(int days = 7, int? storeId = null, int page = 1)
        {
            await LoadShipperCodes();
            var startDate = DateTime.UtcNow.AddDays(-days);

            var query = _context.Orders
                .Include(o => o.User)
                .Include(o => o.Shipper)
                .Include(o => o.Store)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                    .ThenInclude(mi => mi!.Store)
                .AsQueryable();

            if (storeId.HasValue) {
                query = query.Where(o => o.OrderItems.Any(oi => oi.MenuItem != null && oi.MenuItem.StoreId == storeId.Value));
            }
            if (days > 0) {
                query = startDate.Kind == DateTimeKind.Unspecified 
                    ? query.Where(o => o.CreatedAt >= startDate) 
                    : query.Where(o => o.CreatedAt >= startDate);
            }

            int pageSize = 15;
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SelectedDays = days;
            ViewBag.SelectedStoreId = storeId;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            ViewBag.Stores = await _context.Stores.OrderBy(s => s.Name).ToListAsync();

            return View(orders);
        }

        public async Task<IActionResult> Orders(int? storeId, OrderStatus? status, string search, int page = 1)
        {
            await LoadShipperCodes();
            var query = _context.Orders
                .Include(o => o.User)
                .Include(o => o.Shipper)
                .Include(o => o.Store)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .AsQueryable();

            if (storeId.HasValue) {
                query = query.Where(o => o.StoreId == storeId.Value);
            }
            if (status.HasValue) {
                if (status.Value == OrderStatus.Pending) {
                    query = query.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing);
                } else if (status.Value == OrderStatus.SearchingShipper) {
                    query = query.Where(o => o.Status == OrderStatus.SearchingShipper || o.Status == OrderStatus.Accepted);
                } else {
                    query = query.Where(o => o.Status == status.Value);
                }
            }
            if (!string.IsNullOrEmpty(search)) {
                query = query.Where(o => o.OrderCode.Contains(search) || 
                                   (o.User != null && o.User.FullName != null && o.User.FullName.Contains(search)));
            }

            int pageSize = 10;
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var orders = await query
                .OrderByDescending(o => o.IsPinned)
                .ThenBy(o => o.OrderCode)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Stores = await _context.Stores.OrderBy(s => s.Name).ToListAsync();
            ViewBag.SelectedStore = storeId;
            ViewBag.SelectedStatus = status;
            ViewBag.SearchTerm = search;
            ViewBag.TotalItems = totalItems;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(orders);
        }

        [HttpPost]
        public async Task<IActionResult> CancelActiveOrders()
        {
            var activeOrders = await _context.Orders.Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Failed && o.Status != OrderStatus.Cancelled).ToListAsync();
            foreach(var order in activeOrders) {
                order.Status = OrderStatus.Cancelled;
                order.CancelledAt = DateTime.Now;
            }
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = $"Đã hủy {activeOrders.Count} đơn hàng treo." });
        }

        public async Task<IActionResult> Merchants(string search, bool? active, int page = 1)
        {
            // === CLEANUP DUP STORES TRỰC TIẾP KHI TRUY CẬP (Đảm bảo dọn dẹp tức thì) ===
            var allStoresForCleanup = await _context.Stores.ToListAsync();
            var groupedStoresCleanup = allStoresForCleanup
                .GroupBy(s => new { s.OwnerId, s.Address })
                .Where(g => g.Count() > 1)
                .ToList();

            if (groupedStoresCleanup.Any())
            {
                foreach (var group in groupedStoresCleanup) {
                    var keepStore = group.OrderBy(s => s.Id).First();
                    var dupStores = group.OrderBy(s => s.Id).Skip(1).ToList();

                    foreach (var dup in dupStores) {
                        var dupOrders = await _context.Orders.Where(o => o.StoreId == dup.Id).ToListAsync();
                        foreach (var o in dupOrders) { o.StoreId = keepStore.Id; }
                        
                        var dupMenus = await _context.MenuItems.Where(m => m.StoreId == dup.Id).ToListAsync();
                        _context.MenuItems.RemoveRange(dupMenus);
                    }
                    _context.Stores.RemoveRange(dupStores);
                }
                await _context.SaveChangesAsync();
            }
            // ===================================

            // Only show approved partners in management table.
            var query = _context.Stores
                .Include(s => s.Owner)
                .Where(s => s.Owner != null && s.Owner.IsApproved)
                .AsQueryable();

            if (active.HasValue) {
                query = query.Where(s => s.Owner != null && s.Owner.IsActive == active.Value);
            }
            if (!string.IsNullOrEmpty(search)) {
                string s = search.ToLower().Trim();
                // Sử dụng Collate để tìm kiếm không phân biệt dấu (Accent Insensitive) trên SQL Server
                query = query.Where(x => EF.Functions.Collate(x.Name, "SQL_Latin1_General_CP1_CI_AI").Contains(s) || 
                                   (x.Owner != null && x.Owner.FullName != null && EF.Functions.Collate(x.Owner.FullName, "SQL_Latin1_General_CP1_CI_AI").Contains(s)) ||
                                   (x.Owner != null && x.Owner.PhoneNumber != null && x.Owner.PhoneNumber.Contains(s)) ||
                                   (s.Length >= 3 && x.Owner != null && x.Owner.Email != null && x.Owner.Email.ToLower().Contains(s)));
            }

            // Lấy toàn bộ kết quả phù hợp để gom nhóm (Xóa bản trùng lặp tên/chủ sở hữu)
            var allFiltered = await query.ToListAsync();
            var groupedItems = allFiltered
                .GroupBy(s => s.OwnerId)
                .Select(g => g.OrderByDescending(x => x.IsMainBranch).ThenBy(x => x.Id).First())
                .OrderBy(s => s.Owner != null && s.Owner.IsActive ? 1 : 0) // Ngưng hoạt động (0) lên đầu
                .ThenBy(s => s.Id) // Sắp xếp theo thứ tự mã 1 -> 18
                .ToList();

            int pageSize = 10;
            int totalItems = groupedItems.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            
            var pagedStores = groupedItems
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Tính số lượng chi nhánh cho từng chủ sở hữu (vẫn giữ logic cũ)
            var ownerBranchCounts = await _context.Stores
                .GroupBy(s => s.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OwnerId, x => x.Count);

            var partnerCodes = await _context.Stores
                .GroupBy(s => s.OwnerId)
                .Select(g => new {
                    OwnerId = g.Key,
                    MainStoreId = g.OrderByDescending(x => x.IsMainBranch).ThenBy(x => x.Id).Select(x => x.Id).FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.OwnerId, x => $"DT-{x.MainStoreId:D3}");

            var userPartnerCodes = await _context.Users
                .Where(u => u.Role == UserRole.Partner && !string.IsNullOrEmpty(u.PartnerCode))
                .ToDictionaryAsync(u => u.Id, u => u.PartnerCode!);

            foreach (var kvp in userPartnerCodes)
            {
                partnerCodes[kvp.Key] = kvp.Value;
            }

            ViewBag.BranchCounts = ownerBranchCounts;
            ViewBag.PartnerCodes = partnerCodes;
            ViewBag.SearchTerm = search;
            ViewBag.SelectedActive = active;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            
            ViewBag.TotalBranches = allFiltered.Count;
            ViewBag.OpenBranches = allFiltered.Count(s => s.IsOpen);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") {
                return View(pagedStores);
            }

            return View(pagedStores);
        }

        public async Task<IActionResult> MerchantStores(string ownerId, int? highlightStoreId)
        {
            var stores = await _context.Stores
                .Where(s => s.OwnerId == ownerId)
                .OrderByDescending(s => s.IsMainBranch)
                .ThenBy(s => s.Id)
                .ToListAsync();

            ViewBag.HighlightStoreId = highlightStoreId;

            var partner = await _context.Users.FindAsync(ownerId);
            ViewBag.IsPartnerActive = partner?.IsActive ?? true;

            var storeIds = stores.Select(s => s.Id).ToList();

            // Tính số sao thực tế từ bảng Review
            var reviewStats = await _context.Reviews
                .Include(r => r.Order)
                .ThenInclude(o => o!.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
                .Where(r => r.Order != null && r.Order.OrderItems.Any(oi => oi.MenuItem != null && storeIds.Contains(oi.MenuItem.StoreId)))
                .ToListAsync();

            var statsDict = new Dictionary<int, (int Count, double Avg)>();
            foreach (var store in stores)
            {
                var storeReviews = reviewStats.Where(r => r.Order != null && r.Order.OrderItems.Any(oi => oi.MenuItem != null && oi.MenuItem.StoreId == store.Id)).ToList();
                int count = storeReviews.Count;
                double avg = count > 0 ? storeReviews.Average(r => r.RatingMenu) : 5.0;
                statsDict[store.Id] = (count, avg);
            }

            ViewBag.ReviewStats = statsDict;

            var storeIdsStr = storeIds.Select(id => id.ToString()).ToList();
            var pendingStoreIds = await _context.Notifications
                .Where(n => n.Type == NotificationType.StoreRegistration && n.RelatedId != null && storeIdsStr.Contains(n.RelatedId))
                .Select(n => n.RelatedId)
                .ToListAsync();
            ViewBag.PendingStoreIds = pendingStoreIds;

            return PartialView("_MerchantStoresTable", stores);
        }

        public async Task<IActionResult> StoreDetail(int id, int page = 1)
        {
            var store = await _context.Stores
                .Include(s => s.Owner)
                .Include(s => s.MenuItems)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (store == null) return NotFound();

            var ownerStores = await _context.Stores
                .Where(s => s.OwnerId == store.OwnerId)
                .OrderBy(s => s.Id)
                .ToListAsync();

            var firstStoreId = ownerStores.First().Id;
            var partnerCode = $"DT-{firstStoreId:D3}";
            var branchIndex = ownerStores.FindIndex(s => s.Id == store.Id) + 1;
            
            ViewBag.PartnerCode = partnerCode;
            ViewBag.BranchCode = $"{partnerCode}-CN{branchIndex:D3}";

            var unreadStoreRegistration = await _context.Notifications
                .AnyAsync(n => n.Type == NotificationType.StoreRegistration && n.RelatedId == store.Id.ToString());
            ViewBag.IsPending = unreadStoreRegistration;

            // Item-level Sales Counts & Ratings (Only from Completed orders as requested)
            var completedOrderItems = await _context.OrderItems
                .Include(oi => oi.Order)
                .Where(oi => oi.Order!.StoreId == id && oi.Order.Status == OrderStatus.Completed)
                .ToListAsync();

            var itemSalesCounts = completedOrderItems
                .GroupBy(oi => oi.MenuItemId)
                .ToDictionary(g => g.Key, g => g.Count());

            // Top Selling Items (Only from Completed orders as requested)
            var topMenuItemIds = itemSalesCounts
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key)
                .Take(5)
                .ToList();

            var topItems = store.MenuItems
                .Where(m => topMenuItemIds.Contains(m.Id))
                .OrderBy(m => topMenuItemIds.IndexOf(m.Id))
                .ToList();

            // Pagination Logic
            const int pageSize = 10;
            var totalMenuItems = store.MenuItems.Count;
            var totalPages = (int)Math.Ceiling(totalMenuItems / (double)pageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var paginatedItems = store.MenuItems
                .OrderBy(m => m.Category)
                .ThenBy(m => m.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Replace full list with paginated list for the view's simplicity
            store.MenuItems = paginatedItems;

            // Calculate Revenue Stats (Menu Price only for Completed orders)
            var completedOrders = completedOrderItems.GroupBy(oi => oi.OrderId).Select(g => g.First().Order).ToList();

            var totalRevenue = completedOrderItems.Sum(oi => oi.Price * oi.Quantity);
            var totalOrders = completedOrders.Count;

            // Daily Revenue for last 7 days (Based on OrderItems)
            var today = DateTime.Today.AddDays(1).AddSeconds(-1);
            var sevenDaysAgo = DateTime.Today.AddDays(-6);
            
            var dailyRevenue = completedOrderItems
                .Where(oi => oi.Order!.CompletedAt >= sevenDaysAgo && oi.Order.CompletedAt <= today)
                .GroupBy(oi => oi.Order!.CompletedAt?.Date)
                .Select(g => new { Date = g.Key, Amount = g.Sum(oi => oi.Price * oi.Quantity) })
                .ToList();

            // Rating Stats
            var reviews = await _context.Reviews
                .Include(r => r.Order)
                .ThenInclude(o => o != null ? o.OrderItems : null)
                .Where(r => r.Order != null && r.Order.StoreId == id)
                .ToListAsync();
            
            var avgRating = reviews.Any() ? reviews.Average(r => r.RatingMenu) : 5.0;

            // Item-level Ratings (Only from Completed orders to filter out shipper/failed order context)
            var itemRatings = reviews
                .Where(r => r.Order!.Status == OrderStatus.Completed)
                .SelectMany(r => r.Order!.OrderItems.Select(oi => new { oi.MenuItemId, r.RatingMenu }))
                .GroupBy(x => x.MenuItemId)
                .ToDictionary(g => g.Key, g => (Count: g.Count(), Avg: g.Average(x => x.RatingMenu)));

            ViewBag.ItemSalesCounts = itemSalesCounts;

            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.TotalOrders = totalOrders;
            ViewBag.AvgRating = avgRating;
            ViewBag.ReviewCount = reviews.Count;
            ViewBag.DailyRevenue = dailyRevenue;
            ViewBag.ItemRatings = itemRatings;
            ViewBag.TopItems = topItems;
            
            // Pagination ViewBag items
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalMenuItems;

            return View(store);
        }


        [HttpPost]
        public async Task<IActionResult> TogglePin(int id, int? storeId, OrderStatus? status, string search)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                order.IsPinned = !order.IsPinned;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Orders), new { storeId, status, search });
        }

        [HttpPost]
        public async Task<IActionResult> ApproveShipper(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || user.Role != UserRole.Shipper) return Json(new { success = false, message = "Không tìm thấy Shipper." });
            user.IsApproved = true;
            user.IsActive = false; // Shipper mặc định sẽ Offline, chỉ chuyển sang Online khi họ tự bật trạng thái

            if (string.IsNullOrWhiteSpace(user.ShipperCode))
            {
                var existingCodes = await _context.Users
                    .Where(u => u.Role == UserRole.Shipper && u.ShipperCode != null && u.ShipperCode.StartsWith("SP-"))
                    .Select(u => u.ShipperCode!)
                    .ToListAsync();

                int maxCode = 0;
                foreach (var code in existingCodes)
                {
                    if (code.Length >= 6 && int.TryParse(code.Substring(3), out int c) && c > maxCode) maxCode = c;
                }
                user.ShipperCode = $"SP-{(maxCode + 1):D3}";
            }

            await UpdateNotificationOnApproval(NotificationType.ShipperRegistration, id);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> RejectShipper(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || user.Role != UserRole.Shipper) return Json(new { success = false, message = "Không tìm thấy Shipper." });
            _context.Users.Remove(user);
            await UpdateNotificationOnApproval(NotificationType.ShipperRegistration, id);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ApprovePartner(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || user.Role != UserRole.Partner) return Json(new { success = false, message = "Không tìm thấy Đối tác." });
            user.IsApproved = true;
            user.IsActive = true;

            // Auto-approve main branch only when partner is approved
            var stores = await _context.Stores.Where(s => s.OwnerId == id).OrderByDescending(s => s.IsMainBranch).ThenBy(s => s.Id).ToListAsync();
            var mainStore = stores.FirstOrDefault();
            if (mainStore != null)
            {
                foreach (var st in stores) st.IsMainBranch = st.Id == mainStore.Id;
                mainStore.ActivityState = StoreActivityState.Active;
                mainStore.IsOpen = true;
            }

            // Assign sequential PartnerCode if null
            if (string.IsNullOrWhiteSpace(user.PartnerCode))
            {
                var existingCodes = await _context.Users
                    .Where(u => u.Role == UserRole.Partner && u.PartnerCode != null && u.PartnerCode.StartsWith("DT-"))
                    .Select(u => u.PartnerCode!)
                    .ToListAsync();

                int maxCode = 0;
                foreach (var code in existingCodes)
                {
                    if (code.Length >= 6 && int.TryParse(code.Substring(3), out int c) && c > maxCode) maxCode = c;
                }
                user.PartnerCode = $"DT-{(maxCode + 1):D3}";
            }

            await UpdateNotificationOnApproval(NotificationType.PartnerRegistration, id);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> RejectPartner(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || user.Role != UserRole.Partner) return Json(new { success = false, message = "Không tìm thấy Đối tác." });

            // Remove associated stores first to prevent FK constraint errors
            var stores = await _context.Stores.Where(s => s.OwnerId == id).ToListAsync();
            
            // Cleanup any dangling StoreRegistration notifications
            var storeIds = stores.Select(s => s.Id.ToString()).ToList();
            var storeNotifs = await _context.Notifications
                .Where(n => n.Type == NotificationType.StoreRegistration && n.RelatedId != null && storeIds.Contains(n.RelatedId))
                .ToListAsync();
                
            if (storeNotifs.Any()) _context.Notifications.RemoveRange(storeNotifs);
            if (stores.Any()) _context.Stores.RemoveRange(stores);

            _context.Users.Remove(user);
            await UpdateNotificationOnApproval(NotificationType.PartnerRegistration, id);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ApproveStore(int id)
        {
            var store = await _context.Stores.Include(s => s.Owner).FirstOrDefaultAsync(s => s.Id == id);
            if (store == null) return Json(new { success = false, message = "Không tìm thấy Chi nhánh." });
            
            store.IsOpen = false;
            store.ActivityState = (store.Owner != null && !store.Owner.IsActive) ? StoreActivityState.LockedByPartner : StoreActivityState.Active;
            
            await UpdateNotificationOnApproval(NotificationType.StoreRegistration, id.ToString());
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> RejectStore(int id)
        {
            var store = await _context.Stores.FindAsync(id);
            if (store == null) return Json(new { success = false, message = "Không tìm thấy Chi nhánh." });
            _context.Stores.Remove(store);
            await UpdateNotificationOnApproval(NotificationType.StoreRegistration, id.ToString());
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> SuspendStore(int id)
        {
            var store = await _context.Stores.FindAsync(id);
            if (store == null) return Json(new { success = false, message = "Không tìm thấy Chi nhánh." });
            store.ActivityState = StoreActivityState.LockedByPartner;
            store.IsOpen = false;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> RestoreStore(int id)
        {
            var store = await _context.Stores.FindAsync(id);
            if (store == null) return Json(new { success = false, message = "Không tìm thấy Chi nhánh." });
            store.ActivityState = StoreActivityState.Active;
            store.IsOpen = false; // Vẫn giữ trạng thái đóng cửa để đối tác tự mở lại
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> SuspendPartner(string ownerId)
        {
            var user = await _context.Users.FindAsync(ownerId);
            var storesToSuspend = await _context.Stores.Where(s => s.OwnerId == ownerId).ToListAsync();
            
            if (user == null)
                return Json(new { success = false, message = "Không tìm thấy đối tác." });

            // Khóa tài khoản đối tác
            user.IsActive = false;

            // Khóa toàn bộ chi nhánh
            foreach (var store in storesToSuspend)
            {
                store.IsOpen = false;
            }
            
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Đã đình chỉ đối tác và khóa toàn bộ chi nhánh thành công!" });
        }

        [HttpPost]
        public async Task<IActionResult> RestorePartner(string ownerId)
        {
            var user = await _context.Users.FindAsync(ownerId);
            var storesToRestore = await _context.Stores.Where(s => s.OwnerId == ownerId).ToListAsync();
            
            if (user == null)
                return Json(new { success = false, message = "Không tìm thấy đối tác." });

            // Kích hoạt lại tài khoản đối tác
            user.IsActive = true;

            // Mở khóa toàn bộ chi nhánh, nhưng giữ trạng thái cửa hàng đang đóng
            foreach (var store in storesToRestore)
            {
                store.IsOpen = false; // Phải được đối tác tự bật lên lại
            }
            
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Đã khôi phục đối tác và mở khóa các chi nhánh thành công!" });
        }

        [HttpPost("Admin/ToggleLock/{id}")]
        public async Task<IActionResult> ToggleLock(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || (user.Role != UserRole.User && user.Role != UserRole.Shipper)) 
                return Json(new { success = false, message = "Không tìm thấy người dùng hoặc chức năng không hỗ trợ loại user này." });

            if (user.IsLocked) {
                // Đang bị khóa -> Phục hồi
                user.IsLocked = false;
                user.PendingLock = false; // Xóa hàng chờ nếu có
                // Khi phục hồi, Shipper/User trở về trạng thái Ngưng hoạt động / Offline
                user.IsActive = false; 
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Đã mở khóa tài khoản thành công. Tài khoản hiện đang ở trạng thái ngưng hoạt động/offline." });
            } else {
                // Đang mở -> Khóa
                if (user.Role == UserRole.Shipper && user.IsDelivering) {
                    user.PendingLock = true;
                    // Không khóa ngay mà đưa vào hàng chờ
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Shipper đang giao đơn, đã đưa vào hàng chờ khóa tài khoản sau khi hoàn thành đơn." });
                } else {
                    user.IsLocked = true;
                    user.IsActive = false; // Ngưng hoạt động
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Đã khóa tài khoản và ngưng hoạt động thành công." });
                }
            }
        }

        public async Task<IActionResult> Users(string search, string filter = "all", int page = 1)
        {
            var query = _context.Users.Where(u => u.Role == UserRole.User).AsQueryable();

            var allUsersRaw = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

            if (!string.IsNullOrEmpty(search))
            {
                var searchNorm = RemoveAccents(search);
                allUsersRaw = allUsersRaw
                    .Where(u => RemoveAccents(u.FullName ?? "").Contains(searchNorm) || 
                                RemoveAccents(u.Email ?? "").Contains(searchNorm) || 
                                RemoveAccents(u.PhoneNumber ?? "").Contains(searchNorm))
                    .ToList();
            }

            var allUsers = new List<UserViewModel>();
            
            var userIds = allUsersRaw.Select(u => u.Id).ToList();
            
            var orderCounts = await _context.Orders
                .Where(o => userIds.Contains(o.UserId) && o.Status == OrderStatus.Completed)
                .GroupBy(o => o.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(k => k.UserId, v => v.Count);

            foreach (var u in allUsersRaw)
            {
                int totalCompletedOrders = orderCounts.ContainsKey(u.Id) ? orderCounts[u.Id] : 0;
                string rank = string.IsNullOrEmpty(u.UserTier) ? "Đồng" : u.UserTier;

                allUsers.Add(new UserViewModel
                {
                    Id = u.Id,
                    FullName = string.IsNullOrEmpty(u.FullName) ? "Chưa cập nhật" : u.FullName,
                    Email = string.IsNullOrEmpty(u.Email) ? "Chưa cập nhật" : u.Email,
                    PhoneNumber = string.IsNullOrEmpty(u.PhoneNumber) ? "Chưa cập nhật" : u.PhoneNumber,
                    AvatarUrl = string.IsNullOrEmpty(u.AvatarUrl) ? "/images/default-avatar.png" : u.AvatarUrl,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    TotalCompletedOrders = totalCompletedOrders,
                    Rank = rank
                });
            }

            if (filter == "active") {
                allUsers = allUsers.Where(s => s.IsActive).ToList();
            } else if (filter == "pending") {
                allUsers = allUsers.Where(s => !s.IsActive).ToList();
            }

            int pageSize = 10;
            int totalItems = allUsers.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var pagedUsers = allUsers.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.SearchTerm = search;
            ViewBag.CurrentFilter = filter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            
            var totalCount = await _context.Users.CountAsync(u => u.Role == UserRole.User);
            var activeCount = await _context.Users.CountAsync(u => u.Role == UserRole.User && u.IsActive);
            var lockedCount = totalCount - activeCount;

            ViewBag.TotalCount = totalCount;
            ViewBag.ActiveCount = activeCount;
            ViewBag.LockedCount = lockedCount;

            return View(pagedUsers);
        }

        public async Task<IActionResult> Shippers(string search, string filter = "all", int page = 1)        {
            // Only show approved shippers in management table.
            var query = _context.Users.Where(u => u.Role == UserRole.Shipper && u.IsApproved).AsQueryable();


            var allShippersRaw = await query.OrderBy(u => u.Id).ToListAsync();

            // === SMART SEARCH: Accent Insensitive Filter (In-Memory) ===
            if (!string.IsNullOrEmpty(search))
            {
                var searchNorm = RemoveAccents(search);
                allShippersRaw = allShippersRaw
                    .Where(u => RemoveAccents(u.FullName ?? "").Contains(searchNorm))
                    .ToList();
            }
            // ===================================

            var shipperIds = allShippersRaw.Select(s => s.Id).ToList();

            var allShipperOrders = await _context.Orders
                .Where(o => o.ShipperId != null && shipperIds.Contains(o.ShipperId!))
                .ToListAsync();

            var reviews = await _context.Reviews
                .Include(r => r.Order)
                .Where(r => r.Order != null && r.Order.ShipperId != null && shipperIds.Contains(r.Order.ShipperId!))
                .ToListAsync();

            var allShippers = new List<ShipperViewModel>();
            for (int i = 0; i < allShippersRaw.Count; i++)
            {
                var user = allShippersRaw[i];
                var shipperOrders = allShipperOrders.Where(o => o.ShipperId == user.Id).ToList();
                var shipperCompleted = shipperOrders.Where(o => o.Status == OrderStatus.Completed).ToList();
                var shipperReviews = reviews.Where(r => r.Order?.ShipperId == user.Id).ToList();

                var shipperIncomeOrders = shipperOrders.Where(o => o.Status == OrderStatus.Completed || o.Status == OrderStatus.Failed).ToList();

                var orderCount = shipperCompleted.Count();
                var totalIncome = shipperIncomeOrders.Sum(o => o.ShipperIncome);

                // Find active delivery if any
                var activeOrder = shipperOrders.FirstOrDefault(o => o.Status == OrderStatus.Delivering);
                
                double rating = shipperReviews.Any() ? shipperReviews.Average(r => (double)r.RatingShipper) : 5.0;

                allShippers.Add(new ShipperViewModel {
                    Id = user.Id,
                    ShipperCode = user.ShipperCode ?? "SP-???",
                    FullName = user.FullName ?? "Unknown",
                    PhoneNumber = user.PhoneNumber ?? "",
                    Vehicle = user.Vehicle ?? "Chưa cập nhật",
                    Rating = Math.Round(rating, 1),
                    DeliveredOrders = orderCount,
                    Income = totalIncome, 
                    IsActive = user.IsActive,
                    IsDelivering = activeOrder != null, // Based on actual order presence
                    ActiveOrderId = activeOrder?.Id,
                    ActiveOrderCode = activeOrder?.OrderCode,
                    IsLocked = user.IsLocked,
                    PendingLock = user.PendingLock
                });
            }
            
            // Sắp xếp theo Mã Shipper tăng dần
            allShippers = allShippers.OrderBy(s => s.ShipperCode).ToList();
            
            // Apply filtering in-memory to ensure perfect consistency with UI labels
            if (filter == "active") {
                allShippers = allShippers.Where(s => s.IsActive && !s.IsLocked).ToList();
            } else if (filter == "locked") {
                allShippers = allShippers.Where(s => s.IsLocked).ToList();
            } else if (filter == "pending") {
                allShippers = allShippers.Where(s => !s.IsActive && !s.IsLocked).ToList();
            }

            // Pagination Logic
            int pageSize = 10;
            int totalItems = allShippers.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var pagedShippers = allShippers.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.SearchTerm = search;
            ViewBag.CurrentFilter = filter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            
            var totalCount = await _context.Users.CountAsync(u => u.Role == UserRole.Shipper && u.IsApproved);
            var activeCount = await _context.Users.CountAsync(u => u.Role == UserRole.Shipper && u.IsApproved && u.IsActive);
            var offlineCount = await _context.Users.CountAsync(u => u.Role == UserRole.Shipper && u.IsApproved && !u.IsActive);
            
            ViewBag.TotalCount = totalCount;
            ViewBag.ActiveCount = activeCount;
            ViewBag.PendingCount = offlineCount;

            return View(pagedShippers);
        }

        public async Task<IActionResult> UserDetail(string id, string timeFilter = "all")
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null || user.Role != UserRole.User) return NotFound();

            var userOrdersQuery = _context.Orders.Where(o => o.UserId == user.Id).AsQueryable();

            if (timeFilter == "7days")
            {
                var date = DateTime.Now.AddDays(-7);
                userOrdersQuery = userOrdersQuery.Where(o => o.CreatedAt >= date);
            }
            else if (timeFilter == "1month")
            {
                var date = DateTime.Now.AddMonths(-1);
                userOrdersQuery = userOrdersQuery.Where(o => o.CreatedAt >= date);
            }
            else if (timeFilter == "1year")
            {
                var date = DateTime.Now.AddYears(-1);
                userOrdersQuery = userOrdersQuery.Where(o => o.CreatedAt >= date);
            }

            var userOrders = await userOrdersQuery.OrderByDescending(o => o.CreatedAt).ToListAsync();

            var completedOrders = userOrders.Where(o => o.Status == OrderStatus.Completed).ToList();
            
            string rank = "Đồng";
            if (completedOrders.Count >= 50) rank = "Kim cương";
            else if (completedOrders.Count >= 20) rank = "Vàng";
            else if (completedOrders.Count >= 5) rank = "Bạc";

            var viewModel = new UserDetailViewModel
            {
                Id = user.Id,
                FullName = string.IsNullOrEmpty(user.FullName) ? "Khách hàng" : user.FullName,
                Email = user.Email ?? "",
                PhoneNumber = user.PhoneNumber ?? "Chưa cập nhật",
                AvatarUrl = string.IsNullOrEmpty(user.AvatarUrl) ? "/images/default-avatar.png" : user.AvatarUrl,
                CreatedAt = user.CreatedAt,
                Rank = rank,
                TotalOrders = userOrders.Count,
                TotalCompletedOrders = completedOrders.Count,
                TotalFailedOrders = userOrders.Count(o => o.Status == OrderStatus.Failed),
                TotalDeliveringOrders = userOrders.Count(o => o.Status == OrderStatus.Delivering),
                TotalPendingOrders = userOrders.Count(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing),
                TotalSearchingShipperOrders = userOrders.Count(o => o.Status == OrderStatus.SearchingShipper || o.Status == OrderStatus.Accepted),
                TotalCancelledOrders = userOrders.Count(o => o.Status == OrderStatus.Cancelled),
                TotalSpent = completedOrders.Sum(o => o.TotalPrice + o.ShippingFee),
                RecentOrders = userOrders.Take(8).Select(o => new UserRecentOrderViewModel
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    DeliveryAddress = o.DeliveryAddress,
                    TotalPrice = o.TotalPrice + o.ShippingFee,
                    Status = o.Status.ToString(),
                    Time = o.CreatedAt.ToString("dd/MM HH:mm")
                }).ToList()
            };

            ViewBag.CurrentTimeFilter = timeFilter;
            return View(viewModel);
        }

        public async Task<IActionResult> UserOrders(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            var orders = await _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Where(o => o.UserId == id)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.UserName = string.IsNullOrEmpty(user.FullName) ? "Khách hàng" : user.FullName;
            ViewBag.UserId = id;

            return View(orders);
        }

        public async Task<IActionResult> ShipperDetail(string id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null || user.Role != UserRole.Shipper) return NotFound();

            if (!user.IsApproved)
            {
                return View("ShipperRegistrationDetail", user);
            }

            var shipperOrders = await _context.Orders
                .Where(o => o.ShipperId == user.Id)
                .OrderBy(o => o.OrderCode)
                .ToListAsync();

            var completedOrders = shipperOrders.Where(o => o.Status == OrderStatus.Completed).ToList();
            
            var reviews = await _context.Reviews
                .Include(r => r.Order)
                .Where(r => r.Order != null && r.Order.Status == OrderStatus.Completed && r.Order.ShipperId == user.Id)
                .ToListAsync();

            double avgRating = reviews.Any() ? reviews.Average(r => (double)r.RatingShipper) : 5.0;

            var viewModel = new ShipperDetailViewModel
            {
                Id = user.Id,
                ShipperCode = user.ShipperCode ?? "SP-???",
                FullName = user.FullName ?? "Unknown",
                Email = user.Email ?? "",
                PhoneNumber = user.PhoneNumber ?? "",
                TotalDistance = shipperOrders.Where(o => o.Status == OrderStatus.Completed || o.Status == OrderStatus.Failed).Sum(o => o.Distance),
                CitizenId = user.CitizenId ?? "",
                AvatarUrl = user.AvatarUrl ?? "/images/default-avatar.png",
                Vehicle = user.Vehicle ?? "Chưa cập nhật",
                CreatedAt = user.CreatedAt,
                AvgRating = Math.Round(avgRating, 1),
                TotalReviews = reviews.Count,
                TotalOrders = completedOrders.Count,
                TotalIncome = shipperOrders.Where(o => o.Status == OrderStatus.Completed || o.Status == OrderStatus.Failed).Sum(o => o.ShipperIncome),
                RecentOrders = shipperOrders.Take(6).Select(o => new ShipperRecentOrderViewModel {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    DeliveryAddress = o.DeliveryAddress,
                    ShippingFee = o.TotalPrice + o.ShippingFee,
                    Status = o.Status.ToString(),
                    Time = (o.Status == OrderStatus.Completed ? o.CompletedAt : o.CreatedAt)?.ToString("dd/MM HH:mm") ?? ""
                }).ToList()
            };

            var now = DateTime.UtcNow;
            var allOrderData = shipperOrders.Select(o => new {
                Status = o.Status.ToString(),
                CreatedAt = (o.Status == OrderStatus.Completed && o.CompletedAt.HasValue) ? o.CompletedAt.Value.ToString("yyyy-MM-dd") : 
                            (o.Status == OrderStatus.Failed || o.Status == OrderStatus.Cancelled) && o.CancelledAt.HasValue ? o.CancelledAt.Value.ToString("yyyy-MM-dd") : 
                            o.CreatedAt.ToString("yyyy-MM-dd"),
                NormalizedStatus = (o.Status == OrderStatus.Accepted || o.Status == OrderStatus.SearchingShipper || o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing) ? "Accepted" :
                                   (o.Status == OrderStatus.Failed || o.Status == OrderStatus.Cancelled) ? "Failed" : 
                                   o.Status.ToString()
            }).ToList();
            
            ViewBag.AllOrders = System.Text.Json.JsonSerializer.Serialize(allOrderData);
            return View(viewModel);
        }

        [HttpGet]
        public IActionResult GetShipperIncomeConfig()
        {
            return Json(ShipperIncomeConfig.Load());
        }

        [HttpPost]
        public IActionResult SaveShipperIncomeConfig([FromBody] ShipperIncomeConfig modifiedConfig)
        {
            var config = ShipperIncomeConfig.Load();
            config.BaseIncome = modifiedConfig.BaseIncome;
            config.BaseDistance = modifiedConfig.BaseDistance;
            config.ExtraFeePerKm = modifiedConfig.ExtraFeePerKm;
            config.Save();
            return Json(new { success = true, message = "Đã lưu cấu hình thu nhập Shipper thành công!" });
        }

        public async Task<IActionResult> ShipperOrders(string id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null || user.Role != UserRole.Shipper) return NotFound();

            var orders = await _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Where(o => o.ShipperId == id)
                .OrderBy(o => o.OrderCode)
                .ToListAsync();

            ViewBag.ShipperName = user.FullName;
            ViewBag.ShipperId = id;

            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderDetailJson(int id)
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Shipper)
                .Include(o => o.Store)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

var result = new {
                id = order.Id,
                code = order.OrderCode,
                customer = order.User?.FullName,
                merchant = order.Store?.Name,
                merchantAddr = order.Store?.Address,
                deliveryAddr = order.DeliveryAddress,
                totalPrice = (order.TotalPrice + order.ShippingFee).ToString("N0"),
                status = order.Status.ToString(),
                time1 = order.CreatedAt.ToString("HH:mm - dd/MM"),
                time2 = order.AcceptedAt?.ToString("HH:mm - dd/MM") ?? "--:--",
                time3 = order.PickedUpAt?.ToString("HH:mm - dd/MM") ?? "--:--",
                time4 = (order.Status == OrderStatus.Completed ? order.CompletedAt : order.CancelledAt)?.ToString("HH:mm - dd/MM") ?? "--:--",
                shipperName = order.Shipper?.FullName ?? "",
                shipperCode = (order.Shipper != null && order.Shipper.UserName != null && order.Shipper.UserName.Contains("shipper")) 
                    ? "SP-" + System.Text.RegularExpressions.Regex.Match(order.Shipper.UserName!, @"\d+").Value.PadLeft(3, '0') 
                    : (order.ShipperId != null ? order.ShipperId!.Substring(0, 8).ToUpper() : ""),
                note = order.Note,
                items = order.OrderItems.Select(oi => new {
                    name = oi.MenuItem?.Name,
                    qty = oi.Quantity,
                    price = oi.Price.ToString("N0")
                })
            };
            return Json(result);
        }

        // --- DELIVERY SERVICES MANAGEMENT ---
        public async Task<IActionResult> Services()
        {
            var services = await _context.DeliveryServices
                .OrderByDescending(s => s.IsPinned)
                .ThenBy(s => s.Id)
                .ToListAsync();
            return View(services);
        }

        [HttpGet]
        public async Task<IActionResult> GetDeliveryService(int id)
        {
            var service = await _context.DeliveryServices.FindAsync(id);
            if (service == null) return NotFound();
            return Json(service);
        }

        [HttpPost]
        public async Task<IActionResult> SaveDeliveryService([FromBody] DeliveryService model)
        {
            try
            {
                if (model.Id == 0)
                {
                    model.Icon = GetHardcodedIcon(model.Name);
                    _context.DeliveryServices.Add(model);
                }
                else
                {
                    var existing = await _context.DeliveryServices.FindAsync(model.Id);
                    if (existing == null) return NotFound();

                    existing.Name = model.Name;
                    existing.Description = model.Description;
                    existing.VehicleType = model.VehicleType;
                    existing.ServiceType = model.ServiceType;
                    existing.IsActive = model.IsActive;
                    existing.MaxWeightKg = model.MaxWeightKg;
                    existing.EstimatedMinutesPerKm = model.EstimatedMinutesPerKm;
                    existing.BaseFee = model.BaseFee;
                    existing.FeePerKm = model.FeePerKm;
                    existing.ExtraFeePerKg = model.ExtraFeePerKg;

                    // Hardcode icon based on name for consistency
                    existing.Icon = GetHardcodedIcon(model.Name);
                    
                    _context.DeliveryServices.Update(existing);
                }
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> TogglePinService([FromBody] int id)
        {
            var service = await _context.DeliveryServices.FindAsync(id);
            if (service == null) return NotFound(new { success = false, message = "Dịch vụ không tồn tại" });
            
            service.IsPinned = !service.IsPinned;
            _context.DeliveryServices.Update(service);
            await _context.SaveChangesAsync();
            return Json(new { success = true, isPinned = service.IsPinned });
        }

        private string GetHardcodedIcon(string name)
        {
            if (string.IsNullOrEmpty(name)) return "fa-box";
            var n = name.ToLower();
            if (n.Contains("siêu tốc") || n.Contains("rocket")) return "fa-rocket";
            if (n.Contains("nhanh") || n.Contains("bolt")) return "fa-bolt";
            if (n.Contains("ngày") || n.Contains("clock")) return "fa-clock";
            if (n.Contains("cồng kềnh") || n.Contains("truck")) return "fa-truck";
            if (n.Contains("tài liệu") || n.Contains("file")) return "fa-file-lines";
            return "fa-box";
        }

        [HttpPost]
        public async Task<IActionResult> DeleteDeliveryService([FromBody] int id)
        {
            var existing = await _context.DeliveryServices.FindAsync(id);
            if (existing == null) return NotFound();

            existing.IsActive = false;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // --- VOUCHERS MANAGEMENT ---
        [HttpGet]
        public async Task<IActionResult> Vouchers(string search, string filter = "all")
        {
            var query = _context.Vouchers.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.ToLower();
                query = query.Where(v => v.Code.ToLower().Contains(s) || v.ProgramName.ToLower().Contains(s));
            }

            var allVouchers = await query.ToListAsync();

            ViewBag.TotalCount = allVouchers.Count;
            ViewBag.ActiveCount = allVouchers.Count(v => v.IsValid);
            ViewBag.ExpiredCount = allVouchers.Count(v => !v.IsValid);

            if (filter == "active") query = query.Where(v => v.IsActive && DateTime.Now >= v.StartDate && DateTime.Now <= v.EndDate && v.UsedCount < v.MaxUsageCount);
            if (filter == "expired") query = query.Where(v => !(v.IsActive && DateTime.Now >= v.StartDate && DateTime.Now <= v.EndDate && v.UsedCount < v.MaxUsageCount));

            var results = await query
                .OrderByDescending(v => v.IsActive)
                .ThenBy(v => v.EndDate)
                .ToListAsync();

            ViewBag.SearchTerm = search;
            ViewBag.CurrentFilter = filter;

            return View(results);
        }

        [HttpPost("Admin/SoftDeleteVoucher/{id}")]
        public async Task<IActionResult> SoftDeleteVoucher(int id)
        {
            Console.WriteLine($"[DEBUG] SoftDeleteVoucher called with id: {id}");
            try 
            {
                var voucher = await _context.Vouchers.FindAsync(id);
                if (voucher == null) {
                    Console.WriteLine("[DEBUG] Voucher not found");
                    return Json(new { success = false, message = "Voucher không tồn tại" });
                }

                Console.WriteLine("[DEBUG] Marking voucher inactive and expired...");
                
                // Cập nhật Entity
                voucher.IsActive = false; 
                voucher.EndDate = DateTime.Now.AddSeconds(-1); 
                
                // Tạo thông báo do voucher bị vô hiệu hóa (vì IsActive = false sẽ bị bỏ qua trong Sync)
                await CreateVoucherNotificationIfNotExist(voucher, NotificationType.VoucherExpired, 
                    "Voucher đã hết hạn", $"Mã '{voucher.Code}' đã bị vô hiệu hóa.");
                
                await _context.SaveChangesAsync();
                
                // Cập nhật mạnh mẽ xuống SQL Server (liên kết chặt chẽ)
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Vouchers SET IsActive = 0, EndDate = GETDATE() WHERE Id = {id}");

                Console.WriteLine("[DEBUG] Successfully saved inactive voucher");
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
            return Json(new { success = false, message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        // Temporary endpoint to fix existing orders
        // Temporary endpoint to fix existing orders
        [AllowAnonymous]
        [HttpGet("fix-orders")]
        public async Task<IActionResult> FixOrders()
        {
            var standardService = await _context.DeliveryServices.FirstOrDefaultAsync(s => s.Name == "Tiêu chuẩn");
            if (standardService == null)
            {
                standardService = await _context.DeliveryServices.FirstOrDefaultAsync();
            }

            var orders = await _context.Orders.Include(o => o.OrderItems).ToListAsync();
            foreach(var order in orders)
            {
                if (standardService != null)
                {
                    order.ServiceId = standardService.Id;
                    
                    var config = ShipperIncomeConfig.Load();
                    decimal extraFee = (decimal)Math.Ceiling(Math.Max(order.Distance - config.BaseDistance, 0)) * config.ExtraFeePerKm;
                    order.ShipperIncome = config.BaseIncome + extraFee;

                    // Fixed BaseFee and FeePerKm
                    decimal baseFee = standardService.BaseFee;
                    decimal feePerKm = standardService.FeePerKm;

                    order.Distance = Math.Round(order.Distance, 1);
                    decimal shippingFee = baseFee + ((decimal)order.Distance * feePerKm);
                    
                    decimal itemsTotal = order.OrderItems?.Sum(i => i.Price * i.Quantity) ?? 0m;
                    order.ShippingFee = shippingFee;
                    order.TotalPrice = itemsTotal + shippingFee;
                }
            }

            await _context.SaveChangesAsync();
            return Ok($"Fixed {orders.Count} orders.");
        }

        [HttpPost("Admin/RestoreVoucher/{id}")]
        public async Task<IActionResult> RestoreVoucher(int id)
        {
            Console.WriteLine($"[DEBUG] RestoreVoucher called with id: {id}");
            try 
            {
                var voucher = await _context.Vouchers.FindAsync(id);
                if (voucher == null) {
                    Console.WriteLine("[DEBUG] Voucher not found");
                    return Json(new { success = false, message = "Voucher không tồn tại" });
                }

                Console.WriteLine("[DEBUG] Restoring voucher to active state...");
                var now = DateTime.Now;
                var oneMonthLater = now.AddMonths(1);

                // Khôi phục thì xóa các thông báo liên quan đến hết hạn/sắp hết hạn
                var oldNotifs = await _context.Notifications
                    .Where(n => n.RelatedId == id.ToString() && 
                        (n.Type == NotificationType.VoucherExpired || 
                         n.Type == NotificationType.VoucherExpiring || 
                         n.Type == NotificationType.VoucherLimitReached || 
                         n.Type == NotificationType.VoucherLimitSoon))
                    .ToListAsync();
                if (oldNotifs.Any()) {
                    _context.Notifications.RemoveRange(oldNotifs);
                }

                // Kiểm tra hết hạn do lượt dùng (vd: UsedCount >= MaxUsageCount)
                bool isExpiredByUsage = voucher.MaxUsageCount > 0 && voucher.UsedCount >= voucher.MaxUsageCount;

                if (isExpiredByUsage) {
                    voucher.UsedCount = 0;
                }

                // Cập nhật Entity
                voucher.IsActive = true; 
                voucher.StartDate = now;
                voucher.EndDate = oneMonthLater; 
                await _context.SaveChangesAsync();
                
                // Cập nhật mạnh mẽ xuống SQL Server (liên kết chặt chẽ) - Tương thích EF Core ExecuteSqlRawAsync
                string sql = $"UPDATE Vouchers SET IsActive = 1, StartDate = GETDATE(), EndDate = DATEADD(month, 1, GETDATE())";
                if (isExpiredByUsage) {
                    sql += ", UsedCount = 0";
                }
                sql += $" WHERE Id = {id}";
                
                await _context.Database.ExecuteSqlRawAsync(sql);

                Console.WriteLine("[DEBUG] Successfully restored voucher");
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Exception in RestoreVoucher: {ex.Message}");
                return Json(new { success = false, message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateVoucher([FromBody] Voucher voucher)
        {
            if (voucher == null || string.IsNullOrWhiteSpace(voucher.Code))
            {
                return Json(new { success = false, message = "Dữ liệu không hợp lệ" });
            }

            // Kiểm tra trùng mã (SQL Server tự động case-insensitive)
            var exists = await _context.Vouchers.AnyAsync(v => v.Code == voucher.Code && v.Id != voucher.Id);
            if (exists)
            {
                return Json(new { success = false, message = $"Mã Voucher '{voucher.Code}' đã tồn tại!" });
            }

            if (voucher.Id > 0)
            {
                var existing = await _context.Vouchers.FindAsync(voucher.Id);
                if (existing == null) return Json(new { success = false, message = "Voucher không tồn tại!" });
                
                // Dọn dẹp: Nếu cập nhật voucher trở lại trạng thái hoạt động thì xóa thẻ cũ
                if (voucher.IsActive && voucher.EndDate > DateTime.Now)
                {
                    var oldNotifs = await _context.Notifications
                        .Where(n => n.RelatedId == voucher.Id.ToString() && 
                            (n.Type == NotificationType.VoucherExpired || n.Type == NotificationType.VoucherExpiring || 
                             n.Type == NotificationType.VoucherLimitReached || n.Type == NotificationType.VoucherLimitSoon))
                        .ToListAsync();
                    if (oldNotifs.Any()) _context.Notifications.RemoveRange(oldNotifs);
                }

                existing.Code = voucher.Code;
                existing.ProgramName = voucher.ProgramName;
                existing.Description = voucher.Description;
                existing.AppliesToShipping = voucher.AppliesToShipping;
                existing.IsPercentage = voucher.IsPercentage;
                existing.DiscountValue = voucher.DiscountValue;
                existing.MaxDiscountValue = voucher.MaxDiscountValue;
                existing.MinOrderValue = voucher.MinOrderValue;
                existing.MaxUsageCount = voucher.MaxUsageCount;
                existing.LimitPerUser = voucher.LimitPerUser;
                existing.StartDate = voucher.StartDate;
                existing.EndDate = voucher.EndDate;
                
                _context.Vouchers.Update(existing);
            }
            else
            {
                voucher.IsActive = true;
                voucher.UsedCount = 0;
                _context.Vouchers.Add(voucher);
            }
            
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        [HttpGet]
        public async Task<IActionResult> GetShipperRegistrationJson(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || user.Role != UserRole.Shipper) return NotFound();

            return Json(new {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                vehicle = user.Vehicle ?? "Chưa cập nhật",
                vehicleType = user.VehicleType ?? "Chưa cập nhật",
                citizenId = user.CitizenId ?? "Chưa có",
                avatarUrl = user.AvatarUrl,
                citizenIdFrontUrl = user.CitizenIdFrontImageUrl,
                citizenIdBackUrl = user.CitizenIdBackImageUrl,
                driverLicenseUrl = user.DriverLicenseImageUrl,
                isActive = user.IsActive,
                isApproved = user.IsApproved,
                isAlreadyApproved = user.IsApproved
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetStoreRegistrationJson(string id)
        {
            if (!int.TryParse(id, out int storeId)) return NotFound();
            var store = await _context.Stores
                .Include(s => s.Owner)
                .FirstOrDefaultAsync(s => s.Id == storeId);
            
            if (store == null) return NotFound();

            return Json(new {
                id = store.Id,
                name = store.Name,
                address = store.Address,
                avatarUrl = store.ImageUrl,
                ownerFullName = store.Owner?.FullName ?? "Chưa có",
                ownerEmail = store.Owner?.Email ?? "Chưa có",
                ownerPhone = store.Owner?.PhoneNumber ?? "Chưa có",
                ownerAvatarUrl = store.Owner?.AvatarUrl ?? "/images/default-avatar.png",
                isOpen = store.IsOpen,
                isLocked = store.ActivityState != StoreActivityState.Active,
                isAlreadyActive = store.IsOpen && store.ActivityState == StoreActivityState.Active
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetPartnerRegistrationJson(string id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || user.Role != UserRole.Partner) return NotFound();
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.OwnerId == id);

            return Json(new {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                avatarUrl = user.AvatarUrl,
                shopAvatarUrl = user.ShopAvatarUrl,
                storeName = store?.Name ?? "Chưa có",
                storeAddress = store?.Address ?? "Chưa có",
                isActive = user.IsActive,
                isApproved = user.IsApproved,
                isAlreadyApproved = user.IsApproved
            });
        }
    }

    [Authorize(Roles = "Partner")]
    public class PartnerNotificationController : Controller
    {
        private readonly Data.ApplicationDbContext _context;
        public PartnerNotificationController(Data.ApplicationDbContext context) => _context = context;



        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var notifications = await _context.Notifications
                .Where(n => n.RecipientId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(20)
                .ToListAsync();

            var unreadCount = await _context.Notifications.CountAsync(n => n.RecipientId == userId && !n.IsRead);

            return Json(new { success = true, notifications, unreadCount });
        }

        [HttpPost]
        public async Task<IActionResult> MarkNotificationAsRead(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientId == userId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        [HttpPost]
        public async Task<IActionResult> MarkAllNotificationsAsRead()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var unread = await _context.Notifications.Where(n => n.RecipientId == userId && !n.IsRead).ToListAsync();
            foreach (var n in unread) n.IsRead = true;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        public async Task<IActionResult> Index(int? storeId, string timeRange = "7days")
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");


            var userStores = await _context.Stores
                .Where(s => s.OwnerId == userId)
                .OrderBy(s => s.Name)
                .ToListAsync();

            ViewBag.Stores = userStores;
            ViewBag.SelectedStore = storeId;
            ViewBag.SelectedTimeRange = timeRange;

            var storeIds = userStores.Select(s => s.Id).ToList();
            if (storeId.HasValue) {
                storeIds = new List<int> { storeId.Value };
            }

            var query = _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Where(o => o.StoreId.HasValue && storeIds.Contains(o.StoreId.Value))
                .AsQueryable();

            var now = DateTime.UtcNow;
            if (timeRange == "today") {
                query = query.Where(o => o.CreatedAt >= now.Date);
            } else if (timeRange == "7days") {
                query = query.Where(o => o.CreatedAt >= now.AddDays(-7));
            } else if (timeRange == "1month") {
                query = query.Where(o => o.CreatedAt >= now.AddMonths(-1));
            } else if (timeRange == "1year") {
                query = query.Where(o => o.CreatedAt >= now.AddYears(-1));
            }

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();

            var totalOrders = orders.Count;
            var completedOrders = orders.Where(o => o.Status == OrderStatus.Completed).ToList();
            var totalRevenue = completedOrders.Sum(o => o.TotalPrice);

            // Doanh thu theo từng chi nhánh
            var revenueByBranch = completedOrders
                .GroupBy(o => o.StoreId)
                .Select(g => new { 
                    StoreName = g.First().Store?.Name ?? "Unknown", 
                    Revenue = g.Sum(o => o.TotalPrice) 
                })
                .OrderByDescending(x => x.Revenue)
                .ToList();

            // Món ăn bán chạy theo chi nhánh (khi chọn 1 chi nhánh)
            if (storeId.HasValue) {
                var topItems = completedOrders
                    .SelectMany(o => o.OrderItems)
                    .GroupBy(oi => oi.MenuItemId)
                    .Select(g => new {
                        ItemName = g.First().MenuItem?.Name ?? "Unknown",
                        Quantity = g.Sum(oi => oi.Quantity)
                    })
                    .OrderByDescending(x => x.Quantity)
                    .ToList();
                ViewBag.TopItems = topItems;
            }

            // Trạng thái đơn hàng
            var statusStats = orders
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                .ToList();

            // Dữ liệu biểu đồ
            var chartData = orders
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new {
                    Date = g.Key,
                    Revenue = g.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalPrice),
                    Count = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToList();

            ViewBag.TotalOrders = totalOrders;
            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.RevenueByBranch = revenueByBranch;
            ViewBag.StatusStats = statusStats;
            ViewBag.ChartData = chartData;

            // Pass up to 50 orders so we can show 10, and expand to see the rest on the Dashboard
            var recentOrders = orders.Take(50).ToList();
            return View(recentOrders);
        }

        public async Task<IActionResult> Branches(string search, int page = 1)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var query = _context.Stores
                .Include(s => s.MenuItems)
                .Where(s => s.OwnerId == userId)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search)) {
                query = query.Where(s => (s.Name != null && s.Name.Contains(search)) || (s.Address != null && s.Address.Contains(search)));
            }

            int pageSize = 10;
            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var branches = await query
                .OrderBy(s => s.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var branchIds = branches.Select(b => b.Id).ToList();
            var reviews = await _context.Reviews
                .Include(r => r.Order)
                .Where(r => r.Order != null && r.Order.StoreId.HasValue && branchIds.Contains(r.Order.StoreId.Value))
                .ToListAsync();

            foreach (var branch in branches)
            {
                var storeReviews = reviews.Where(r => r.Order!.StoreId == branch.Id).ToList();
                if (storeReviews.Any())
                {
                    double totalScore = 0;
                    int totalCount = 0;
                    foreach (var r in storeReviews)
                    {
                        if (r.Type == ReviewType.Customer) {
                            totalScore += r.RatingMenu;
                            totalCount++;
                        } else if (r.Type == ReviewType.Shipper) {
                            totalScore += r.RatingMenu; // Shipper đánh giá quán dùng chung RatingMenu (hoặc lấy từ form ShipperRate)
                            totalCount++;
                        }
                    }
                    branch.Rating = totalCount > 0 ? Math.Round(totalScore / totalCount, 1) : 0.0;
                    branch.ReviewCount = totalCount;
                }
                else
                {
                    branch.Rating = 5.0; // Mặc định 5 sao khi chưa có đánh giá
                    branch.ReviewCount = 0;
                }
            }

            ViewBag.SearchTerm = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            var branchIdsStr = branches.Select(id => id.Id.ToString()).ToList();
            ViewBag.PendingStoreIds = await _context.Notifications
                .Where(n => n.Type == NotificationType.StoreRegistration && n.RelatedId != null && branchIdsStr.Contains(n.RelatedId))
                .Select(n => n.RelatedId)
                .ToListAsync();

            var partner = await _context.Users.FindAsync(userId);
            ViewBag.IsPartnerActive = partner?.IsActive ?? false;

            return View(branches);
        }

        [HttpPost]
        public async Task<IActionResult> AddBranch(string name, string address, string description, string? imageUrl)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Json(new { success = false, message = "Bạn cần đăng nhập." });

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            {
                return Json(new { success = false, message = "Tên và Địa chỉ không được để trống." });
            }

            var store = new Store
            {
                Name = name,
                Address = address,
                Description = description ?? "",
                ImageUrl = imageUrl,
                OwnerId = userId,
                IsMainBranch = false, // Not the main registered branch
                IsOpen = false,
                ActivityState = StoreActivityState.LockedByPartner,
                Rating = 5.0, // Default real rating
                ReviewCount = 0
            };

            _context.Stores.Add(store);
            await _context.SaveChangesAsync();

            // Gửi thông báo phê duyệt cho Admin
            var userName = User.Identity?.Name ?? "Đối tác";
            _context.Notifications.Add(new Notification
            {
                Title = "Yêu cầu mở Chi nhánh mới",
                Message = $"Đối tác {userName} vừa đăng ký thêm chi nhánh: {name}.",
                Type = NotificationType.StoreRegistration,
                RelatedId = store.Id.ToString(),
                CreatedAt = DateTime.UtcNow.AddHours(7),
                IsRead = false,
                TargetUrl = $"/Admin/Merchants?openPartnerId={userId}&highlightStoreId={store.Id}"
            });
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        public async Task<IActionResult> Orders(int? storeId, string search, string timeRange = "all", int page = 1)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var userStores = await _context.Stores
                .Where(s => s.OwnerId == userId)
                .ToListAsync();

            var userStoreIds = userStores.Select(s => s.Id).ToList();

            var query = _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Include(o => o.Shipper)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                .Where(o => o.StoreId.HasValue && userStoreIds.Contains(o.StoreId.Value))
                .AsQueryable();

            if (storeId.HasValue) {
                query = query.Where(o => o.StoreId == storeId.Value);
            }

            var now = DateTime.UtcNow;
            if (timeRange == "today") {
                query = query.Where(o => o.CreatedAt >= now.Date);
            } else if (timeRange == "7days") {
                query = query.Where(o => o.CreatedAt >= now.AddDays(-7));
            } else if (timeRange == "1month") {
                query = query.Where(o => o.CreatedAt >= now.AddMonths(-1));
            } else if (timeRange == "1year") {
                query = query.Where(o => o.CreatedAt >= now.AddYears(-1));
            }

            if (!string.IsNullOrEmpty(search)) {
                query = query.Where(o => o.OrderCode.Contains(search) || (o.User != null && o.User.FullName != null && o.User.FullName.Contains(search)));
            }

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();

            var partner = await _context.Users.FindAsync(userId);
            ViewBag.PartnerName = partner?.FullName ?? "Partner";
            ViewBag.StoreId = storeId;
            
            if (storeId.HasValue) {
                var selectedStore = userStores.FirstOrDefault(s => s.Id == storeId.Value);
                if (selectedStore != null) {
                    ViewBag.StoreName = selectedStore.Name;
                }
            }
            
            return View(orders);
        }

        public async Task<IActionResult> StoreDetail(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var store = await _context.Stores
                .Include(s => s.MenuItems)
                .FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId);
            
            if (store == null) return NotFound();

            // Calculate Item Sales Counts (Only Completed Orders)
            var itemSalesCounts = await _context.OrderItems
                .Include(oi => oi.Order)
                .Where(oi => oi.Order != null && oi.Order.StoreId == id && oi.Order.Status == OrderStatus.Completed)
                .GroupBy(oi => oi.MenuItemId)
                .Select(g => new { MenuItemId = g.Key, Count = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.MenuItemId, x => x.Count);
            
            ViewBag.ItemSalesCounts = itemSalesCounts;

            var topItems = itemSalesCounts
                .OrderByDescending(x => x.Value)
                .Take(3)
                .ToDictionary(x => x.Key, x => x.Value);
            ViewBag.TopItems = topItems;

            // Calculate Real Rating
            var reviews = await _context.Reviews
                .Include(r => r.Order)
                .ThenInclude(o => o!.OrderItems)
                .Where(r => r.Order != null && r.Order.StoreId == id)
                .ToListAsync();

            var itemRatings = reviews
                .Where(r => r.Order!.Status == OrderStatus.Completed && r.Type == ReviewType.Customer)
                .SelectMany(r => r.Order!.OrderItems.Select(oi => new { oi.MenuItemId, r.RatingMenu }))
                .GroupBy(x => x.MenuItemId)
                .ToDictionary(g => g.Key, g => (Count: g.Count(), Avg: Math.Round(g.Average(x => (double)x.RatingMenu), 1)));

            ViewBag.ItemRatings = itemRatings;

            if (reviews.Any())
            {
                double totalScore = 0;
                int totalCount = 0;

                foreach(var r in reviews)
                {
                    if (r.Type == ReviewType.Customer) {
                        totalScore += r.RatingMenu;
                        totalCount++;
                    } else if (r.Type == ReviewType.Shipper) {
                        totalScore += r.RatingMenu; // Thống nhất: Điểm chấm cho Quán đều lưu vào RatingMenu (Menu/Store)
                        totalCount++;
                    }
                }

                if (totalCount > 0) {
                    store.Rating = Math.Round(totalScore / totalCount, 1);
                    store.ReviewCount = totalCount;
                } else {
                    store.Rating = 5.0; // Mặc định 5 sao khi chưa có đánh giá
                    store.ReviewCount = 0;
                }
            } else {
                store.Rating = 5.0; // Mặc định 5 sao khi chưa có đánh giá
                store.ReviewCount = 0;
            }

            var partner = await _context.Users.FindAsync(userId);
            ViewBag.IsPartnerActive = partner?.IsActive ?? false;

            return View("StoreDetail", store);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStoreStatus(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var store = await _context.Stores
                .Include(s => s.Owner)
                .FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId);
            
            if (store == null) return Json(new { success = false, message = "Không tìm thấy chi nhánh." });
            
            // Double check: Either individual store lock OR overall partner account lock
            var isPending = await _context.Notifications.AnyAsync(n => n.Type == NotificationType.StoreRegistration && n.RelatedId == store.Id.ToString());
            if (isPending) {
                return Json(new { success = false, message = "Chi nhánh này đang chờ phê duyệt. Xin vui lòng chờ hệ thống xử lý." });
            }
            if (store.ActivityState != StoreActivityState.Active || (store.Owner != null && !store.Owner.IsActive)) {
                return Json(new { success = false, message = "Chi nhánh này đang bị khóa. Xin vui lòng liên hệ tổng đài để mở khóa." });
            }

            store.IsOpen = !store.IsOpen;
            await _context.SaveChangesAsync();
            
            return Json(new { success = true, isOpen = store.IsOpen });
        }

        [HttpPost]
        public async Task<IActionResult> LockStoreByPartner(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId);
            
            if (store == null) return Json(new { success = false, message = "Không tìm thấy chi nhánh." });
            
            if (store.ActivityState == StoreActivityState.LockedByBranch || store.ActivityState == StoreActivityState.Active) {
                store.ActivityState = StoreActivityState.LockedByPartner;
                store.IsOpen = false;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Không thể khóa chi nhánh ở trạng thái này." });
        }

        [HttpPost]
        public async Task<IActionResult> UnlockStoreByPartner(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId);
            
            if (store == null) return Json(new { success = false, message = "Không tìm thấy chi nhánh." });
            
            if (store.ActivityState == StoreActivityState.LockedByPartner) {
                store.ActivityState = StoreActivityState.Active;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Chi nhánh không bị khóa bởi bạn." });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStore(int id, string name, string address, string? imageUrl, IFormFile? imageFile)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId);
            
            if (store == null) return Json(new { success = false, message = "Không tìm thấy chi nhánh." });

            store.Name = name;
            store.Address = address;

            if (imageFile != null)
            {
                var fileName = $"store_{id}_{DateTime.Now.Ticks}{Path.GetExtension(imageFile.FileName)}";
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/stores", fileName);
                
                var dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }
                store.ImageUrl = $"/uploads/stores/{fileName}";
            }
            else if (!string.IsNullOrEmpty(imageUrl))
            {
                store.ImageUrl = imageUrl;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> AddCustomCategory(int storeId, string categoryName)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == storeId && s.OwnerId == userId);
                
                if (store == null) return Json(new { success = false, message = "Không tìm thấy chi nhánh hoặc bạn không có quyền." });

                if (!string.IsNullOrWhiteSpace(categoryName))
                {
                    var existingCats = (store.CustomCategories ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (!existingCats.Contains(categoryName))
                    {
                        existingCats.Add(categoryName);
                        store.CustomCategories = string.Join(",", existingCats);
                        await _context.SaveChangesAsync();
                    }
                }
                
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message + (ex.InnerException != null ? " " + ex.InnerException.Message : "") });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteCustomCategory(int storeId, string categoryName)
        {
            try
            {
                if (categoryName == "Khác") return Json(new { success = false, message = "Không thể xóa danh mục này." });

                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var store = await _context.Stores.Include(s => s.MenuItems).FirstOrDefaultAsync(s => s.Id == storeId && s.OwnerId == userId);
                
                if (store == null) return Json(new { success = false, message = "Không tìm thấy chi nhánh hoặc bạn không có quyền." });

                // Move items to 'Khác'
                var itemsToMove = store.MenuItems.Where(m => m.Category == categoryName).ToList();
                foreach (var item in itemsToMove)
                {
                    item.Category = "Khác";
                }

                // Remove from custom categories list
                var existingCats = (store.CustomCategories ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (existingCats.Contains(categoryName))
                {
                    existingCats.Remove(categoryName);
                }
                
                // If it's a default category, mark it as deleted with '-' prefix
                var defaultCategories = new[] { "Món chính", "Món thêm", "Đồ uống", "Tráng miệng" };
                if (defaultCategories.Contains(categoryName) && !existingCats.Contains("-" + categoryName))
                {
                    existingCats.Add("-" + categoryName);
                }
                
                store.CustomCategories = string.Join(",", existingCats);

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveMenuItem(int id, int storeId, string name, string description, decimal price, string category, bool isAvailable, string? imageUrl, IFormFile? imageFile)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == storeId && s.OwnerId == userId);
            
            if (store == null) return Json(new { success = false, message = "Không tìm thấy chi nhánh hoặc bạn không có quyền." });

            MenuItem? item;
            if (id == 0)
            {
                item = new MenuItem { StoreId = storeId };
                _context.MenuItems.Add(item);
            }
            else
            {
                item = await _context.MenuItems.FirstOrDefaultAsync(m => m.Id == id && m.StoreId == storeId);
                if (item == null) return Json(new { success = false, message = "Không tìm thấy món ăn." });
            }

            item.Name = name;
            item.Description = description;
            item.Price = price;
            item.Category = category;
            item.IsAvailable = isAvailable;

            if (imageFile != null)
            {
                var fileName = $"menu_{storeId}_{DateTime.Now.Ticks}{Path.GetExtension(imageFile.FileName)}";
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/menuitems", fileName);
                
                var dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }
                item.ImageUrl = $"/uploads/menuitems/{fileName}";
            }
            else if (!string.IsNullOrEmpty(imageUrl))
            {
                item.ImageUrl = imageUrl;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMenuItem(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var item = await _context.MenuItems
                .Include(m => m.Store)
                .FirstOrDefaultAsync(m => m.Id == id);
            
            if (item == null || item.Store?.OwnerId != userId)
                return Json(new { success = false, message = "Không tìm thấy món ăn hoặc bạn không có quyền." });

            // Check if item has been ordered
            var hasOrders = await _context.OrderItems.AnyAsync(oi => oi.MenuItemId == id);
            if (hasOrders)
            {
                return Json(new { success = false, message = "Món ăn này đã có trong lịch sử đơn hàng, không thể xóa vĩnh viễn. Bạn có thể chọn 'Hết hàng' thay thế." });
            }

            _context.MenuItems.Remove(item);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }

    [Authorize(Roles = "Shipper")]
    public class ShipperController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly DeliveryHubWeb.Services.IRouteOptimizationService _routeService;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<DeliveryHubWeb.Hubs.OrderHub> _hubContext;

        public ShipperController(ApplicationDbContext context, DeliveryHubWeb.Services.IRouteOptimizationService routeService, Microsoft.AspNetCore.SignalR.IHubContext<DeliveryHubWeb.Hubs.OrderHub> hubContext)
        {
            _context = context;
            _routeService = routeService;
            _hubContext = hubContext;
        }

        [Authorize(Roles = "Shipper")]
        public async Task<IActionResult> Index()
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return RedirectToAction("Login", "Account");

            var shipperId = user.Id;

            // All orders assigned to this shipper
            var myOrders = await _context.Orders
                .Where(o => o.ShipperId == shipperId)
                .ToListAsync();

            var completedOrders = myOrders.Where(o => o.Status == OrderStatus.Completed).ToList();
            var totalCompleted = completedOrders.Count;
            var totalIncome = completedOrders.Sum(o => o.ShipperIncome);
            var totalOrders = myOrders.Count;
            var successRate = totalOrders > 0 ? Math.Round((double)totalCompleted / totalOrders * 100, 1) : 100.0;

            // Today stats
            var today = DateTime.Now.Date;
            var todayOrders = completedOrders.Where(o => o.CompletedAt?.Date == today).ToList();
            var todayIncome = todayOrders.Sum(o => o.ShipperIncome);
            var todayCompleted = todayOrders.Count;

            // Average Shipper Rating from Reviews
            var shipperRatings = await _context.Reviews
                .Where(r => r.Order != null && r.Order.ShipperId == shipperId)
                .Select(r => r.RatingShipper)
                .ToListAsync();
            var avgRating = shipperRatings.Any() ? Math.Round(shipperRatings.Average(), 1) : 5.0;
            var totalReviews = shipperRatings.Count;

            // Available orders to pick up
            var availableOrders = await _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Where(o => o.Status == OrderStatus.SearchingShipper)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            // Currently delivering orders
            var deliveringOrders = await _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Where(o => o.ShipperId == shipperId && (o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.Delivering))
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.ShipperUser = user;
            ViewBag.TotalCompleted = totalCompleted;
            ViewBag.TotalIncome = totalIncome;
            ViewBag.AvgRating = avgRating;
            ViewBag.TotalReviews = totalReviews;
            ViewBag.SuccessRate = successRate;
            ViewBag.TodayIncome = todayIncome;
            ViewBag.TodayCompleted = todayCompleted;
            ViewBag.DeliveringOrders = deliveringOrders;
            ViewBag.IsOnline = user.IsActive;

            return View(availableOrders);
        }

        [HttpGet]
        public async Task<IActionResult> GetShipperChartData(string id, int days = 7)
        {
            var query = _context.Orders.Where(o => o.ShipperId == id);
            var now = DateTime.UtcNow;
            
            if (days > 0) {
                var startDate = now.Date.AddDays(-days + 1);
                query = query.Where(o => o.CreatedAt >= startDate);
            }

            var orders = await query.ToListAsync();

            // Setup date range
            var result = new List<object>();
            var limit = days > 0 ? days : 30; // fallback to 30 points if all time
            var start = days > 0 ? now.Date.AddDays(-days + 1) : (orders.Any() ? orders.Min(o => o.CreatedAt.Date) : now.Date);
            var totalDays = (now.Date - start).Days + 1;

            for (int i = 0; i < totalDays; i++)
            {
                var d = start.AddDays(i);
                var dailyOrders = orders.Where(o => o.CreatedAt.Date == d).ToList();
                result.Add(new {
                    Date = d.ToString("yyyy-MM-dd"),
                    DisplayDate = d.ToString("dd/MM"),
                    accepted = dailyOrders.Count(o => o.Status == OrderStatus.Accepted || o.Status == OrderStatus.SearchingShipper || o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing),
                    delivering = dailyOrders.Count(o => o.Status == OrderStatus.Delivering),
                    completed = dailyOrders.Count(o => o.Status == OrderStatus.Completed),
                    failed = dailyOrders.Count(o => o.Status == OrderStatus.Failed || o.Status == OrderStatus.Cancelled)
                });
            }

            return Json(result);
        }

        [Authorize(Roles = "Shipper")]
        public async Task<IActionResult> AvailableOrders()
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return RedirectToAction("Login", "Account");

            List<Order> availableOrders = new List<Order>();
            if (user.IsActive)
            {
                availableOrders = await _context.Orders
                    .Include(o => o.Store)
                    .Include(o => o.User)
                    .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                    .Where(o => o.Status == OrderStatus.SearchingShipper)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();
            }

            ViewBag.ShipperUser = user;
            ViewBag.IsOnline = user.IsActive;
            ViewData["Title"] = "Đơn mới nổ";
            return View(availableOrders);
        }

        // ===== GOM ĐƠN (BATCH ORDERS) =====

        [Authorize(Roles = "Shipper")]
        public async Task<IActionResult> BatchOrders()
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return RedirectToAction("Login", "Account");

            List<Order> availableOrders = new List<Order>();
            if (user.IsActive)
            {
                availableOrders = await _context.Orders
                    .Include(o => o.Store)
                    .Include(o => o.User)
                    .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                    .Where(o => o.Status == OrderStatus.SearchingShipper)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();
            }

            // Lấy danh sách batch đang active của shipper
            var activeBatches = await _context.BatchOrders
                .Include(b => b.Items).ThenInclude(bi => bi.Order).ThenInclude(o => o!.Store)
                .Where(b => b.ShipperId == user.Id && (b.Status == BatchOrderStatus.Created || b.Status == BatchOrderStatus.InProgress))
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            ViewBag.ShipperUser = user;
            ViewBag.IsOnline = user.IsActive;
            ViewBag.ActiveBatches = activeBatches;
            ViewData["Title"] = "Gom đơn";
            return View(availableOrders);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateBatch([FromBody] CreateBatchRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return Json(new { success = false, message = "Unauthorized" });

            if (request.OrderCodes == null || request.OrderCodes.Count < 2)
                return Json(new { success = false, message = "Vui lòng chọn ít nhất 2 đơn để gom." });

            // Lấy các đơn hàng
            var orders = await _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Where(o => request.OrderCodes.Contains(o.OrderCode) && o.Status == OrderStatus.SearchingShipper)
                .ToListAsync();

            if (orders.Count < 2)
                return Json(new { success = false, message = "Không đủ đơn hàng hợp lệ để gom." });
            
            if (orders.Count > 10)
                return Json(new { success = false, message = "Theo quy định, bạn không được phép gom quá 10 đơn một lúc để đảm bảo đồ ăn không bị nguội lạnh." });

            // Ràng buộc khoảng cách không gian (Không gom đơn có cửa hàng cách xa nhau quá 15km cho bản demo)
            double maxDist = 0;
            for (int i = 0; i < orders.Count; i++) {
                for (int j = i + 1; j < orders.Count; j++) {
                    var s1 = orders[i].Store;
                    var s2 = orders[j].Store;
                    if (s1 != null && s2 != null && s1.Latitude != 0 && s2.Latitude != 0) {
                        var R = 6371d; // Bán kính trái đất
                        var dLat = (s2.Latitude - s1.Latitude) * Math.PI / 180.0;
                        var dLon = (s2.Longitude - s1.Longitude) * Math.PI / 180.0;
                        var a = Math.Sin(dLat/2)*Math.Sin(dLat/2) + Math.Cos(s1.Latitude*Math.PI/180.0)*Math.Cos(s2.Latitude*Math.PI/180.0)*Math.Sin(dLon/2)*Math.Sin(dLon/2);
                        var dist = R * (2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a)));
                        if (dist > maxDist) maxDist = dist;
                    }
                }
            }

            // Tạm thời bỏ ràng buộc khoảng cách để Demo dễ gom đơn hơn.
            // if (maxDist > 15) {
            //    return Json(new { success = false, message = $"Các quán ăn cách nhau quá xa ({Math.Round(maxDist, 1)}km). Vui lòng chọn quán gần nhau (trong bán kính 15km) để tối ưu thời gian." });
            // }

            // Chuẩn bị dữ liệu cho route optimization
            var shipperLoc = (lat: user.Latitude ?? 10.762622, lng: user.Longitude ?? 106.660172);
            
            var pickupPoints = orders
                .Where(o => o.Store != null && o.Store.Latitude != 0 && o.Store.Longitude != 0)
                .Select(o => (orderId: o.Id, storeName: o.Store!.Name, lat: o.Store.Latitude, lng: o.Store.Longitude))
                .ToList();

            if (pickupPoints.Count < 2)
                return Json(new { success = false, message = "Không đủ cửa hàng có tọa độ để tối ưu lộ trình." });

            // Điểm giao: lấy từ request hoặc từ đơn đầu tiên
            var deliveryLat = request.DeliveryLat != 0 ? request.DeliveryLat : orders.First().DeliveryLatitude;
            var deliveryLng = request.DeliveryLng != 0 ? request.DeliveryLng : orders.First().DeliveryLongitude;
            var deliveryAddr = !string.IsNullOrEmpty(request.DeliveryAddress) ? request.DeliveryAddress : orders.First().DeliveryAddress;

            // Nếu delivery location = 0, dùng vị trí shipper
            if (deliveryLat == 0 || deliveryLng == 0)
            {
                deliveryLat = shipperLoc.lat;
                deliveryLng = shipperLoc.lng;
            }

            var deliveryLoc = (lat: deliveryLat, lng: deliveryLng);

            // Gọi OpenRouteService Optimization
            var routeResult = await _routeService.OptimizeRoute(shipperLoc, pickupPoints, deliveryLoc);

            if (!routeResult.Success)
                return Json(new { success = false, message = routeResult.ErrorMessage ?? "Lỗi tối ưu lộ trình." });

            // Tạo BatchOrder
            var batch = new BatchOrder
            {
                BatchCode = $"GD-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                ShipperId = user.Id,
                Status = BatchOrderStatus.Created,
                CreatedAt = DateTime.Now,
                TotalDistance = routeResult.TotalDistanceKm,
                EstimatedMinutes = routeResult.EstimatedMinutes,
                DeliveryAddress = deliveryAddr,
                DeliveryLatitude = deliveryLat,
                DeliveryLongitude = deliveryLng,
                RouteGeometry = routeResult.RouteGeoJson,
                OptimizedRouteJson = System.Text.Json.JsonSerializer.Serialize(routeResult.Stops)
            };

            _context.BatchOrders.Add(batch);
            await _context.SaveChangesAsync();

            // Tạo BatchOrderItems + cập nhật orders
            foreach (var stop in routeResult.Stops)
            {
                var order = orders.FirstOrDefault(o => o.Id == stop.OrderId);
                if (order != null)
                {
                    order.ShipperId = user.Id;
                    order.Status = OrderStatus.Accepted;
                    order.AcceptedAt = DateTime.Now;

                    _context.BatchOrderItems.Add(new BatchOrderItem
                    {
                        BatchOrderId = batch.Id,
                        OrderId = order.Id,
                        Sequence = stop.Sequence
                    });
                }
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                batchId = batch.Id,
                batchCode = batch.BatchCode,
                totalDistance = routeResult.TotalDistanceKm,
                estimatedMinutes = routeResult.EstimatedMinutes,
                stops = routeResult.Stops.Select(s => new { s.OrderId, s.StoreName, s.Sequence, s.Lat, s.Lng, s.DistanceFromPrev }),
                routeGeoJson = routeResult.RouteGeoJson
            });
        }

        [Authorize(Roles = "Shipper")]
        public async Task<IActionResult> BatchDetail(int id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return RedirectToAction("Login", "Account");

            var batch = await _context.BatchOrders
                .Include(b => b.Items).ThenInclude(bi => bi.Order).ThenInclude(o => o!.Store)
                .Include(b => b.Items).ThenInclude(bi => bi.Order).ThenInclude(o => o!.User)
                .Include(b => b.Items).ThenInclude(bi => bi.Order).ThenInclude(o => o!.OrderItems)
                .FirstOrDefaultAsync(b => b.Id == id && b.ShipperId == user.Id);

            if (batch == null) return NotFound();

            ViewBag.ShipperUser = user;
            ViewData["Title"] = $"Batch {batch.BatchCode}";
            return View(batch);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> PickupBatchItem([FromBody] PickupBatchItemRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return Json(new { success = false, message = "Unauthorized" });

            var item = await _context.BatchOrderItems
                .Include(bi => bi.BatchOrder)
                .Include(bi => bi.Order)
                .FirstOrDefaultAsync(bi => bi.Id == request.ItemId && bi.BatchOrder!.ShipperId == user.Id);

            if (item == null) return Json(new { success = false, message = "Không tìm thấy." });

            item.IsPickedUp = true;
            item.PickedUpAt = DateTime.Now;

            // Cập nhật order status
            if (item.Order != null)
            {
                item.Order.PickedUpAt = DateTime.Now;
                item.Order.Status = OrderStatus.Delivering;
            }

            // Nếu tất cả items đã picked up → batch InProgress
            var batch = item.BatchOrder!;
            if (batch.Status == BatchOrderStatus.Created)
            {
                batch.Status = BatchOrderStatus.InProgress;
            }

            // Kiểm tra tất cả đã lấy hết chưa
            var allItems = await _context.BatchOrderItems
                .Where(bi => bi.BatchOrderId == batch.Id)
                .ToListAsync();

            var allPicked = allItems.All(bi => bi.IsPickedUp || bi.Id == item.Id);
            
            await _context.SaveChangesAsync();

            return Json(new { success = true, allPickedUp = allPicked });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CompleteBatch([FromBody] CompleteBatchRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return Json(new { success = false, message = "Unauthorized" });

            var batch = await _context.BatchOrders
                .Include(b => b.Items).ThenInclude(bi => bi.Order)
                .FirstOrDefaultAsync(b => b.Id == request.BatchId && b.ShipperId == user.Id);

            if (batch == null) return Json(new { success = false, message = "Không tìm thấy batch." });

            batch.Status = BatchOrderStatus.Completed;
            batch.CompletedAt = DateTime.Now;

            foreach (var item in batch.Items)
            {
                if (item.Order != null)
                {
                    item.Order.Status = OrderStatus.Completed;
                    item.Order.CompletedAt = DateTime.Now;
                }
                item.IsPickedUp = true;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // ===== END GOM ĐƠN =====

        [Authorize(Roles = "Shipper")]
        public async Task<IActionResult> History(string filter = "all")
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return RedirectToAction("Login", "Account");

            var query = _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Where(o => o.ShipperId == user.Id && (o.Status == OrderStatus.Completed || o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.Failed))
                .AsQueryable();

            var today = DateTime.Now.Date;
            if (filter == "today") query = query.Where(o => o.CompletedAt.HasValue ? o.CompletedAt.Value.Date == today : o.CancelledAt.HasValue && o.CancelledAt.Value.Date == today);
            else if (filter == "week") query = query.Where(o => o.CreatedAt >= today.AddDays(-7));
            else if (filter == "month") query = query.Where(o => o.CreatedAt >= today.AddDays(-30));

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
            ViewBag.ShipperUser = user;
            ViewBag.Filter = filter;
            ViewData["Title"] = "Lịch sử giao";
            return View(orders);
        }

        [Authorize(Roles = "Shipper")]
        public async Task<IActionResult> Wallet()
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return RedirectToAction("Login", "Account");

            // Recent completed orders as "income transactions"
            var incomeOrders = await _context.Orders
                .Include(o => o.Store)
                .Where(o => o.ShipperId == user.Id && o.Status == OrderStatus.Completed)
                .OrderByDescending(o => o.CompletedAt)
                .Take(10)
                .ToListAsync();

            var totalIncome = await _context.Orders
                .Where(o => o.ShipperId == user.Id && o.Status == OrderStatus.Completed)
                .SumAsync(o => o.ShipperIncome);

            ViewBag.ShipperUser = user;
            ViewBag.TotalIncome = totalIncome;
            ViewBag.IncomeOrders = incomeOrders;
            ViewData["Title"] = "Ví tiền";
            return View();
        }

        [Authorize(Roles = "Shipper")]
        public async Task<IActionResult> Reviews()
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return RedirectToAction("Login", "Account");

            var reviews = await _context.Reviews
                .Include(r => r.Order).ThenInclude(o => o!.Store)
                .Include(r => r.Order).ThenInclude(o => o!.User)
                .Where(r => r.Order != null && r.Order.ShipperId == user.Id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var avgRating = reviews.Any() ? Math.Round(reviews.Average(r => r.RatingShipper), 1) : 5.0;
            var star5 = reviews.Count(r => r.RatingShipper == 5);
            var star4 = reviews.Count(r => r.RatingShipper == 4);
            var star3 = reviews.Count(r => r.RatingShipper == 3);
            var star2 = reviews.Count(r => r.RatingShipper == 2);
            var star1 = reviews.Count(r => r.RatingShipper == 1);
            int total = reviews.Count;

            ViewBag.ShipperUser = user;
            ViewBag.AvgRating = avgRating;
            ViewBag.TotalReviews = total;
            ViewBag.Star5 = total > 0 ? (int)Math.Round(star5 * 100.0 / total) : 0;
            ViewBag.Star4 = total > 0 ? (int)Math.Round(star4 * 100.0 / total) : 0;
            ViewBag.Star3 = total > 0 ? (int)Math.Round(star3 * 100.0 / total) : 0;
            ViewBag.Star2 = total > 0 ? (int)Math.Round(star2 * 100.0 / total) : 0;
            ViewBag.Star1 = total > 0 ? (int)Math.Round(star1 * 100.0 / total) : 0;
            ViewData["Title"] = "Đánh giá";
            return View(reviews);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ToggleOnline()
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return Json(new { success = false, message = "Unauthorized" });

            user.IsActive = !user.IsActive; // Toggle
            await _context.SaveChangesAsync();

            return Json(new { success = true, isActive = user.IsActive });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AcceptOrder([FromBody] string orderCode)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return Json(new { success = false, message = "Unauthorized" });

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderCode == orderCode);
            if (order == null) return Json(new { success = false, message = "Order not found" });

            if (order.Status != OrderStatus.SearchingShipper) {
                return Json(new { success = false, message = "Đơn hàng đã được nhận bởi người khác hoặc bị hủy." });
            }

            order.ShipperId = user.Id;
            order.Status = OrderStatus.Accepted;
            order.AcceptedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("OrderStatusUpdated", order.OrderCode, order.Status.ToString(), user.Id, user.FullName ?? "", user.PhoneNumber ?? "");

            // Return order coordinates for route drawing
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == order.StoreId);
            return Json(new
            {
                success = true,
                storeLat = store?.Latitude ?? 0,
                storeLng = store?.Longitude ?? 0,
                storeName = store?.Name ?? "Cửa hàng",
                pickupAddress = order.PickupAddress,
                deliveryLat = order.DeliveryLatitude,
                deliveryLng = order.DeliveryLongitude,
                deliveryAddress = order.DeliveryAddress,
                customerName = (await _context.Users.FirstOrDefaultAsync(u => u.Id == order.UserId))?.FullName ?? "Khách hàng",
                distance = order.Distance
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> MarkPickedUp([FromBody] string orderCode)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return Json(new { success = false, message = "Unauthorized" });

            var order = await _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .FirstOrDefaultAsync(o => o.OrderCode == orderCode && o.ShipperId == user.Id);

            if (order == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

            if (order.Status != OrderStatus.Accepted && order.Status != OrderStatus.Preparing)
                return Json(new { success = false, message = "Đơn hàng không ở trạng thái có thể lấy hàng." });

            order.Status = OrderStatus.Delivering;
            order.PickedUpAt = DateTime.Now;

            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("OrderStatusUpdated", order.OrderCode, order.Status.ToString(), user.Id, user.FullName ?? "", user.PhoneNumber ?? "");

            return Json(new
            {
                success = true,
                storeLat = order.Store?.Latitude ?? 0,
                storeLng = order.Store?.Longitude ?? 0,
                deliveryLat = order.DeliveryLatitude,
                deliveryLng = order.DeliveryLongitude,
                deliveryAddress = order.DeliveryAddress,
                customerName = order.User?.FullName ?? "Khách hàng"
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CompleteOrder([FromBody] string orderCode)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);
            if (user == null) return Json(new { success = false, message = "Unauthorized" });

            var order = await _context.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.OrderCode == orderCode && o.ShipperId == user.Id);
            if (order == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

            order.Status = OrderStatus.Completed;
            order.CompletedAt = DateTime.Now;
            
            user.IsDelivering = false;
            
            if (user.PendingLock) {
                user.PendingLock = false;
                user.IsLocked = true;
                user.IsActive = false;
                // Nếu muốn ghi log thông báo nội bộ tùy bạn
            }

            if (order.User != null) {
                order.User.TotalSpent += order.TotalPrice; // Chỉ cộng TotalPrice, hoặc cộng luôn ShippingFee tùy logic bạn đang thiết kế (ở đây lấy TotalPrice theo giá đồ ăn dã giảm - hay giá tổng)
                // Cập nhật hạng
                var oldTier = order.User.UserTier;
                if (order.User.TotalSpent >= 15000000) order.User.UserTier = "Kim Cương";
                else if (order.User.TotalSpent >= 5000000) order.User.UserTier = "Vàng";
                else if (order.User.TotalSpent >= 1000000) order.User.UserTier = "Bạc";
                else order.User.UserTier = "Đồng";
            }

            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("OrderStatusUpdated", order.OrderCode, order.Status.ToString(), user.Id, user.FullName ?? "", user.PhoneNumber ?? "");

            return Json(new { success = true });
        }
    }

    // ==================================================================================
    // RESTAURANT MANAGER CONTROLLER
    // ==================================================================================
    [Authorize(Roles = "RestaurantManager")]
    public class RestaurantManagerController : Controller
    {
        private readonly Data.ApplicationDbContext _context;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<DeliveryHubWeb.Hubs.OrderHub> _hubContext;

        public RestaurantManagerController(Data.ApplicationDbContext context, Microsoft.AspNetCore.SignalR.IHubContext<DeliveryHubWeb.Hubs.OrderHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> UpdateOrderStatus([FromBody] UpdateOrderStatusRequest req)
        {
            if (req == null) return Json(new { success = false, message = "Dữ liệu không hợp lệ." });
            var user = await GetCurrentUser();
            if (user == null || !user.ManagedStoreId.HasValue) return Json(new { success = false, message = "Bạn chưa đăng nhập hoặc không có quyền quản lý cửa hàng." });

            var order = await _context.Orders.Include(o => o.User).Include(o => o.Shipper).FirstOrDefaultAsync(o => o.Id == req.OrderId && o.StoreId == user.ManagedStoreId.Value);
            if (order == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

            if (!Enum.TryParse<OrderStatus>(req.Status, out var newStatus)) {
                return Json(new { success = false, message = "Trạng thái không hợp lệ." });
            }

            order.Status = newStatus;

            // Xử lý Logic Thành công / Thất bại
            if (newStatus == OrderStatus.Completed || newStatus == OrderStatus.Failed) {
                if (newStatus == OrderStatus.Completed) order.CompletedAt = DateTime.Now;

                // Xử lý Shipper Pending Lock
                if (order.Shipper != null) {
                    order.Shipper.IsDelivering = false;
                    if (order.Shipper.PendingLock) {
                        order.Shipper.PendingLock = false;
                        order.Shipper.IsLocked = true;
                        order.Shipper.IsActive = false;
                    }
                }

                if (order.User != null) {
                    if (newStatus == OrderStatus.Completed) {
                        order.User.TotalSpent += order.TotalPrice;
                        if (order.User.TotalSpent >= 15000000) order.User.UserTier = "Kim Cương";
                        else if (order.User.TotalSpent >= 5000000) order.User.UserTier = "Vàng";
                        else if (order.User.TotalSpent >= 1000000) order.User.UserTier = "Bạc";
                        else order.User.UserTier = "Đồng";
                    } else if (newStatus == OrderStatus.Failed) {
                        order.User.FailedOrdersCount++;
                        order.User.MonthlyFailedOrdersCount++;

                        var tier = order.User.UserTier;
                        if ((tier == "Đồng" && order.User.FailedOrdersCount >= 1) ||
                            (tier == "Bạc" && order.User.FailedOrdersCount >= 3) ||
                            (tier == "Vàng" && order.User.MonthlyFailedOrdersCount >= 3) ||
                            (tier == "Kim Cương" && order.User.MonthlyFailedOrdersCount >= 5)) 
                        {
                            order.User.IsLocked = true;
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("OrderStatusUpdated", order.OrderCode, order.Status.ToString(), "", "", "");

            return Json(new { success = true });
        }

        public class UpdateOrderStatusRequest {
            public int OrderId { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        private async Task<ApplicationUser?> GetCurrentUser()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<IActionResult> Index(string timeRange = "7days")
        {
            var user = await GetCurrentUser();
            if (user == null || !user.ManagedStoreId.HasValue) return RedirectToAction("Login", "Account");

            var storeId = user.ManagedStoreId.Value;
            var store = await _context.Stores
                .Include(s => s.MenuItems)
                .FirstOrDefaultAsync(s => s.Id == storeId);

            if (store == null) return NotFound("Chi nhánh không tồn tại.");

            var query = _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Where(o => o.StoreId == storeId)
                .AsQueryable();

            var now = DateTime.UtcNow;
            if (timeRange == "today") query = query.Where(o => o.CreatedAt >= now.Date);
            else if (timeRange == "7days") query = query.Where(o => o.CreatedAt >= now.AddDays(-7));
            else if (timeRange == "1month") query = query.Where(o => o.CreatedAt >= now.AddMonths(-1));

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();

            ViewBag.Store = store;
            ViewBag.TotalOrders = orders.Count;
            ViewBag.TotalRevenue = orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalPrice);
            ViewBag.SelectedTimeRange = timeRange;

            // Status stats
            ViewBag.StatusStats = orders
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                .ToList();

            // Chart data
            ViewBag.ChartData = orders
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new {
                    Date = g.Key,
                    Revenue = g.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalPrice),
                    Count = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToList();

            return View(orders.Take(10).ToList());
        }

        public async Task<IActionResult> Orders(string search, string timeRange = "all")
        {
            var user = await GetCurrentUser();
            if (user == null || !user.ManagedStoreId.HasValue) return RedirectToAction("Login", "Account");

            var storeId = user.ManagedStoreId.Value;
            var query = _context.Orders
                .Include(o => o.User)
                .Include(o => o.Shipper)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Where(o => o.StoreId == storeId)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
                query = query.Where(o => o.OrderCode.Contains(search) || (o.User != null && o.User.FullName != null && o.User.FullName.Contains(search)));

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
            ViewBag.StoreId = storeId;
            return View(orders);
        }

        public async Task<IActionResult> Menu()
        {
            var user = await GetCurrentUser();
            if (user == null || !user.ManagedStoreId.HasValue) return RedirectToAction("Login", "Account");

            var storeId = user.ManagedStoreId.Value;
            var store = await _context.Stores
                .Include(s => s.MenuItems)
                .FirstOrDefaultAsync(s => s.Id == storeId);

            if (store == null) return NotFound();

            // Calculate Item Sales Counts
            var itemSalesCounts = await _context.OrderItems
                .Include(oi => oi.Order)
                .Where(oi => oi.Order != null && oi.Order.StoreId == storeId && oi.Order.Status == OrderStatus.Completed)
                .GroupBy(oi => oi.MenuItemId)
                .Select(g => new { MenuItemId = g.Key, Count = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.MenuItemId, x => x.Count);
            
            ViewBag.ItemSalesCounts = itemSalesCounts;

            // Calculate Item Ratings
            var reviews = await _context.Reviews
                .Include(r => r.Order).ThenInclude(o => o!.OrderItems)
                .Where(r => r.Order != null && r.Order.StoreId == storeId)
                .ToListAsync();

            var itemRatings = reviews
                .Where(r => r.Order!.Status == OrderStatus.Completed && r.Type == ReviewType.Customer)
                .SelectMany(r => r.Order!.OrderItems.Select(oi => new { oi.MenuItemId, r.RatingMenu }))
                .GroupBy(x => x.MenuItemId)
                .ToDictionary(g => g.Key, g => (Count: g.Count(), Avg: Math.Round(g.Average(x => (double)x.RatingMenu), 1)));

            ViewBag.ItemRatings = itemRatings;
            ViewBag.TopItems = itemSalesCounts.OrderByDescending(x => x.Value).Take(3).ToDictionary(x => x.Key, x => x.Value);

            return View(store);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStoreStatus()
        {
            var user = await GetCurrentUser();
            if (user == null || !user.ManagedStoreId.HasValue) return Json(new { success = false });

            var store = await _context.Stores.FindAsync(user.ManagedStoreId.Value);
            if (store == null) return Json(new { success = false });

            store.IsOpen = !store.IsOpen;
            await _context.SaveChangesAsync();
            return Json(new { success = true, isOpen = store.IsOpen });
        }

        [HttpPost]
        public async Task<IActionResult> SaveMenuItem(int id, string name, string description, decimal price, string category, bool isAvailable, string? imageUrl, IFormFile? imageFile)
        {
            var user = await GetCurrentUser();
            if (user == null || !user.ManagedStoreId.HasValue) return Json(new { success = false });

            var storeId = user.ManagedStoreId.Value;
            MenuItem? item;
            if (id == 0) {
                item = new MenuItem { StoreId = storeId };
                _context.MenuItems.Add(item);
            } else {
                item = await _context.MenuItems.FirstOrDefaultAsync(m => m.Id == id && m.StoreId == storeId);
                if (item == null) return Json(new { success = false });
            }

            item.Name = name;
            item.Description = description;
            item.Price = price;
            item.Category = category;
            item.IsAvailable = isAvailable;

            if (imageFile != null) {
                var fileName = $"menu_{storeId}_{DateTime.Now.Ticks}{Path.GetExtension(imageFile.FileName)}";
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/menuitems", fileName);
                var dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                using (var stream = new FileStream(path, FileMode.Create)) await imageFile.CopyToAsync(stream);
                item.ImageUrl = $"/uploads/menuitems/{fileName}";
            } else if (!string.IsNullOrEmpty(imageUrl)) {
                item.ImageUrl = imageUrl;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
    // ==================================================================================
    // REQUEST DTOS
    // ==================================================================================
    public class CreateBatchRequest
    {
        public List<string> OrderCodes { get; set; } = new();
        public string DeliveryAddress { get; set; } = "";
        public double DeliveryLat { get; set; }
        public double DeliveryLng { get; set; }
    }

    public class PickupBatchItemRequest
    {
        public int ItemId { get; set; }
    }

    public class CompleteBatchRequest
    {
        public int BatchId { get; set; }
    }
}

