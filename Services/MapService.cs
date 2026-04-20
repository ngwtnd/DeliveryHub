using System;

namespace DeliveryHubWeb.Services
{
    public interface IMapService
    {
        double CalculateDistance(double lat1, double lon1, double lat2, double lon2);
        decimal CalculateShippingFee(double distanceKm);
        double CalculateMergedDistance(double userLat, double userLon, List<(double lat, double lon)> storeLocations);
    }

    public class MapService : IMapService
    {
        private const double EarthRadiusKm = 6371.0;
        private const decimal BaseFee = 15000; // 15,000 VND cho 2km đầu
        private const decimal PricePerKm = 5000; // 5,000 VND cho mỗi km tiếp theo

        public double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            lat1 = ToRadians(lat1);
            lat2 = ToRadians(lat2);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
            var c = 2 * Math.Asin(Math.Sqrt(a));
            
            // Giả lập quãng đường thực tế bằng cách nhân hệ số 1.3 (vì đường đi không thẳng)
            return EarthRadiusKm * c * 1.3;
        }

        public decimal CalculateShippingFee(double distanceKm)
        {
            if (distanceKm <= 2) return BaseFee;
            return BaseFee + (decimal)(distanceKm - 2) * PricePerKm;
        }

        private static double ToRadians(double angleIn10thofaDegree)
        {
            return (Math.PI / 180) * angleIn10thofaDegree;
        }

        // Logic gộp đơn thông minh
        public double CalculateMergedDistance(double userLat, double userLon, List<(double lat, double lon)> storeLocations)
        {
            if (storeLocations == null || storeLocations.Count == 0) return 0;
            
            // Tìm cửa hàng gần User nhất làm điểm dừng đầu tiên
            var sortedStores = storeLocations.OrderBy(s => CalculateDistance(userLat, userLon, s.lat, s.lon)).ToList();
            
            double totalDist = 0;
            double currentLat = userLat;
            double currentLon = userLon;

            foreach (var store in sortedStores)
            {
                totalDist += CalculateDistance(currentLat, currentLon, store.lat, store.lon);
                currentLat = store.lat;
                currentLon = store.lon;
            }

            return totalDist;
        }
    }
}
