using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using DeliveryHubWeb.Models;
using DeliveryHubWeb.Data;

namespace DeliveryHubWeb.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var topSales = await _context.OrderItems
            .Where(oi => oi.Order != null && oi.Order.Status == OrderStatus.Completed && oi.MenuItem != null && oi.MenuItem.IsAvailable)
            .GroupBy(oi => oi.MenuItemId)
            .Select(g => new {
                MenuItemId = g.Key,
                TotalSold = g.Sum(oi => oi.Quantity)
            })
            .OrderByDescending(x => x.TotalSold)
            .Take(6)
            .ToListAsync();

        var topMenuItemIds = topSales.Select(x => x.MenuItemId).ToList();
        var topMenuItems = new List<MenuItem>();

        if (topMenuItemIds.Any())
        {
            var fetchedItems = await _context.MenuItems
                .Where(m => topMenuItemIds.Contains(m.Id))
                .ToListAsync();

            foreach (var id in topMenuItemIds)
            {
                var item = fetchedItems.FirstOrDefault(m => m.Id == id);
                if (item != null) topMenuItems.Add(item);
            }
        }

        if (topMenuItems.Count < 6)
        {
            var existingIds = topMenuItems.Select(m => m!.Id).ToList();
            var additional = await _context.MenuItems
                .Where(m => !existingIds.Contains(m.Id) && m.IsAvailable)
                .Take(6 - topMenuItems.Count)
                .ToListAsync();
            topMenuItems.AddRange(additional);
        }

        var finalItemIds = topMenuItems.Select(m => m.Id).ToList();

        var totalSoldDict = await _context.OrderItems
            .Where(oi => finalItemIds.Contains(oi.MenuItemId) && oi.Order != null && oi.Order.Status == OrderStatus.Completed)
            .GroupBy(oi => oi.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, TotalSold = g.Sum(oi => oi.Quantity) })
            .ToDictionaryAsync(x => x.MenuItemId, x => x.TotalSold);

        var itemRatingsDict = await _context.OrderItems
            .Where(oi => finalItemIds.Contains(oi.MenuItemId) && oi.Order != null)
            .Join(_context.Reviews, oi => oi.OrderId, r => r.OrderId, (oi, r) => new { oi.MenuItemId, r.RatingMenu })
            .GroupBy(x => x.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, AvgRating = g.Average(x => (double)x.RatingMenu) })
            .ToDictionaryAsync(x => x.MenuItemId, x => x.AvgRating);

        var productsList = topMenuItems.Select(m => new {
            id = m!.Id,
            categoryId = m.Category.ToLower() == "pizza" ? "pizza" : 
                         m.Category.ToLower() == "burger" ? "burger" : 
                         m.Category.ToLower() == "sushi" ? "sushi" : 
                         m.Category.ToLower() == "mì ý" ? "pasta" : 
                         m.Category.ToLower() == "salad" ? "salad" : 
                         m.Category.ToLower().Contains("uống") ? "drinks" : "pizza",
            name = m.Name,
            price = m.Price,
            description = m.Description,
            image = string.IsNullOrEmpty(m.ImageUrl) ? "https://images.unsplash.com/photo-1568901346375-23c9450c58cd?q=80&w=600&auto=format&fit=crop" : m.ImageUrl,
            rating = itemRatingsDict.ContainsKey(m.Id) ? Math.Round(itemRatingsDict[m.Id], 1) : 5.0,
            totalSold = totalSoldDict.ContainsKey(m.Id) ? totalSoldDict[m.Id] : 0
        }).ToList();

        ViewBag.TopProductsJson = System.Text.Json.JsonSerializer.Serialize(productsList);

        var topStoreStats = await _context.Orders
            .Where(o => o.Status == OrderStatus.Completed && o.StoreId != null)
            .GroupBy(o => o.StoreId)
            .Select(g => new { StoreId = g.Key!.Value, TotalOrders = g.Count() })
            .OrderByDescending(x => x.TotalOrders)
            .Take(5)
            .ToListAsync();

        var topStoreIds = topStoreStats.Select(x => x.StoreId).ToList();
        var topStores = new List<Store>();

        if (topStoreIds.Any())
        {
            var fetchedStores = await _context.Stores
                .Where(s => topStoreIds.Contains(s.Id))
                .ToListAsync();

            foreach (var id in topStoreIds)
            {
                var store = fetchedStores.FirstOrDefault(s => s.Id == id);
                if (store != null) topStores.Add(store);
            }
        }

        if (topStores.Count < 5)
        {
            var existingStoreIds = topStores.Select(s => s.Id).ToList();
            var additionalStores = await _context.Stores
                .Where(s => !existingStoreIds.Contains(s.Id) && s.IsOpen && s.ActivityState == StoreActivityState.Active)
                .Take(5 - topStores.Count)
                .ToListAsync();
            topStores.AddRange(additionalStores);
        }

        var branchRatings = await _context.Reviews
            .Where(r => r.Order != null && r.Order.StoreId != null && topStoreIds.Contains(r.Order.StoreId.Value))
            .GroupBy(r => r.Order!.StoreId!.Value)
            .Select(g => new { StoreId = g.Key, AvgRating = g.Average(r => (double)r.RatingMenu) })
            .ToDictionaryAsync(x => x.StoreId, x => x.AvgRating);

        var branchesList = topStores.Select(s => new {
            id = s.Id,
            name = s.Name,
            address = s.Address ?? "TP. Hồ Chí Minh",
            image = string.IsNullOrEmpty(s.ImageUrl) ? "https://images.unsplash.com/photo-1555396273-367ea4eb4db5?q=80&w=600&auto=format&fit=crop" : s.ImageUrl,
            rating = branchRatings.ContainsKey(s.Id) ? Math.Round(branchRatings[s.Id], 1) : 5.0,
            totalSold = topStoreStats.FirstOrDefault(x => x.StoreId == s.Id)?.TotalOrders ?? 0
        }).ToList();

        ViewBag.TopBranchesJson = System.Text.Json.JsonSerializer.Serialize(branchesList);

        return View();
    }

    public async Task<IActionResult> Store(int id)
    {
        var store = await _context.Stores
            .Include(s => s.MenuItems)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (store == null) return NotFound();

        var storeReviews = await _context.Reviews
            .Where(r => r.Order != null && r.Order.StoreId == id)
            .ToListAsync();

        var completedOrders = await _context.Orders
            .CountAsync(o => o.StoreId == id && o.Status == OrderStatus.Completed);

        double realRating = storeReviews.Any() ? Math.Round(storeReviews.Average(r => (double)r.RatingMenu), 1) : 5.0;

        ViewBag.RealRating = realRating;
        ViewBag.TotalSold = completedOrders;
        ViewBag.ReviewCount = storeReviews.Count;

        var storeItemIds = store.MenuItems.Select(m => m.Id).ToList();
        
        var totalSoldDictLocal = await _context.OrderItems
            .Where(oi => storeItemIds.Contains(oi.MenuItemId) && oi.Order != null && oi.Order.Status == OrderStatus.Completed)
            .GroupBy(oi => oi.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, TotalSold = g.Sum(oi => oi.Quantity) })
            .ToDictionaryAsync(x => x.MenuItemId, x => x.TotalSold);

        var itemRatingsDictLocal = await _context.OrderItems
            .Where(oi => storeItemIds.Contains(oi.MenuItemId) && oi.Order != null)
            .Join(_context.Reviews, oi => oi.OrderId, r => r.OrderId, (oi, r) => new { oi.MenuItemId, r.RatingMenu })
            .GroupBy(x => x.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, AvgRating = Math.Round(g.Average(x => (double)x.RatingMenu), 1) })
            .ToDictionaryAsync(x => x.MenuItemId, x => x.AvgRating);

        ViewBag.ItemSoldDict = totalSoldDictLocal;
        ViewBag.ItemRatingsDict = itemRatingsDictLocal;

        return View(store);
    }

    public async Task<IActionResult> Menu(string search, string filter, int page = 1)
    {
        int pageSize = 25;

        var query = _context.MenuItems
            .Include(m => m.Store)
            .Where(m => m.IsAvailable && m.Store != null && m.Store.ActivityState == StoreActivityState.Active);

        var allItems = await query.ToListAsync();

        if (!string.IsNullOrEmpty(search))
        {
            string normalizedSearch = RemoveDiacritics(search).ToLower();
            allItems = allItems.Where(m => 
                RemoveDiacritics(m.Name).ToLower().Contains(normalizedSearch) || 
                (m.Description != null && RemoveDiacritics(m.Description).ToLower().Contains(normalizedSearch))
            ).ToList();
        }

        var itemIds = allItems.Select(m => m.Id).ToList();

        var totalSoldDictMenu = await _context.OrderItems
            .Where(oi => itemIds.Contains(oi.MenuItemId) && oi.Order != null && oi.Order.Status == OrderStatus.Completed)
            .GroupBy(oi => oi.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, TotalSold = g.Sum(oi => oi.Quantity) })
            .ToDictionaryAsync(x => x.MenuItemId, x => x.TotalSold);

        var itemRatingsDictMenu = await _context.OrderItems
            .Where(oi => itemIds.Contains(oi.MenuItemId) && oi.Order != null)
            .Join(_context.Reviews, oi => oi.OrderId, r => r.OrderId, (oi, r) => new { oi.MenuItemId, r.RatingMenu })
            .GroupBy(x => x.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, AvgRating = Math.Round(g.Average(x => (double)x.RatingMenu), 1) })
            .ToDictionaryAsync(x => x.MenuItemId, x => x.AvgRating);

        var menuItemsWithStats = allItems.Select(m => new {
            Item = m,
            Rating = itemRatingsDictMenu.ContainsKey(m.Id) ? itemRatingsDictMenu[m.Id] : 5.0,
            Sold = totalSoldDictMenu.ContainsKey(m.Id) ? totalSoldDictMenu[m.Id] : 0
        });

        menuItemsWithStats = filter switch
        {
            "top-rating" => menuItemsWithStats.OrderByDescending(m => m.Rating).ThenByDescending(m => m.Sold),
            "top-sales" => menuItemsWithStats.OrderByDescending(m => m.Sold).ThenByDescending(m => m.Rating),
            "price-asc" => menuItemsWithStats.OrderBy(m => m.Item.Price),
            "price-desc" => menuItemsWithStats.OrderByDescending(m => m.Item.Price),
            _ => menuItemsWithStats.OrderByDescending(m => m.Sold)
        };

        var totalItems = menuItemsWithStats.Count();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var pagedItems = menuItemsWithStats.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        ViewBag.CurrentSearch = search;
        ViewBag.CurrentFilter = filter;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;

        var resultList = pagedItems.Select(p => p.Item).ToList();
        
        ViewBag.ItemSoldDict = totalSoldDictMenu;
        ViewBag.ItemRatingsDict = itemRatingsDictMenu;

        return View(resultList);
    }

    // =============================================================
    // ĐÁNH GIÁ SHIPPER & NHÀ HÀNG (User → sau khi đơn Completed)
    // =============================================================

    [Authorize(Roles = "User")]
    [HttpGet]
    public async Task<IActionResult> SubmitReview(int orderId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return RedirectToAction("Login", "Account");

        var order = await _context.Orders
            .Include(o => o.Store)
            .Include(o => o.Shipper)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId && o.Status == OrderStatus.Completed);

        if (order == null) return NotFound("Đơn hàng không tồn tại hoặc chưa hoàn thành.");

        // Kiểm tra đã review chưa
        var existingReview = await _context.Reviews.FirstOrDefaultAsync(r => r.OrderId == orderId);
        if (existingReview != null)
        {
            TempData["InfoMessage"] = "Bạn đã đánh giá đơn hàng này rồi.";
            return RedirectToAction("Index");
        }

        return View(order);
    }

    [Authorize(Roles = "User")]
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SubmitReview([FromBody] SubmitReviewRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Json(new { success = false, message = "Chưa đăng nhập." });

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.UserId == userId && o.Status == OrderStatus.Completed);
        if (order == null) return Json(new { success = false, message = "Đơn hàng không hợp lệ." });

        // Check duplicate
        if (await _context.Reviews.AnyAsync(r => r.OrderId == request.OrderId))
            return Json(new { success = false, message = "Đã đánh giá đơn này." });

        var review = new Review
        {
            OrderId = request.OrderId,
            ShipperId = order.ShipperId,
            StoreId = order.StoreId,
            RatingMenu = Math.Clamp(request.RatingMenu, 1, 5),
            RatingShipper = Math.Clamp(request.RatingShipper, 1, 5),
            Comment = request.Comment,
            CommentForShipper = request.CommentForShipper,
            Type = ReviewType.Customer,
            CreatedAt = DateTime.Now
        };

        _context.Reviews.Add(review);

        // Flag 1 sao cho shipper → HasOneStarReview + thông báo Admin
        if (review.RatingShipper == 1 && order.ShipperId != null)
        {
            var shipper = await _context.Users.FindAsync(order.ShipperId);
            if (shipper != null)
            {
                shipper.HasOneStarReview = true;
                _context.Notifications.Add(new Notification
                {
                    Title = "Cảnh báo: Shipper nhận 1 ⭐",
                    Message = $"Shipper {shipper.FullName} ({shipper.ShipperCode}) bị đánh giá 1 sao cho đơn {order.OrderCode}",
                    Type = NotificationType.CustomerReview,
                    RelatedId = shipper.Id,
                    TargetUrl = $"/Admin/ShipperDetail/{shipper.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }
        }

        // Flag 1 sao cho cửa hàng
        if (review.RatingMenu == 1 && order.StoreId.HasValue)
        {
            var store = await _context.Stores.FindAsync(order.StoreId.Value);
            if (store != null)
            {
                store.HasOneStarReview = true;
                _context.Notifications.Add(new Notification
                {
                    Title = "Cảnh báo: Cửa hàng nhận 1 ⭐",
                    Message = $"Cửa hàng {store.Name} bị đánh giá 1 sao cho đơn {order.OrderCode}",
                    Type = NotificationType.ShipperReview,
                    RelatedId = order.StoreId.Value.ToString(),
                    TargetUrl = $"/Admin/StoreDetail/{order.StoreId.Value}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }
        }

        await _context.SaveChangesAsync();
        return Json(new { success = true, message = "Cảm ơn bạn đã đánh giá!" });
    }

    // =============================================================
    // TÍNH PHÍ GIAO HÀNG THEO KHOẢNG CÁCH THỰC TẾ (Haversine)
    // =============================================================

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CalculateShippingFee([FromBody] ShippingFeeRequest request)
    {
        // Haversine distance
        double dist = HaversineDistance(request.PickupLat, request.PickupLng, request.DeliveryLat, request.DeliveryLng);
        dist = Math.Round(dist, 1);

        // Lấy service config
        decimal baseFee = 15000m;
        decimal feePerKm = 5000m;
        int estMinPerKm = 3;

        if (request.ServiceId > 0)
        {
            var service = await _context.DeliveryServices.FindAsync(request.ServiceId);
            if (service != null)
            {
                baseFee = service.BaseFee;
                feePerKm = service.FeePerKm;
                estMinPerKm = service.EstimatedMinutesPerKm;
            }
        }

        decimal fee = baseFee + feePerKm * (decimal)dist;
        fee = Math.Round(fee / 1000) * 1000; // làm tròn nghìn

        return Json(new
        {
            success = true,
            distance = dist,
            fee = fee,
            estimatedMinutes = Math.Round(dist * estMinPerKm)
        });
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        
        string normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }
        
        string result = stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
        return result.Replace('đ', 'd').Replace('Đ', 'D');
    }

    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

// Request DTOs
public class SubmitReviewRequest
{
    public int OrderId { get; set; }
    public int RatingMenu { get; set; }
    public int RatingShipper { get; set; }
    public string? Comment { get; set; }
    public string? CommentForShipper { get; set; }
}

public class ShippingFeeRequest
{
    public double PickupLat { get; set; }
    public double PickupLng { get; set; }
    public double DeliveryLat { get; set; }
    public double DeliveryLng { get; set; }
    public int ServiceId { get; set; }
}
