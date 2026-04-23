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

        [HttpGet]
        public async Task<IActionResult> BatchTracking(int id)
        {
            var batch = await _context.BatchOrders
                .Include(b => b.Shipper)
                .Include(b => b.Items)
                    .ThenInclude(i => i.Order)
                        .ThenInclude(o => o!.Store)
                .Include(b => b.Items)
                    .ThenInclude(i => i.Order)
                        .ThenInclude(o => o!.OrderItems)
                            .ThenInclude(oi => oi.MenuItem)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (batch == null) return NotFound();
            return View(batch);
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
            var storeIds = req.StoreIds?.Where(id => id > 0).ToList();
            if (storeIds == null || storeIds.Count == 0)
            {
                if (req.StoreId > 0) storeIds = new List<int> { req.StoreId };
                else storeIds = new List<int>(); // Sẽ dùng fallback bên dưới
            }

            if (req == null || string.IsNullOrEmpty(req.DeliveryAddress))
                return Json(new { success = false, message = "Dữ liệu không hợp lệ." });

            var stores = await _context.Stores.Where(s => storeIds.Contains(s.Id) && s.IsOpen).ToListAsync();
            
            if (stores.Count == 0)
            {
                // Fallback dành cho dữ liệu mock (khi cart từ localstorage không có StoreId đúng)
                var store = await _context.Stores.FirstOrDefaultAsync(s => s.IsOpen);
                if (store == null)
                    return Json(new { success = false, message = "Hệ thống tạm ngưng. Không có nhà hàng nào mở cửa." });
                stores.Add(store);
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

            double dist = 0;
            if (stores.Count == 1) 
            {
                dist = _mapService.CalculateDistance(stores[0].Latitude, stores[0].Longitude, dLat, dLon);
            }
            else 
            {
                var locations = stores.Select(s => (s.Latitude, s.Longitude)).ToList();
                dist = _mapService.CalculateMergedDistance(dLat, dLon, locations);
            }
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
            var itemsByStore = req.Items.GroupBy(i => i.StoreId).ToList();
            var createdOrderIds = new List<int>();

            BatchOrder? batchOrder = null;
            decimal totalBatchShipperIncome = 0;
            if (itemsByStore.Count > 1) 
            {
                batchOrder = new BatchOrder
                {
                    BatchCode = $"BATCH-{DateTime.Now:yyyyMMdd}-{rnd.Next(1000, 9999)}",
                    UserId = user.Id,
                    Status = BatchOrderStatus.Created,
                    DeliveryAddress = req.DeliveryAddress,
                    DeliveryLatitude = req.Lat != 0 ? req.Lat : 10.762622,
                    DeliveryLongitude = req.Lng != 0 ? req.Lng : 106.660172,
                    CreatedAt = DateTime.Now
                };
                _context.BatchOrders.Add(batchOrder);
                await _context.SaveChangesAsync();
            }

            var storeLocations = new List<(double lat, double lon)>();
            int seq = 1;

            foreach (var storeGroup in itemsByStore)
            {
                Store? store = null;

                // 1. Tìm store theo StoreId được gửi lên
                if (storeGroup.Key > 0)
                    store = await _context.Stores.FindAsync(storeGroup.Key);

                // 2. Nếu storeId không hợp lệ, thử lấy store từ MenuItem đầu tiên trong nhóm
                if (store == null)
                {
                    var firstItem = storeGroup.First();
                    var menuItem = await _context.MenuItems
                        .Include(m => m.Store)
                        .FirstOrDefaultAsync(m => m.Id == firstItem.MenuItemId);
                    if (menuItem?.Store != null)
                        store = menuItem.Store;
                }

                // 3. Fallback cuối cùng: lấy bất kỳ store nào đang mở
                if (store == null)
                    store = await _context.Stores.FirstOrDefaultAsync(s => s.IsOpen && s.ActivityState == StoreActivityState.Active);

                // Vẫn không tìm được store hợp lệ → bỏ qua nhóm này
                if (store == null) continue;

                // Store đang đóng cửa → báo lỗi rõ ràng
                if (!store.IsOpen || store.ActivityState != StoreActivityState.Active)
                {
                    return Json(new { success = false, message = $"Nhà hàng '{store.Name}' hiện đang đóng cửa. Vui lòng thử lại sau." });
                }

                double dLat = req.Lat != 0 ? req.Lat : 10.762622;
                double dLon = req.Lng != 0 ? req.Lng : 106.660172;
                
                storeLocations.Add((store.Latitude, store.Longitude));
                
                double dist = Math.Round(_mapService.CalculateDistance(store.Latitude, store.Longitude, dLat, dLon), 1);
                decimal shippingFee = Math.Round((15000m + (decimal)dist * 5000m) / 1000) * 1000;
                decimal shipperIncome = 20000m + (decimal)Math.Ceiling(Math.Max(dist - 2.0, 0.0)) * 5000m;

                decimal totalItemsPrice = 0;
                var orderItems = new List<OrderItem>();

                foreach (var item in storeGroup)
                {
                    totalItemsPrice += item.Price * item.Quantity;
                    orderItems.Add(new OrderItem { MenuItemId = item.MenuItemId, Quantity = item.Quantity, Price = item.Price });
                }

                var order = new Order
                {
                    OrderCode = $"DH-{DateTime.Now:yyyyMMdd}-{rnd.Next(1000, 9999)}",
                    UserId = user.Id,
                    StoreId = store.Id,
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
                totalBatchShipperIncome += order.ShipperIncome;

                if (batchOrder != null) 
                {
                    _context.BatchOrderItems.Add(new BatchOrderItem { BatchOrderId = batchOrder.Id, OrderId = order.Id, Sequence = seq++ });
                }
                else 
                {
                    await _hubContext.Clients.All.SendAsync("NewOrderAvailable", order.OrderCode, order.PickupLatitude, order.PickupLongitude);
                }
            }

            if (!createdOrderIds.Any())
                return Json(new { success = false, message = "Không tạo được đơn hàng. Vui lòng kiểm tra lại giỏ hàng hoặc thử chọn nhà hàng khác." });

            if (batchOrder != null)
            {
                double dLat = req.Lat != 0 ? req.Lat : 10.762622;
                double dLon = req.Lng != 0 ? req.Lng : 106.660172;
                batchOrder.TotalDistance = Math.Round(_mapService.CalculateMergedDistance(dLat, dLon, storeLocations), 1);
                batchOrder.EstimatedMinutes = batchOrder.TotalDistance * 4;
                batchOrder.TotalIncome = totalBatchShipperIncome;
                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("NewBatchAvailable", batchOrder.BatchCode);
                return Json(new { success = true, batchId = batchOrder.Id, orderCount = createdOrderIds.Count, orderId = createdOrderIds.First() });
            }

            return Json(new { success = true, orderId = createdOrderIds.First(), orderCount = createdOrderIds.Count });
        }
    }

    public class CalculateShippingRequest
    {
        public int StoreId { get; set; }
        public List<int>? StoreIds { get; set; }
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
