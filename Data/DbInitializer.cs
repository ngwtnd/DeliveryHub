using DeliveryHubWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DeliveryHubWeb.Data
{
    public static class DbInitializer
    {
        /// <summary>Haversine distance in km</summary>
        private static double Haversine(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            var dLat = (lat2 - lat1) * Math.PI / 180.0;
            var dLon = (lon2 - lon1) * Math.PI / 180.0;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        /// <summary>Tính phí giao hàng theo khoảng cách thực tế</summary>
        private static decimal CalcShippingFee(double distKm)
        {
            // BaseFee 15.000 + 5.000/km (Giao đồ ăn)
            var fee = 15000m + (decimal)distKm * 5000m;
            return Math.Round(fee / 1000) * 1000; // làm tròn nghìn
        }

        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            using var context = new ApplicationDbContext(
                serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // ==========================================
            // 1. ROLES
            // ==========================================
            string[] roleNames = { "Admin", "Partner", "Shipper", "User", "RestaurantManager" };
            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                    await roleManager.CreateAsync(new IdentityRole(roleName));
            }

            const string demoPwd = "Demo@2026";

            // Helper: tạo hoặc cập nhật user
            async Task<ApplicationUser> EnsureUser(string email, string fullName, UserRole role, string phone,
                string? vehicle = null, string? vehicleType = null, string? citizenId = null, string? avatarUrl = null,
                bool isApproved = true, bool isActive = true, int? managedStoreId = null, string? shipperCode = null, string? partnerCode = null)
            {
                var existing = await userManager.FindByEmailAsync(email);
                if (existing != null)
                {
                    // Cập nhật password về Demo@2026 cho demo accounts
                    var token = await userManager.GeneratePasswordResetTokenAsync(existing);
                    await userManager.ResetPasswordAsync(existing, token, demoPwd);
                    existing.FullName = fullName;
                    existing.PhoneNumber = phone;
                    existing.IsApproved = isApproved;
                    existing.IsActive = isActive;
                    if (vehicle != null) existing.Vehicle = vehicle;
                    if (vehicleType != null) existing.VehicleType = vehicleType;
                    if (citizenId != null) existing.CitizenId = citizenId;
                    if (avatarUrl != null) existing.AvatarUrl = avatarUrl;
                    if (managedStoreId.HasValue) existing.ManagedStoreId = managedStoreId;
                    if (shipperCode != null) existing.ShipperCode = shipperCode;
                    if (partnerCode != null) existing.PartnerCode = partnerCode;
                    await userManager.UpdateAsync(existing);
                    return existing;
                }

                var user = new ApplicationUser
                {
                    UserName = email, Email = email, FullName = fullName,
                    Role = role, EmailConfirmed = true, PhoneNumber = phone,
                    IsApproved = isApproved, IsActive = isActive,
                    Vehicle = vehicle, VehicleType = vehicleType, CitizenId = citizenId,
                    AvatarUrl = avatarUrl ?? $"https://i.pravatar.cc/150?u={email}",
                    ManagedStoreId = managedStoreId,
                    ShipperCode = shipperCode, PartnerCode = partnerCode,
                    CreatedAt = DateTime.UtcNow
                };
                await userManager.CreateAsync(user, demoPwd);
                await userManager.AddToRoleAsync(user, role.ToString());
                return user;
            }

            // ==========================================
            // 2. ADMIN
            // ==========================================
            var admin = await EnsureUser("admin@deliveryhub.vn", "Nguyễn Quản Trị", UserRole.Admin, "0909000001");

            // ==========================================
            // 3. PARTNERS & STORES (realistic Vietnamese restaurants in HCMC)
            // ==========================================
            var partner1 = await EnsureUser("partner.bundau@deliveryhub.vn", "Nguyễn Văn Đối", UserRole.Partner, "0944555666", partnerCode: "DT-001");
            var partner2 = await EnsureUser("partner.pizza4ps@deliveryhub.vn", "Trần Thị Trang", UserRole.Partner, "0944998877", partnerCode: "DT-002");
            var partner3 = await EnsureUser("partner.pho@deliveryhub.vn", "Lê Văn Phở", UserRole.Partner, "0901239876", partnerCode: "DT-003");
            var partner4 = await EnsureUser("partner.coffee@deliveryhub.vn", "Trần Minh Cà Phê", UserRole.Partner, "0944123555", partnerCode: "DT-004");
            var partner5 = await EnsureUser("partner.comtam@deliveryhub.vn", "Phạm Thùy Tâm", UserRole.Partner, "0912345678", partnerCode: "DT-005");
            var partner6 = await EnsureUser("partner.banhmi@deliveryhub.vn", "Huỳnh Tiến Bánh", UserRole.Partner, "0987654321", partnerCode: "DT-006");
            var partner7 = await EnsureUser("partner.gogi@deliveryhub.vn", "Trương Hàn Quốc", UserRole.Partner, "0911223344", partnerCode: "DT-007");
            var partner8 = await EnsureUser("partner.bobay@deliveryhub.vn", "Đặng Thị Bò", UserRole.Partner, "0933445566", partnerCode: "DT-008");

            await context.SaveChangesAsync();

            // Helper: tạo store nếu chưa có
            async Task<Store> EnsureStore(string name, string desc, string addr, double lat, double lng,
                string ownerId, string? img = null, bool isMain = false, double rating = 4.5, int reviewCount = 0)
            {
                var existing = await context.Stores.FirstOrDefaultAsync(s => s.OwnerId == ownerId && s.Name == name && s.Description == desc);
                if (existing != null) return existing;

                var store = new Store
                {
                    Name = name, Description = desc, Address = addr,
                    Latitude = lat, Longitude = lng, OwnerId = ownerId,
                    ImageUrl = img ?? "https://images.unsplash.com/photo-1555396273-367ea4eb4db5?w=400&h=300&fit=crop",
                    IsMainBranch = isMain, IsOpen = true,
                    ActivityState = StoreActivityState.Active,
                    Rating = rating, ReviewCount = reviewCount,
                    CreatedAt = DateTime.UtcNow.AddDays(-new Random().Next(30, 365))
                };
                context.Stores.Add(store);
                await context.SaveChangesAsync();
                return store;
            }

            // === Partners & Stores ===
            var s1 = await EnsureStore("Bún Đậu Mắm Tôm Hà Nội", "Chi nhánh Quận 1 - Nguyễn Huệ", "15 Nguyễn Huệ, Bến Nghé, Quận 1", 10.77380, 106.70280, partner1.Id, "https://images.unsplash.com/photo-1541544741938-0af808871cc0?w=400&h=300&fit=crop", true, 4.5, 128);
            var s2 = await EnsureStore("Bún Đậu Mắm Tôm Hà Nội", "Chi nhánh Quận 3 - Tú Xương", "22 Tú Xương, Phường 7, Quận 3", 10.78680, 106.69170, partner1.Id, "https://images.unsplash.com/photo-1552566626-52f8b828add9?w=400&h=300&fit=crop", false, 4.3, 85);
            var s3 = await EnsureStore("Bún Đậu Mắm Tôm Hà Nội", "Chi nhánh Quận 7 - PMH", "Lotte Mart, Phú Mỹ Hưng, Quận 7", 10.72940, 106.71890, partner1.Id, "https://images.unsplash.com/photo-1517248135467-4c7edcad34c4?w=400&h=300&fit=crop", false, 4.2, 62);

            var s4 = await EnsureStore("Pizza 4P's", "Lê Thánh Tôn - Quận 1", "8/15 Lê Thánh Tôn, Bến Nghé, Quận 1", 10.77650, 106.70430, partner2.Id, "https://images.unsplash.com/photo-1574126154517-d1e0d89ef734?w=400&h=300&fit=crop", true, 4.8, 312);

            var s5 = await EnsureStore("Phở Thìn Bờ Hồ", "Chi nhánh Sài Gòn - SJC", "13 Nguyễn Du, Bến Nghé, Quận 1", 10.77590, 106.69880, partner3.Id, "https://images.unsplash.com/photo-1582878826629-29b7ad1cdc43?w=400&h=300&fit=crop", true, 4.6, 203);
            var s6 = await EnsureStore("Phở Thìn Bờ Hồ", "Chi nhánh Bình Thạnh", "55 Điện Biên Phủ, P.15, Bình Thạnh", 10.80150, 106.71230, partner3.Id, "https://images.unsplash.com/photo-1514362545857-3bc16c4c7d1b?w=400&h=300&fit=crop", false, 4.4, 156);

            var s7 = await EnsureStore("Highlands Coffee", "Landmark 81", "Vinhomes Central Park, Bình Thạnh", 10.79500, 106.72180, partner4.Id, "https://images.unsplash.com/photo-1495474472287-4d71bcdd2085?w=400&h=300&fit=crop", true, 4.3, 445);
            var s8 = await EnsureStore("Highlands Coffee", "Bitexco Tower", "2 Hải Triều, Bến Nghé, Quận 1", 10.77160, 106.70420, partner4.Id, "https://images.unsplash.com/photo-1501339847302-ac426a4a7cbb?w=400&h=300&fit=crop", false, 4.1, 287);

            var s9 = await EnsureStore("Cơm Tấm Bụi Sài Gòn", "Quận Bình Thạnh", "84 Nguyễn Huy Tưởng, P.6, Bình Thạnh", 10.80410, 106.70850, partner5.Id, "https://images.unsplash.com/photo-1567620905732-2d1ec7ab7445?w=400&h=300&fit=crop", true, 4.7, 521);

            var s10 = await EnsureStore("Bánh Mì Huỳnh Hoa", "Quận 1", "26 Lê Thị Riêng, Bến Thành, Quận 1", 10.77170, 106.69260, partner6.Id, "https://images.unsplash.com/photo-1509722747041-616f39b57569?w=400&h=300&fit=crop", true, 4.9, 1205);

            var s11 = await EnsureStore("GoGi House", "Hàn Quốc BBQ - Quận 1", "31 Lê Duẩn, Bến Nghé, Quận 1", 10.78180, 106.69930, partner7.Id, "https://images.unsplash.com/photo-1504674900247-0877df9cc836?w=400&h=300&fit=crop", true, 4.5, 342);

            var s12 = await EnsureStore("Bò Bảy Món Á Đông", "Quận 10", "246 Lý Thái Tổ, P.1, Quận 10", 10.76940, 106.67260, partner8.Id, "https://images.unsplash.com/photo-1414235077428-338989a2e8c0?w=400&h=300&fit=crop", true, 4.4, 189);

            // ==========================================
            // 4. SHIPPERS (20 shippers thực tế)
            // ==========================================
            string[] shipperNames = {
                "Trần Văn Tốc", "Lê Văn Nhận", "Cao Thị Uyên", "Kiều Văn Tùng", "Châu Văn Sáng",
                "Phan Thị Rung", "Hà Văn Quang", "Tô Văn Phúc", "Dương Thị Oanh", "Mai Văn Nam",
                "Phạm Văn Lực", "Nguyễn Văn Hùng", "Lê Thị Lan", "Bùi Văn Dũng", "Hoàng Văn Tuấn",
                "Đặng Văn Giang", "Vũ Văn Minh", "Phan Văn Bình", "Lý Văn Thanh", "Trịnh Văn Kỳ"
            };
            string[] vehicleList = {
                "Yamaha Exciter (59A2-56789)", "Honda Wave Alpha (59A2-45678)", "SYM Attila (59A2-34567)",
                "Honda Air Blade (59P1-12345)", "Honda Vision (59P1-67890)", "Suzuki Raider (59P1-33333)",
                "Honda Winner X (59P1-44444)", "Yamaha NVX 155 (59A2-55555)"
            };

            // Tọa độ shipper phân bố quanh TP.HCM
            double[][] shipperCoords = {
                new[]{10.7750, 106.7020}, new[]{10.7890, 106.6950}, new[]{10.7680, 106.6850},
                new[]{10.8010, 106.7100}, new[]{10.7600, 106.6720}, new[]{10.7830, 106.7150},
                new[]{10.7720, 106.6890}, new[]{10.7950, 106.7050}, new[]{10.7560, 106.6780},
                new[]{10.7870, 106.6980}, new[]{10.7740, 106.7070}, new[]{10.7660, 106.6900},
                new[]{10.7810, 106.6830}, new[]{10.7900, 106.7130}, new[]{10.7630, 106.6960},
                new[]{10.7780, 106.6770}, new[]{10.8000, 106.7000}, new[]{10.7700, 106.7080},
                new[]{10.7850, 106.6860}, new[]{10.7590, 106.7040}
            };

            var shipperList = new List<ApplicationUser>();
            for (int i = 0; i < shipperNames.Length; i++)
            {
                var email = $"shipper{i + 1}@deliveryhub.vn";
                var shipper = await EnsureUser(email, shipperNames[i], UserRole.Shipper, $"0903{(i + 1):D6}",
                    vehicle: vehicleList[i % vehicleList.Length],
                    vehicleType: "Xe máy",
                    citizenId: $"0790{10000000 + i}",
                    shipperCode: $"SP-{i + 1:D3}",
                    isActive: true
                );
                shipper.Latitude = shipperCoords[i][0];
                shipper.Longitude = shipperCoords[i][1];
                shipper.CitizenIdFrontImageUrl = "/images/default-cccd-front.png";
                shipper.CitizenIdBackImageUrl = "/images/default-cccd-back.png";
                shipper.DriverLicenseImageUrl = "/images/default-license.png";
                await userManager.UpdateAsync(shipper);
                shipperList.Add(shipper);
            }

            // ==========================================
            // 5. USERS (khách hàng)
            // ==========================================
            var user1 = await EnsureUser("user1@gmail.com", "Trần Thị Lan", UserRole.User, "0908889990");
            user1.Address = "Vinhomes Central Park, Bình Thạnh"; user1.Latitude = 10.7955; user1.Longitude = 106.7222;
            await userManager.UpdateAsync(user1);

            var user2 = await EnsureUser("user2@gmail.com", "Nguyễn Minh Tuấn", UserRole.User, "0977123456");
            user2.Address = "Sunrise City, Quận 7"; user2.Latitude = 10.7327; user2.Longitude = 106.7065;
            await userManager.UpdateAsync(user2);

            var user3 = await EnsureUser("user3@gmail.com", "Lê Hồng Phúc", UserRole.User, "0966789012");
            user3.Address = "The Manor, Bình Thạnh"; user3.Latitude = 10.7981; user3.Longitude = 106.7210;
            await userManager.UpdateAsync(user3);

            // ==========================================
            // 6. DEMO ACCOUNTS (chung mật khẩu Demo@2026)
            // ==========================================
            await EnsureUser("demo_admin@deliveryhub.vn", "Demo Admin", UserRole.Admin, "0999000001");
            await EnsureUser("demo_partner@deliveryhub.vn", "Demo Partner", UserRole.Partner, "0999000002", partnerCode: "DT-DEMO");
            var demoShipper = await EnsureUser("demo_shipper@deliveryhub.vn", "Demo Shipper", UserRole.Shipper, "0999000003",
                vehicle: "Honda Future (59A-11111)", vehicleType: "Xe máy", citizenId: "079099999999", shipperCode: "SP-DEMO");
            demoShipper.Latitude = 10.7726; demoShipper.Longitude = 106.6980;
            await userManager.UpdateAsync(demoShipper);
            await EnsureUser("demo_user@deliveryhub.vn", "Demo User", UserRole.User, "0999000004");

            // RestaurantManager demo sẽ cập nhật sau khi có store ID
            await context.SaveChangesAsync();

            // Cập nhật ManagedStoreId cho RestaurantManager demo
            var demoRm = await EnsureUser("demo_rm@deliveryhub.vn", "Demo QL Nhà Hàng", UserRole.RestaurantManager, "0999000005", managedStoreId: s1.Id);
            
            // Create Restaurant Managers for all other branches
            var allStoresList = new[] { s2, s3, s4, s5, s6, s7, s8, s9, s10, s11, s12 };
            int rmCounter = 1;
            foreach(var store in allStoresList)
            {
                var namePrefix = store.Name.Length > 15 ? store.Name.Substring(0, 15) : store.Name;
                await EnsureUser($"rm_branch{store.Id}@deliveryhub.vn", $"QL {namePrefix}", UserRole.RestaurantManager, $"0988{rmCounter:D6}", managedStoreId: store.Id);
                rmCounter++;
            }

            // ==========================================
            // 7. MENU ITEMS (realistic Vietnamese menus)
            // ==========================================
            if (!await context.MenuItems.AnyAsync())
            {
                var menus = new Dictionary<int, (string name, string desc, decimal price, string cat, string? img)[]>
                {
                    [s1.Id] = new[] {
                        ("Bún đậu mắm tôm đặc biệt", "Bún, đậu hũ chiên, mắm tôm pha lê, chả nem, thịt luộc", 65000m, "Món chính", "https://images.unsplash.com/photo-1585032226651-759b368d7246?w=400&h=300&fit=crop"),
                        ("Bún đậu thập cẩm", "Full topping: nem, chả cốm, dồi, lòng, thịt", 89000m, "Món chính", "https://images.unsplash.com/photo-1569058242253-92a9c755a0ec?w=400&h=300&fit=crop"),
                        ("Bún đậu chả cốm", "Bún, đậu hũ, chả cốm chiên giòn", 55000m, "Món chính", null),
                        ("Nem chua rán (5 cái)", "Nem chua chiên giòn kèm rau sống", 45000m, "Khai vị", null),
                        ("Trà đá", "Trà đá truyền thống", 5000m, "Đồ uống", null),
                        ("Nước chanh muối", "Chanh tươi muối Himalaya", 20000m, "Đồ uống", null),
                        ("Đậu hũ chiên thêm (5 miếng)", "Đậu hũ chiên vàng giòn", 15000m, "Phụ thêm", null),
                        ("Bún chả Hà Nội", "Bún chả nướng kèm nem rán, rau sống", 60000m, "Món chính", "https://images.unsplash.com/photo-1582878826629-29b7ad1cdc43?w=400&h=300&fit=crop"),
                    },
                    [s4.Id] = new[] {
                        ("Pizza Burrata", "Pizza với phô mai Burrata tươi, cà chua bi, basil", 219000m, "Món chính", "https://images.unsplash.com/photo-1574071318508-1cdbab80d002?w=400&h=300&fit=crop"),
                        ("Pizza Margherita", "Sốt cà chua, phô mai Mozzarella, húng quế", 169000m, "Món chính", null),
                        ("Salmon Sashimi Pizza", "Cá hồi tươi, wasabi cream, rong biển", 259000m, "Món chính", null),
                        ("Caesar Salad", "Rau xà lách, phô mai Parmesan, sốt Caesar", 109000m, "Khai vị", null),
                        ("Tiramisu", "Tiramisu truyền thống Ý", 89000m, "Tráng miệng", null),
                        ("Kem Matcha 4P's", "Kem Matcha tự làm", 69000m, "Tráng miệng", null),
                        ("Craft Beer 4P's", "Bia thủ công 330ml", 79000m, "Đồ uống", null),
                        ("Lemonade Sparkling", "Nước chanh có ga tự pha", 55000m, "Đồ uống", null),
                    },
                    [s5.Id] = new[] {
                        ("Phở bò tái lăn", "Phở bò tái lăn truyền thống Hà Nội", 65000m, "Món chính", "https://images.unsplash.com/photo-1582878826629-29b7ad1cdc43?w=400&h=300&fit=crop"),
                        ("Phở bò chín nạm", "Phở bò chín nạm đặc biệt", 60000m, "Món chính", null),
                        ("Phở gà ta", "Phở gà ta thả vườn, nước dùng trong", 55000m, "Món chính", null),
                        ("Phở bò viên", "Phở bò viên thủ công, dai giòn", 55000m, "Món chính", null),
                        ("Giò chả thêm", "Giò chả miếng kèm phở", 15000m, "Phụ thêm", null),
                        ("Quẩy (2 cái)", "Quẩy chiên giòn ăn kèm phở", 10000m, "Phụ thêm", null),
                        ("Trà đá", "Trà đá truyền thống", 5000m, "Đồ uống", null),
                    },
                    [s7.Id] = new[] {
                        ("Phin Freeze Trà Xanh", "Cà phê phin pha trà xanh matcha đá xay", 55000m, "Đồ uống", "https://images.unsplash.com/photo-1461023058943-07fcbe16d735?w=400&h=300&fit=crop"),
                        ("Phin Sữa Đá", "Cà phê phin sữa đặc đá", 39000m, "Đồ uống", null),
                        ("Freeze Trà Vải", "Trà vải đá xay", 55000m, "Đồ uống", null),
                        ("Bánh mì thịt nguội", "Bánh mì thịt nguội kiểu Việt", 35000m, "Đồ ăn nhẹ", null),
                        ("Croissant bơ", "Croissant bơ Pháp nướng giòn", 32000m, "Đồ ăn nhẹ", null),
                        ("Phin Đen Đá", "Cà phê phin đen đá", 35000m, "Đồ uống", null),
                    },
                    [s9.Id] = new[] {
                        ("Cơm tấm sườn bì chả", "Sườn nướng, bì, chả trứng, mỡ hành", 55000m, "Món chính", "https://images.unsplash.com/photo-1567620905732-2d1ec7ab7445?w=400&h=300&fit=crop"),
                        ("Cơm tấm sườn nướng", "Sườn heo nướng mỡ hành", 45000m, "Món chính", null),
                        ("Cơm tấm đặc biệt", "Sườn, bì, chả, trứng ốp la, thêm chả giò", 69000m, "Món chính", null),
                        ("Cơm tấm sườn chả cua", "Sườn nướng + chả cua hấp", 65000m, "Món chính", null),
                        ("Canh chua", "Canh chua cá lóc đồng", 25000m, "Khai vị", null),
                        ("Trà đá", "Trà đá Sài Gòn", 5000m, "Đồ uống", null),
                        ("Nước mía", "Nước mía tươi ép", 15000m, "Đồ uống", null),
                        ("Sữa đậu nành", "Sữa đậu nành tươi", 12000m, "Đồ uống", null),
                    },
                    [s10.Id] = new[] {
                        ("Bánh mì đặc biệt", "Thịt nguội, pate, bơ, rau, ớt, đồ chua", 47000m, "Món chính", "https://images.unsplash.com/photo-1509722747041-616f39b57569?w=400&h=300&fit=crop"),
                        ("Bánh mì thịt nướng", "Thịt nướng than hoa, rau sống", 42000m, "Món chính", null),
                        ("Bánh mì xíu mại", "Xíu mại sốt cà chua, pate", 40000m, "Món chính", null),
                        ("Bánh mì chả cá", "Chả cá Nha Trang chiên giòn", 38000m, "Món chính", null),
                        ("Nước ngọt lon", "Coca-Cola / Pepsi / 7Up", 12000m, "Đồ uống", null),
                    },
                    [s11.Id] = new[] {
                        ("Set nướng Hàn Quốc 2 người", "Thịt bò, thịt heo, rau sống, kim chi", 359000m, "Món chính", "https://images.unsplash.com/photo-1504674900247-0877df9cc836?w=400&h=300&fit=crop"),
                        ("Bibimbap bò", "Cơm trộn Hàn Quốc với thịt bò, rau, trứng", 109000m, "Món chính", null),
                        ("Tteokbokki", "Bánh gạo cay Hàn Quốc", 79000m, "Khai vị", null),
                        ("Tokbokki phô mai", "Bánh gạo sốt phô mai béo ngậy", 89000m, "Khai vị", null),
                        ("Mandu chiên (6 cái)", "Há cảo chiên giòn kiểu Hàn", 69000m, "Khai vị", null),
                        ("Soju bottle", "Rượu Soju Hàn Quốc 360ml", 99000m, "Đồ uống", null),
                        ("Kem bingsu đậu đỏ", "Bingsu truyền thống", 79000m, "Tráng miệng", null),
                    },
                    [s12.Id] = new[] {
                        ("Bò 7 món đặc biệt (2 người)", "Bò nướng, bò nhúng, bò lúc lắc, bò viên, bò xào, bò kho, cháo bò", 399000m, "Món chính", "https://images.unsplash.com/photo-1414235077428-338989a2e8c0?w=400&h=300&fit=crop"),
                        ("Bò lúc lắc", "Bò lúc lắc đá xiên nướng", 159000m, "Món chính", null),
                        ("Lẩu bò (2 người)", "Lẩu bò sa tế kiểu Á Đông", 219000m, "Món chính", null),
                        ("Bò nhúng dấm", "Thịt bò nhúng dấm kèm rau", 189000m, "Món chính", null),
                        ("Nước ép cam", "Cam tươi ép 100%", 28000m, "Đồ uống", null),
                    },
                };

                // Also add menu for branch stores (s2, s3, s6, s8) - clone from main
                menus[s2.Id] = menus[s1.Id]; // clone menu bún đậu
                menus[s3.Id] = menus[s1.Id];
                menus[s6.Id] = menus[s5.Id]; // clone menu phở
                menus[s8.Id] = menus[s7.Id]; // clone menu coffee

                foreach (var (storeId, items) in menus)
                {
                    foreach (var (name, desc, price, cat, img) in items)
                    {
                        if (!await context.MenuItems.AnyAsync(m => m.StoreId == storeId && m.Name == name))
                        {
                            context.MenuItems.Add(new MenuItem
                            {
                                StoreId = storeId, Name = name, Description = desc,
                                Price = price, Category = cat, IsAvailable = true, ImageUrl = img
                            });
                        }
                    }
                }
                await context.SaveChangesAsync();
            }

            // ==========================================
            // 8. DELIVERY SERVICES
            // ==========================================
            if (!context.DeliveryServices.Any())
            {
                context.DeliveryServices.AddRange(
                    new DeliveryService { Name = "Giao hàng nhanh", Description = "Giao hàng trong 1-2 giờ trong nội thành", Icon = "fa-bolt", IsActive = true, MaxWeightKg = 30, EstimatedMinutesPerKm = 4, BaseFee = 15000, FeePerKm = 5000, ExtraFeePerKg = 2000, VehicleType = "Xe máy", ServiceType = "Giao hàng" },
                    new DeliveryService { Name = "Giao hàng siêu tốc", Description = "Giao ngay trong 30 phút", Icon = "fa-rocket", IsActive = true, MaxWeightKg = 10, EstimatedMinutesPerKm = 2, BaseFee = 25000, FeePerKm = 8000, ExtraFeePerKg = 3000, VehicleType = "Xe máy", ServiceType = "Giao hàng" },
                    new DeliveryService { Name = "Giao hàng trong ngày", Description = "Giao trong ngày đặt hàng", Icon = "fa-clock", IsActive = true, MaxWeightKg = 50, EstimatedMinutesPerKm = 6, BaseFee = 12000, FeePerKm = 4000, ExtraFeePerKg = 1500, VehicleType = "Xe máy", ServiceType = "Giao hàng" },
                    new DeliveryService { Name = "Giao đồ ăn", Description = "Giao đồ ăn nhanh chóng, giữ nhiệt", Icon = "fa-burger", IsActive = true, MaxWeightKg = 0, EstimatedMinutesPerKm = 3, BaseFee = 15000, FeePerKm = 5000, ExtraFeePerKg = 0, VehicleType = "Tất cả", ServiceType = "Giao đồ ăn" }
                );
                await context.SaveChangesAsync();
            }

            // ==========================================
            // 9. VOUCHERS
            // ==========================================
            if (!context.Vouchers.Any())
            {
                context.Vouchers.AddRange(
                    new Voucher { Code = "VIP10", ProgramName = "Ưu đãi VIP 10%", IsPercentage = true, DiscountValue = 10, MaxDiscountValue = 80000, MinOrderValue = 0, AppliesToShipping = false, UsedCount = 0, MaxUsageCount = 50, StartDate = DateTime.SpecifyKind(new DateTime(2026, 3, 21), DateTimeKind.Utc), EndDate = DateTime.SpecifyKind(new DateTime(2026, 6, 26), DateTimeKind.Utc), IsActive = true },
                    new Voucher { Code = "FREESHIP", ProgramName = "Miễn phí giao hàng", IsPercentage = true, DiscountValue = 100, MaxDiscountValue = null, MinOrderValue = 0, AppliesToShipping = true, UsedCount = 0, MaxUsageCount = 500, StartDate = DateTime.SpecifyKind(new DateTime(2026, 3, 13), DateTimeKind.Utc), EndDate = DateTime.SpecifyKind(new DateTime(2026, 7, 27), DateTimeKind.Utc), IsActive = true },
                    new Voucher { Code = "WELCOME50", ProgramName = "Chào mừng khách hàng mới", IsPercentage = false, DiscountValue = 50000, MaxDiscountValue = null, MinOrderValue = 100000, AppliesToShipping = false, UsedCount = 0, MaxUsageCount = 1000, StartDate = DateTime.SpecifyKind(new DateTime(2026, 2, 26), DateTimeKind.Utc), EndDate = DateTime.SpecifyKind(new DateTime(2026, 8, 27), DateTimeKind.Utc), IsActive = true },
                    new Voucher { Code = "GIAM20", ProgramName = "Giảm 20% phí giao", IsPercentage = true, DiscountValue = 20, MaxDiscountValue = 50000, MinOrderValue = 50000, AppliesToShipping = true, UsedCount = 0, MaxUsageCount = 200, StartDate = DateTime.SpecifyKind(new DateTime(2026, 3, 18), DateTimeKind.Utc), EndDate = DateTime.SpecifyKind(new DateTime(2026, 5, 17), DateTimeKind.Utc), IsActive = true }
                );
                await context.SaveChangesAsync();
            }

            // ==========================================
            // 10. ORDERS & STATISTICS (Enhanced for PostgreSQL)
            // ==========================================
            var currentOrderCount = await context.Orders.CountAsync();
            if (currentOrderCount < 100)
            {
                // Reset orders to ensure quality demo data
                context.BatchOrderItems.RemoveRange(context.BatchOrderItems);
                context.BatchOrders.RemoveRange(context.BatchOrders);
                context.Reviews.RemoveRange(context.Reviews);
                context.OrderItems.RemoveRange(context.OrderItems);
                context.Orders.RemoveRange(context.Orders);
                await context.SaveChangesAsync();

                var allStores = await context.Stores.ToListAsync();
                var allMenuItems = await context.MenuItems.ToListAsync();
                var allUsers = new[] { user1, user2, user3 };
                var rng = new Random(42);

                var deliveryAddresses = new (string addr, double lat, double lng)[] {
                    ("Vinhomes Central Park, Bình Thạnh", 10.7955, 106.7222),
                    ("Sunrise City, Quận 7", 10.7327, 106.7065),
                    ("The Manor, Bình Thạnh", 10.7981, 106.7210),
                    ("Chung cư Saigon Pearl, Bình Thạnh", 10.7889, 106.7195),
                    ("Chung cư Estella Heights, Quận 2", 10.7910, 106.7423),
                    ("168 Phan Xích Long, Phú Nhuận", 10.7997, 106.6878),
                    ("250 Nguyễn Trãi, Quận 1", 10.7621, 106.6866),
                    ("55 Bạch Đằng, Bình Thạnh", 10.8027, 106.7077),
                    ("KĐT Sala, Quận 2", 10.7723, 106.7284),
                    ("Chung cư Sky Garden, Quận 7", 10.7298, 106.7076),
                };

                string[] sampleNotes = { "Giao tận cửa", "Gọi trước khi đến", "Không lấy hành", "Thêm tương ớt", null! };

                // Tạo 400 đơn hàng lịch sử (từ tết 2026 đến nay)
                for (int i = 1; i <= 400; i++)
                {
                    // Chọn nhà hàng (Bias 30% cho s1 và 30% cho s10 để tạo Top)
                    Store store;
                    int roll = rng.Next(100);
                    if (roll < 30) store = s1;
                    else if (roll < 60) store = s10;
                    else store = allStores[rng.Next(allStores.Count)];

                    var storeItems = allMenuItems.Where(m => m.StoreId == store.Id).ToList();
                    if (!storeItems.Any()) continue;

                    var customer = allUsers[rng.Next(allUsers.Length)];
                    var delivery = deliveryAddresses[rng.Next(deliveryAddresses.Length)];
                    var dist = Math.Round(Haversine(store.Latitude, store.Longitude, delivery.lat, delivery.lng), 1);
                    var shippingFee = CalcShippingFee(dist);

                    // Trạng thái (Phần lớn là Completed)
                    OrderStatus status = OrderStatus.Completed;
                    if (i % 20 == 0) status = OrderStatus.Cancelled;
                    else if (i % 50 == 0) status = OrderStatus.Failed;

                    // Shipper (Bias cho Shipper 1 - Trần Văn Tốc)
                    ApplicationUser? shipper = roll < 40 ? shipperList[0] : shipperList[rng.Next(shipperList.Count)];

                    var createdAt = DateTime.Now.AddDays(-rng.Next(1, 100)).AddHours(rng.Next(8, 22));

                    var order = new Order
                    {
                        OrderCode = $"DH-H-{i:D4}",
                        UserId = customer.Id,
                        StoreId = store.Id,
                        ShipperId = shipper.Id,
                        Status = status,
                        CreatedAt = createdAt,
                        ShippingFee = shippingFee,
                        TotalPrice = 0, // Tính sau
                        Distance = dist,
                        PickupAddress = store.Address ?? "Store",
                        DeliveryAddress = delivery.addr,
                        PickupLatitude = store.Latitude, PickupLongitude = store.Longitude,
                        DeliveryLatitude = delivery.lat, DeliveryLongitude = delivery.lng,
                        ShipperIncome = Math.Round(shippingFee * 0.8m / 1000) * 1000,
                        Note = sampleNotes[rng.Next(sampleNotes.Length)],
                        PaymentMethod = rng.Next(2) == 0 ? PaymentMethod.Cash : PaymentMethod.Wallet,
                        CompletedAt = status == OrderStatus.Completed ? createdAt.AddMinutes(rng.Next(30, 60)) : null,
                        CancelledAt = status == OrderStatus.Cancelled ? createdAt.AddMinutes(rng.Next(5, 15)) : null
                    };

                    context.Orders.Add(order);
                    await context.SaveChangesAsync();

                    // Món ăn
                    var itemsCount = rng.Next(1, 4);
                    var selected = storeItems.OrderBy(x => rng.Next()).Take(itemsCount).ToList();
                    decimal total = 0;
                    foreach (var mi in selected)
                    {
                        var qty = rng.Next(1, 4);
                        context.OrderItems.Add(new OrderItem { OrderId = order.Id, MenuItemId = mi.Id, Quantity = qty, Price = mi.Price });
                        total += mi.Price * qty;
                    }
                    order.TotalPrice = total;
                    await context.SaveChangesAsync();

                    // Đánh giá (70% đơn Completed có review)
                    if (status == OrderStatus.Completed && rng.Next(100) < 70)
                    {
                        context.Reviews.Add(new Review {
                            OrderId = order.Id, ShipperId = order.ShipperId, StoreId = order.StoreId,
                            RatingMenu = rng.Next(4, 6), RatingShipper = rng.Next(4, 6),
                            Comment = "Hài lòng, dịch vụ rất tốt!", CreatedAt = order.CompletedAt ?? DateTime.Now,
                            Type = ReviewType.Customer
                        });
                    }
                }

                // Tạo 30 đơn hàng "MỚI NỔ" (Live Data)
                for (int i = 1; i <= 30; i++)
                {
                    var store = allStores[rng.Next(allStores.Count)];
                    var storeItems = allMenuItems.Where(m => m.StoreId == store.Id).ToList();
                    var customer = allUsers[rng.Next(allUsers.Length)];
                    var delivery = deliveryAddresses[rng.Next(deliveryAddresses.Length)];
                    var dist = Math.Round(Haversine(store.Latitude, store.Longitude, delivery.lat, delivery.lng), 1);
                    var shippingFee = CalcShippingFee(dist);

                    OrderStatus status = (OrderStatus)rng.Next(0, 5); // Pending -> Delivering
                    ApplicationUser? shipper = status >= OrderStatus.Accepted ? shipperList[rng.Next(shipperList.Count)] : null;

                    var order = new Order {
                        OrderCode = $"DH-LIVE-{i:D3}", UserId = customer.Id, StoreId = store.Id, ShipperId = shipper?.Id,
                        Status = status, CreatedAt = DateTime.Now.AddMinutes(-rng.Next(5, 500)),
                        ShippingFee = shippingFee, TotalPrice = 0, Distance = dist,
                        PickupAddress = store.Address ?? "Store", DeliveryAddress = delivery.addr,
                        PickupLatitude = store.Latitude, PickupLongitude = store.Longitude,
                        DeliveryLatitude = delivery.lat, DeliveryLongitude = delivery.lng,
                        ShipperIncome = Math.Round(shippingFee * 0.8m / 1000) * 1000,
                        Note = "Đơn hàng demo mới"
                    };
                    context.Orders.Add(order);
                    await context.SaveChangesAsync();
                    
                    var mi = storeItems[rng.Next(storeItems.Count)];
                    context.OrderItems.Add(new OrderItem { OrderId = order.Id, MenuItemId = mi.Id, Quantity = 1, Price = mi.Price });
                    order.TotalPrice = mi.Price;
                    await context.SaveChangesAsync();
                }
            }

            // ==========================================
            // 11. BATCH ORDERS (Seeding for optimization demo)
            // ==========================================
            if (!await context.BatchOrders.AnyAsync())
            {
                var shipper = shipperList[0];
                var batch = new BatchOrder {
                    BatchCode = "BATCH-001", ShipperId = shipper.Id, Status = BatchOrderStatus.InProgress,
                    CreatedAt = DateTime.Now.AddMinutes(-30), TotalDistance = 5.5, EstimatedMinutes = 20,
                    DeliveryAddress = "Phan Xích Long, Phú Nhuận", DeliveryLatitude = 10.7997, DeliveryLongitude = 106.6878
                };
                context.BatchOrders.Add(batch);
                await context.SaveChangesAsync();

                // Gán 2 đơn hàng Delivering vào batch này
                var liveOrders = await context.Orders.Where(o => o.OrderCode.StartsWith("DH-LIVE") && o.Status == OrderStatus.Delivering).Take(2).ToListAsync();
                int seq = 1;
                foreach(var o in liveOrders) {
                    context.BatchOrderItems.Add(new BatchOrderItem { BatchOrderId = batch.Id, OrderId = o.Id, Sequence = seq++, IsPickedUp = true, PickedUpAt = DateTime.Now.AddMinutes(-10) });
                }
                await context.SaveChangesAsync();
            }
        }
    }
}
