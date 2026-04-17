using DeliveryHubWeb.Data;
using DeliveryHubWeb.Models;
using DeliveryHubWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryHubWeb.Hubs;

namespace DeliveryHubWeb.Controllers
{
    [Authorize]
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMapService _mapService;
        private readonly IHubContext<OrderHub> _hubContext;

        public OrderController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IMapService mapService, IHubContext<OrderHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _mapService = mapService;
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<IActionResult> Checkout()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");
            return View(user);
        }

        [HttpGet]
        public IActionResult Delivery()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(string pickup, string delivery, double pLat, double pLon, double dLat, double dLon)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var distance = _mapService.CalculateDistance(pLat, pLon, dLat, dLon);
            var standardService = await _context.DeliveryServices.FirstOrDefaultAsync(s => s.Name == "Tiêu chuẩn");
            if (standardService == null) standardService = await _context.DeliveryServices.FirstOrDefaultAsync();

            decimal baseFee = standardService?.BaseFee ?? 15000m;
            decimal feePerKm = standardService?.FeePerKm ?? 5000m;
            
            distance = Math.Round(distance, 1);
            decimal fee = baseFee + ((decimal)distance * feePerKm);

            var config = ShipperIncomeConfig.Load();
            decimal extraFee = (decimal)Math.Ceiling(Math.Max(distance - config.BaseDistance, 0.0)) * config.ExtraFeePerKm;
            decimal shipperIncome = config.BaseIncome + extraFee;

            var order = new Order
            {
                OrderCode = $"DH-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                UserId = user.Id,
                ServiceId = standardService?.Id,
                Status = OrderStatus.SearchingShipper,
                TotalPrice = fee, // Tạm thời TotalPrice là shipping fee vì đây là đơn giao hàng đơn thuần không món
                ShippingFee = fee,
                ShipperIncome = shipperIncome,
                PickupAddress = pickup,
                DeliveryAddress = delivery,
                PickupLatitude = pLat,
                PickupLongitude = pLon,
                DeliveryLatitude = dLat,
                DeliveryLongitude = dLon,
                CreatedAt = DateTime.Now
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Phát tín hiệu SignalR cho tất cả Shipper đang online (hoặc Client nghe sự kiện này)
            await _hubContext.Clients.All.SendAsync("NewOrderAvailable", order.OrderCode, order.PickupLatitude, order.PickupLongitude);

            return RedirectToAction("Tracking", new { id = order.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Tracking(int id)
        {
            var order = await _context.Orders
                .Include(o => o.Shipper)
                .FirstOrDefaultAsync(o => o.Id == id);
            
            if (order == null) return NotFound();
            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> SubmitReview(int orderId, int ratingMenu, int ratingShipper, string comment)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null) return NotFound();

            // Shipper và khách chỉ được đánh giá khi trạng thái đơn hàng từ Đang giao trở đi
            if (order.Status < OrderStatus.Delivering)
            {
                return BadRequest("Chỉ được đánh giá dịch vụ khi đơn hàng đang được giao hoặc đã hoàn thành!");
            }

            var review = new Review
            {
                OrderId = orderId,
                RatingMenu = ratingMenu,
                RatingShipper = ratingShipper,
                Comment = comment,
                CreatedAt = DateTime.Now
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Cảm ơn bạn đã đánh giá!" });
        }

        [HttpPost]
        public async Task<IActionResult> CalculateShippingFeeByAddress([FromBody] CalculateShippingRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.DeliveryAddress) || req.StoreId <= 0)
                return BadRequest("Dữ liệu không hợp lệ.");

            var store = await _context.Stores.FindAsync(req.StoreId);
            if (store == null || !store.IsOpen)
                return Json(new { success = false, message = "Nhà hàng không tồn tại hoặc đã đóng cửa." });

            // Giả lập tính tọa độ từ địa chỉ. Trong thực tế cần dùng Google Maps Geocoding API.
            // Ở đây ta random một tọa độ gần hồ chí minh cho Delivery.
            var rnd = new Random();
            double dLat = 10.762622 + (rnd.NextDouble() - 0.5) * 0.1;
            double dLon = 106.660172 + (rnd.NextDouble() - 0.5) * 0.1;

            double pLat = store.Latitude;
            double pLon = store.Longitude;

            // Dùng MapService hoặc Haversine để ước tính.
            var dist = _mapService.CalculateDistance(pLat, pLon, dLat, dLon);
            dist = Math.Round(dist, 1);

            // Bảng giá chung
            var standardService = await _context.DeliveryServices.FirstOrDefaultAsync(s => s.Name == "Tiêu chuẩn");
            decimal baseFee = standardService?.BaseFee ?? 15000m;
            decimal feePerKm = standardService?.FeePerKm ?? 5000m;

            decimal fee = baseFee + ((decimal)dist * feePerKm);
            fee = Math.Round(fee / 1000) * 1000;

            return Json(new { success = true, fee = fee, distance = dist, dLat = dLat, dLng = dLon });
        }

        [HttpPost]
        public async Task<IActionResult> ProcessCheckout([FromBody] ProcessCheckoutRequest req)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new { success = false, message = "Bạn chưa đăng nhập." });

            if (req == null || !req.Items.Any() || req.StoreId <= 0)
                return Json(new { success = false, message = "Giỏ hàng trống hoặc thiếu thông tin cửa hàng." });

            var store = await _context.Stores.FindAsync(req.StoreId);
            if (store == null || !store.IsOpen)
                return Json(new { success = false, message = "Nhà hàng hiện đang đóng cửa." });

            // Mock delivery coords
            var rnd = new Random();
            double dLat = 10.762622 + (rnd.NextDouble() - 0.5) * 0.1;
            double dLon = 106.660172 + (rnd.NextDouble() - 0.5) * 0.1;

            double dist = _mapService.CalculateDistance(store.Latitude, store.Longitude, dLat, dLon);
            dist = Math.Round(dist, 1);

            var standardService = await _context.DeliveryServices.FirstOrDefaultAsync(s => s.Name == "Tiêu chuẩn");
            decimal baseFee = standardService?.BaseFee ?? 15000m;
            decimal feePerKm = standardService?.FeePerKm ?? 5000m;
            decimal shippingFee = baseFee + ((decimal)dist * feePerKm);
            shippingFee = Math.Round(shippingFee / 1000) * 1000;

            var config = ShipperIncomeConfig.Load();
            decimal extraFee = (decimal)Math.Ceiling(Math.Max(dist - config.BaseDistance, 0.0)) * config.ExtraFeePerKm;
            decimal shipperIncome = config.BaseIncome + extraFee;

            decimal totalItemsPrice = 0;
            var orderItems = new List<OrderItem>();

            foreach (var item in req.Items)
            {
                var menuItem = await _context.MenuItems.FindAsync(item.MenuItemId);
                if (menuItem != null && menuItem.IsAvailable)
                {
                    totalItemsPrice += menuItem.Price * item.Quantity;
                    orderItems.Add(new OrderItem
                    {
                        MenuItemId = menuItem.Id,
                        Quantity = item.Quantity,
                        Price = menuItem.Price
                    });
                }
            }

            if (!orderItems.Any())
                return Json(new { success = false, message = "Các món ăn trong giỏ đã hết hàng hoặc không tồn tại." });

            var order = new Order
            {
                OrderCode = $"DH-{DateTime.Now:yyyyMMdd}-{rnd.Next(1000, 9999)}",
                UserId = user.Id,
                StoreId = store.Id,
                ServiceId = standardService?.Id,
                Status = OrderStatus.SearchingShipper,
                TotalPrice = totalItemsPrice,
                ShippingFee = shippingFee,
                ShipperIncome = shipperIncome,
                PickupAddress = store.Address ?? "TP. HCM",
                DeliveryAddress = req.DeliveryAddress,
                PickupLatitude = store.Latitude,
                PickupLongitude = store.Longitude,
                DeliveryLatitude = dLat,
                DeliveryLongitude = dLon,
                Distance = dist,
                Note = req.Note,
                CreatedAt = DateTime.Now,
                OrderItems = orderItems
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Notify shippers via SignalR
            await _hubContext.Clients.All.SendAsync("NewOrderAvailable", order.OrderCode, order.PickupLatitude, order.PickupLongitude);

            return Json(new { success = true, orderId = order.Id });
        }
    }

    public class CalculateShippingRequest
    {
        public int StoreId { get; set; }
        public string DeliveryAddress { get; set; } = string.Empty;
    }

    public class ProcessCheckoutRequest
    {
        public int StoreId { get; set; }
        public string DeliveryAddress { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public List<CheckoutItemRequest> Items { get; set; } = new List<CheckoutItemRequest>();
    }

    public class CheckoutItemRequest
    {
        public int MenuItemId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
}
