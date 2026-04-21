using DeliveryHubWeb.Data;
using DeliveryHubWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Hỗ trợ DateTime cũ của SQL Server chuyển sang PostgreSQL
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var connectionUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
string connectionString;

if (!string.IsNullOrEmpty(connectionUrl))
{
    // Parse Railway's postgresql:// URL
    var databaseUri = new Uri(connectionUrl);
    var userInfo = databaseUri.UserInfo.Split(':');
    
    connectionString = $"Host={databaseUri.Host};Port={databaseUri.Port};Database={databaseUri.LocalPath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Prefer;Trust Server Certificate=true;";
}
else
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

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
builder.Services.AddHttpClient<DeliveryHubWeb.Services.IAiService, DeliveryHubWeb.Services.AiService>();
builder.Services.AddHttpClient<DeliveryHubWeb.Services.IRouteOptimizationService, DeliveryHubWeb.Services.RouteOptimizationService>();
builder.Services.AddHostedService<DeliveryHubWeb.Services.PlatformBackgroundService>();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// 4. Seeding Data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try {
        var _context = services.GetRequiredService<ApplicationDbContext>();
        
        // Auto-run migrations on startup (important for Railway)
        await _context.Database.MigrateAsync();
        
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

app.Run();
