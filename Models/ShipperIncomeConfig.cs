using System.Text.Json;

namespace DeliveryHubWeb.Models
{
    public class ShipperIncomeConfig
    {
        public decimal BaseIncome { get; set; } = 15000m;
        public double BaseDistance { get; set; } = 3.0;
        public decimal ExtraFeePerKm { get; set; } = 3000m;

        private static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), "shipper_income_config.json");

        public static ShipperIncomeConfig Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<ShipperIncomeConfig>(json) ?? new ShipperIncomeConfig();
                }
                catch { }
            }
            return new ShipperIncomeConfig();
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}
