using DeliveryHubWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DeliveryHubWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly Data.ApplicationDbContext _context;

        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, Data.ApplicationDbContext context)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, bool rememberMe)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                var result = await _signInManager.PasswordSignInAsync(user.UserName ?? email, password, rememberMe, false);
                if (result.Succeeded)
                {
                    return user.Role switch
                    {
                        UserRole.Admin => RedirectToAction("Index", "Admin"),
                        UserRole.Partner => RedirectToAction("Index", "Partner"),
                        UserRole.Shipper => RedirectToAction("Index", "Shipper"),
                        UserRole.RestaurantManager => RedirectToAction("Index", "RestaurantManager"),
                        _ => RedirectToAction("Index", "Home")
                    };
                }
            }

            ModelState.AddModelError("", "Email hoặc mật khẩu không đúng.");
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");
            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> Profile(string FullName, string Email, string PhoneNumber, string AvatarUrl, string NewPassword, IFormFile? avatarFile)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            if (avatarFile != null && avatarFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(avatarFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await avatarFile.CopyToAsync(stream);
                }

                user.AvatarUrl = "/uploads/avatars/" + fileName;
            }
            else if (AvatarUrl != null)
            {
                user.AvatarUrl = AvatarUrl;
            }

            user.FullName = FullName;
            
            if (user.Email != Email) {
                await _userManager.SetEmailAsync(user, Email);
                await _userManager.SetUserNameAsync(user, Email);
            }
            user.PhoneNumber = PhoneNumber;

            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded && !string.IsNullOrEmpty(NewPassword))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                await _userManager.ResetPasswordAsync(user, token, NewPassword);
            }

            TempData["SuccessMessage"] = "Cập nhật hồ sơ thành công!";
            return RedirectToAction("Profile");
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        // =============================================================
        // ĐĂNG KÝ USER (Khách hàng)
        // =============================================================
        [HttpGet]
        public IActionResult RegisterUser() => View();

        [HttpPost]
        public async Task<IActionResult> RegisterUser(string fullName, string email, string password, string phoneNumber, string? address)
        {
            if (await _userManager.FindByEmailAsync(email) != null)
            {
                ModelState.AddModelError("", "Email đã được sử dụng.");
                return View();
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                Address = address,
                Role = UserRole.User,
                IsApproved = true, // User không cần duyệt
                IsActive = true,
                Balance = 500000m, // Tặng 500k vào ví cho user mới
                CreatedAt = DateTime.UtcNow.AddHours(7)
            };

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, "User");
                TempData["SuccessMessage"] = "Đăng ký tài khoản thành công! Bạn được tặng 500.000đ vào ví. Hãy đăng nhập để bắt đầu.";
                return RedirectToAction("Login");
            }
            foreach (var err in result.Errors) ModelState.AddModelError("", err.Description);
            return View();
        }

        // =============================================================
        // ĐĂNG KÝ SHIPPER
        // =============================================================
        [HttpGet]
        public IActionResult RegisterShipper() => View();

        [HttpPost]
        public async Task<IActionResult> RegisterShipper(string fullName, string email, string password, string phoneNumber, string vehicleType, string vehicle, string citizenId, IFormFile citizenIdFront, IFormFile citizenIdBack, IFormFile driverLicense, IFormFile avatarFile)
        {
            if (await _userManager.FindByEmailAsync(email) != null) {
                ModelState.AddModelError("", "Email đã được sử dụng.");
                return View();
            }

            var user = new ApplicationUser {
                UserName = email,
                Email = email,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                Role = UserRole.Shipper,
                IsApproved = false,
                IsActive = true,
                Vehicle = vehicle,
                VehicleType = vehicleType,
                CitizenId = citizenId
            };

            async Task<string?> SaveFile(IFormFile? file) {
                if (file == null || file.Length == 0) return null;
                var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "registrations");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                using (var stream = new FileStream(Path.Combine(folder, fileName), FileMode.Create)) {
                    await file.CopyToAsync(stream);
                }
                return "/uploads/registrations/" + fileName;
            }

            user.CitizenIdFrontImageUrl = await SaveFile(citizenIdFront);
            user.CitizenIdBackImageUrl = await SaveFile(citizenIdBack);
            user.DriverLicenseImageUrl = await SaveFile(driverLicense);
            user.AvatarUrl = await SaveFile(avatarFile) ?? "/images/default-avatar.png";

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded) {
                await _userManager.AddToRoleAsync(user, "Shipper");
                
                var existing = await _context.Notifications
                    .FirstOrDefaultAsync(n => n.RecipientId == null && n.Type == NotificationType.ShipperRegistration && n.RelatedId == user.Id);
                if (existing == null)
                {
                    _context.Notifications.Add(new Notification {
                        Title = "Đăng ký Shipper chờ duyệt",
                        Message = $"Hồ sơ mới: {user.FullName} ({user.PhoneNumber})",
                        Type = NotificationType.ShipperRegistration,
                        RelatedId = user.Id,
                        TargetUrl = $"/Admin/ShipperDetail/{user.Id}",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow.AddHours(7)
                    });
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = "Gửi hồ sơ đăng ký Shipper thành công. Vui lòng chờ xét duyệt từ Admin.";
                return RedirectToAction("Login");
            }
            foreach (var err in result.Errors) ModelState.AddModelError("", err.Description);
            return View();
        }

        // =============================================================
        // ĐĂNG KÝ ĐỐI TÁC
        // =============================================================
        [HttpGet]
        public IActionResult RegisterPartner() => View();

        [HttpPost]
        public async Task<IActionResult> RegisterPartner(string fullName, string email, string password, string phoneNumber, string storeName, string storeAddress, IFormFile shopAvatar, IFormFile ownerAvatar)
        {
            if (await _userManager.FindByEmailAsync(email) != null) {
                ModelState.AddModelError("", "Email đã được sử dụng.");
                return View();
            }

            var user = new ApplicationUser {
                UserName = email,
                Email = email,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                Role = UserRole.Partner,
                IsApproved = false,
                IsActive = true
            };

            async Task<string?> SaveFile(IFormFile? file) {
                if (file == null || file.Length == 0) return null;
                var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "registrations");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                using (var stream = new FileStream(Path.Combine(folder, fileName), FileMode.Create)) {
                    await file.CopyToAsync(stream);
                }
                return "/uploads/registrations/" + fileName;
            }

            user.ShopAvatarUrl = await SaveFile(shopAvatar);
            user.AvatarUrl = await SaveFile(ownerAvatar) ?? "/images/default-avatar.png";

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded) {
                await _userManager.AddToRoleAsync(user, "Partner");
                
                var store = new Store {
                    Name = storeName,
                    Address = storeAddress,
                    OwnerId = user.Id,
                    IsMainBranch = true,
                    ActivityState = StoreActivityState.LockedByPartner,
                    IsOpen = false,
                    CreatedAt = DateTime.UtcNow.AddHours(7),
                    ImageUrl = user.ShopAvatarUrl ?? "/images/default-store.png"
                };
                
                _context.Stores.Add(store);
                await _context.SaveChangesAsync();

                var now = DateTime.UtcNow.AddHours(7);
                var existingPartnerNotif = await _context.Notifications
                    .FirstOrDefaultAsync(n => n.RecipientId == null && n.Type == NotificationType.PartnerRegistration && n.RelatedId == user.Id);
                if (existingPartnerNotif == null)
                {
                    _context.Notifications.Add(new Notification {
                        Title = "Đăng ký Đối tác chờ duyệt",
                        Message = $"Đối tác mới: {user.FullName}",
                        Type = NotificationType.PartnerRegistration,
                        RelatedId = user.Id,
                        TargetUrl = $"/Admin/Merchants?openPartnerId={user.Id}",
                        IsRead = false,
                        CreatedAt = now
                    });
                    await _context.SaveChangesAsync();
                }
                
                TempData["SuccessMessage"] = "Gửi hồ sơ đăng ký Đối tác thành công. Vui lòng chờ xét duyệt.";
                return RedirectToAction("Login");
            }
            foreach (var err in result.Errors) ModelState.AddModelError("", err.Description);
            return View();
        }

        public IActionResult AccessDenied() => View();
    }
}
