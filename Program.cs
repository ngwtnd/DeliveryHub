using DeliveryHubWeb.Data;
using DeliveryHubWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Database & Identity
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => {
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// 2. Add SignalR
builder.Services.AddSignalR();

// 3. MVC & Services
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<DeliveryHubWeb.Services.IMapService, DeliveryHubWeb.Services.MapService>();
builder.Services.AddHttpClient<DeliveryHubWeb.Services.IRouteOptimizationService, DeliveryHubWeb.Services.RouteOptimizationService>();

var app = builder.Build();

// 4. Seeding Data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try {
        var _context = services.GetRequiredService<ApplicationDbContext>();
        string checkPartnerCodeSql = @"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[AspNetUsers]') AND name = 'PartnerCode')
            BEGIN
                ALTER TABLE [AspNetUsers] ADD [PartnerCode] nvarchar(max) NULL;
            END";
        await _context.Database.ExecuteSqlRawAsync(checkPartnerCodeSql);
        
        await DbInitializer.Initialize(services);
    } catch (Exception ex) {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

// 5. HTTP Request Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<DeliveryHubWeb.Hubs.OrderHub>("/orderHub");
app.MapHub<DeliveryHubWeb.Hubs.ChatHub>("/chatHub");

app.Run("http://localhost:8080");

app.Urls.Add("http://0.0.0.0:8080");
