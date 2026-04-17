using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryHubWeb.Models;
using DeliveryHubWeb.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;

namespace DeliveryHubWeb.Controllers
{
    [Authorize(Roles = "Partner")]
    public class PartnerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public PartnerController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        private async Task<ApplicationUser> GetCurrentUser()
        {
            return await _userManager.GetUserAsync(User);
        }

        public async Task<IActionResult> Index(int? storeId, string timeRange = "7days")
        {
            var user = await GetCurrentUser();
            if (user == null) return NotFound();

            var stores = await _context.Stores
                .Include(s => s.MenuItems)
                .Where(s => s.OwnerId == user.Id)
                .ToListAsync();

            ViewBag.Stores = stores;
            ViewBag.SelectedStore = storeId;
            ViewBag.SelectedTimeRange = timeRange;

            var baseQuery = _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Include(o => o.Shipper)
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
                .Where(o => o.Store!.OwnerId == user.Id);

            if (storeId.HasValue && storeId > 0)
            {
                baseQuery = baseQuery.Where(o => o.StoreId == storeId);
            }

            var orders = await baseQuery.OrderByDescending(o => o.CreatedAt).ToListAsync();

            ViewBag.TotalOrders = orders.Count;
            ViewBag.TotalRevenue = orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalPrice);
            
            // Chart Data
            var groupedOrders = orders.GroupBy(o => o.CreatedAt.Date).Select(g => new {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Where(x => x.Status == OrderStatus.Completed).Sum(x => x.TotalPrice)
            }).OrderBy(x => x.Date).ToList();

            ViewBag.ChartData = groupedOrders;
            ViewBag.StatusStats = orders.GroupBy(o => o.Status).Select(g => new { Status = g.Key.ToString(), Count = g.Count() }).ToList();

            if (storeId.HasValue)
            {
                var topItems = orders.SelectMany(o => o.OrderItems)
                    .GroupBy(x => x.MenuItemId)
                    .Select(g => new { ItemName = g.First().MenuItem?.Name ?? "N/A", Quantity = g.Sum(x => x.Quantity) })
                    .OrderByDescending(x => x.Quantity)
                    .Take(5)
                    .ToList();
                ViewBag.TopItems = topItems;
            }
            else
            {
                var revByBranch = orders.Where(o => o.Status == OrderStatus.Completed)
                    .GroupBy(o => o.StoreId)
                    .Select(g => new { StoreName = g.First().Store!.Name, Revenue = g.Sum(x => x.TotalPrice) })
                    .OrderByDescending(x => x.Revenue)
                    .Take(5).ToList();
                ViewBag.RevenueByBranch = revByBranch;
            }

            // Dummy ShipperCodes
            ViewBag.ShipperCodes = new Dictionary<string, string>();

            return View(orders);
        }

        public async Task<IActionResult> Branches(string search, int page = 1)
        {
            var user = await GetCurrentUser();
            if (user == null) return NotFound();

            var query = _context.Stores
                .Include(s => s.MenuItems)
                .Where(s => s.OwnerId == user.Id);

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(s => (s.Name != null && s.Name.Contains(search)) || (s.Address != null && s.Address.Contains(search)));
            }

            int pageSize = 12;
            int total = await query.CountAsync();
            var stores = await query.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.SearchTerm = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.IsPartnerActive = user.IsActive; // Assuming partner IsActive
            ViewBag.PendingStoreIds = new List<string>();

            return View(stores);
        }

        public async Task<IActionResult> Orders(int? storeId, string timeRange = "all", string search = "")
        {
            var user = await GetCurrentUser();
            if (user == null) return NotFound();

            ViewBag.StoreId = storeId;
            if (storeId.HasValue && storeId > 0)
            {
                var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == storeId && s.OwnerId == user.Id);
                if (store != null) ViewBag.StoreName = store.Name;
            }

            var query = _context.Orders
                .Include(o => o.Store)
                .Include(o => o.User)
                .Include(o => o.Shipper)
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
                .Where(o => o.Store.OwnerId == user.Id);

            if (storeId.HasValue && storeId > 0)
            {
                query = query.Where(o => o.StoreId == storeId.Value);
            }

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(o => (o.OrderCode != null && o.OrderCode.Contains(search)) || (o.User != null && o.User.FullName != null && o.User.FullName.Contains(search)));
            }

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
            return View(orders);
        }

        public async Task<IActionResult> StoreDetail(int id)
        {
            var user = await GetCurrentUser();
            if (user == null) return NotFound();

            var store = await _context.Stores
                .Include(s => s.MenuItems)
                .FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == user.Id);

            if (store == null) return RedirectToAction(nameof(Branches));

            var orders = await _context.Orders.Include(o => o.OrderItems)
                .Where(o => o.StoreId == id && o.Status == OrderStatus.Completed)
                .ToListAsync();

            var itemSalesCounts = new Dictionary<int, int>();
            var itemRatings = new Dictionary<int, (int Count, double Avg)>();

            foreach (var o in orders)
            {
                foreach (var st in o.OrderItems)
                {
                    if (!itemSalesCounts.ContainsKey(st.MenuItemId)) itemSalesCounts[st.MenuItemId] = 0;
                    itemSalesCounts[st.MenuItemId] += st.Quantity;
                }
            }

            ViewBag.ItemSalesCounts = itemSalesCounts;
            ViewBag.ItemRatings = itemRatings;
            ViewBag.TopItems = itemSalesCounts;
            ViewBag.IsPartnerActive = user.IsActive;

            return View(store);
        }

        [HttpPost]
        public async Task<IActionResult> AddBranch([FromForm] Store model)
        {
            var user = await GetCurrentUser();
            if (user == null) return Json(new { success = false, message = "Không tìm thấy user" });

            model.OwnerId = user.Id;
            model.CreatedAt = DateTime.UtcNow;
            model.ActivityState = StoreActivityState.Active;
            model.IsOpen = true;
            model.Rating = 5.0;

            _context.Stores.Add(model);
            await _context.SaveChangesAsync();

            // Setup RM for branch
            var rmEmail = $"rm_{model.Id}@deliveryhub.vn";
            var existingRm = await _userManager.FindByEmailAsync(rmEmail);
            if (existingRm == null)
            {
                var rmUser = new ApplicationUser
                {
                    UserName = rmEmail,
                    Email = rmEmail,
                    FullName = $"QL {model.Name.Substring(0, Math.Min(model.Name.Length, 15))}",
                    Role = UserRole.RestaurantManager,
                    IsActive = true,
                    ManagedStoreId = model.Id,
                    EmailConfirmed = true
                };
                await _userManager.CreateAsync(rmUser, "Demo@2026");
            }

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStore([FromForm] Store model)
        {
            var user = await GetCurrentUser();
            if (user == null) return Json(new { success = false, message = "Auth form failed" });

            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == model.Id && s.OwnerId == user.Id);
            if (store == null) return Json(new { success = false, message = "Store not found" });

            store.Name = model.Name;
            store.Address = model.Address;
            store.ImageUrl = model.ImageUrl;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStoreStatus(int id)
        {
            var user = await GetCurrentUser();
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == user.Id);
            if (store == null) return Json(new { success = false });

            store.IsOpen = !store.IsOpen;
            await _context.SaveChangesAsync();
            return Json(new { success = true, isOpen = store.IsOpen });
        }

        [HttpPost]
        public async Task<IActionResult> LockStoreByPartner(int id)
        {
            var user = await GetCurrentUser();
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == user.Id);
            if (store == null) return Json(new { success = false });

            store.ActivityState = StoreActivityState.LockedByPartner;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UnlockStoreByPartner(int id)
        {
            var user = await GetCurrentUser();
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == user.Id);
            if (store == null) return Json(new { success = false });

            store.ActivityState = StoreActivityState.Active;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> SaveMenuItem([FromForm] MenuItem model, int storeId)
        {
            var user = await GetCurrentUser();
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == storeId && s.OwnerId == user.Id);
            if (store == null) return Json(new { success = false, message = "Access denied" });

            if (model.Id == 0)
            {
                model.StoreId = storeId;
                _context.MenuItems.Add(model);
            }
            else
            {
                var existing = await _context.MenuItems.FirstOrDefaultAsync(m => m.Id == model.Id && m.StoreId == storeId);
                if (existing != null)
                {
                    existing.Name = model.Name;
                    existing.Description = model.Description;
                    existing.Price = model.Price;
                    existing.Category = model.Category;
                    existing.ImageUrl = model.ImageUrl;
                    existing.IsAvailable = model.IsAvailable;
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}
