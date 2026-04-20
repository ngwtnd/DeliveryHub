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
                return Json(new { success = false, message = "Dữ liệu không hợp lệ." });

            var store = await _context.Stores.FindAsync(req.StoreId);
            if (store == null || !store.IsOpen)
            {
                // Fallback dành cho dữ liệu mock (khi cart từ localstorage không có StoreId đúng)
                store = await _context.Stores.FirstOrDefaultAsync(s => s.IsOpen);
                if (store == null)
                    return Json(new { success = false, message = "Hệ thống tạm ngưng. Không có nhà hàng nào mở cửa." });
            }

            // Sử dụng tọa độ từ request được gửi lên (qua bản đồ/geocoding)
            double dLat = req.Lat;
            double dLon = req.Lng;

            // Nếu tọa độ không hợp lệ, set mặc định là trung tâm HCM
            if (dLat == 0 && dLon == 0)
            {
                dLat = 10.762622;
                dLon = 106.660172;
            }

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

            if (req == null || !req.Items.Any())
                return Json(new { success = false, message = "Giỏ hàng trống." });

            var rnd = new Random();
            var standardService = await _context.DeliveryServices.FirstOrDefaultAsync(s => s.Name == "Tiêu chuẩn");
            decimal baseFee = standardService?.BaseFee ?? 15000m;
            decimal feePerKm = standardService?.FeePerKm ?? 5000m;

            var config = Models.ShipperIncomeConfig.Load();

            // Nh\u00f3m m\u00f3n \u0103n theo StoreId
            var itemsByStore = req.Items.GroupBy(i => i.StoreId).ToList();
            var createdOrderIds = new List<int>();

            foreach (var storeGroup in itemsByStore)
            {
                var store = await _context.Stores.FindAsync(storeGroup.Key);
                if (store == null || !store.IsOpen)
                {
                    store = await _context.Stores.FirstOrDefaultAsync(s => s.IsOpen);
                    if (store == null) continue;
                }

                // Sử dụng tọa độ từ request
                double dLat = req.Lat;
                double dLon = req.Lng;
                if (dLat == 0 && dLon == 0)
                {
                    dLat = 10.762622;
                    dLon = 106.660172;
                }
                double dist = _mapService.CalculateDistance(store.Latitude, store.Longitude, dLat, dLon);
                dist = Math.Round(dist, 1);

                decimal shippingFee = baseFee + (decimal)dist * feePerKm;
                shippingFee = Math.Round(shippingFee / 1000) * 1000;

                decimal extraFee = (decimal)Math.Ceiling(Math.Max(dist - config.BaseDistance, 0.0)) * config.ExtraFeePerKm;
                decimal shipperIncome = config.BaseIncome + extraFee;

                decimal totalItemsPrice = 0;
                var orderItems = new List<OrderItem>();

                var firstValidMenuItemId = await _context.MenuItems.Select(m => m.Id).FirstOrDefaultAsync();

                foreach (var item in storeGroup)
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
                    else
                    {
                        // Fallback cho fake data từ trang chủ
                        totalItemsPrice += item.Price * item.Quantity;
                        orderItems.Add(new OrderItem
                        {
                            MenuItemId = firstValidMenuItemId > 0 ? firstValidMenuItemId : 1, // Avoid FK error if possible
                            Quantity = item.Quantity,
                            Price = item.Price
                        });
                    }
                }

                if (!orderItems.Any()) continue;

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
                createdOrderIds.Add(order.Id);

                // Notify shippers via SignalR
                await _hubContext.Clients.All.SendAsync("NewOrderAvailable", order.OrderCode, order.PickupLatitude, order.PickupLongitude);
            }

            if (!createdOrderIds.Any())
                return Json(new { success = false, message = "Không tạo được đơn hàng. Các cửa hàng có thể đã đóng cửa." });

            // Tr\u1ea3 v\u1ec1 orderId \u0111\u1ea7u ti\u00ean \u0111\u1ec3 redirect tracking
            return Json(new { success = true, orderId = createdOrderIds.First(), orderCount = createdOrderIds.Count });
        }
    }

    public class CalculateShippingRequest
    {
        public int StoreId { get; set; }
        public string DeliveryAddress { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
    }

    public class ProcessCheckoutRequest
    {
        public string DeliveryAddress { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string Note { get; set; } = string.Empty;
        public List<CheckoutItemRequest> Items { get; set; } = new List<CheckoutItemRequest>();
    }

    public class CheckoutItemRequest
    {
        public int MenuItemId { get; set; }
        public int StoreId { get; set; }  // mỗi item biết nó thuộc store nào
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
}
