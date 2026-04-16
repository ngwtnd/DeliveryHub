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
    }
}
