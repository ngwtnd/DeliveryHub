using System.Text;
using System.Text.Json;

namespace DeliveryHubWeb.Services
{
    public interface IRouteOptimizationService
    {
        Task<RouteOptimizationResult> OptimizeRoute(
            (double lat, double lng) shipperLocation,
            List<(int orderId, string storeName, double lat, double lng)> pickupPoints,
            (double lat, double lng) deliveryLocation);
    }

    public class RouteOptimizationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public double TotalDistanceKm { get; set; }
        public double EstimatedMinutes { get; set; }
        public List<OptimizedStop> Stops { get; set; } = new();
        public string? RouteGeoJson { get; set; }
    }

    public class OptimizedStop
    {
        public int OrderId { get; set; }
        public string StoreName { get; set; } = "";
        public int Sequence { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public double DistanceFromPrev { get; set; } // km từ điểm trước
    }

    public class RouteOptimizationService : IRouteOptimizationService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public RouteOptimizationService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["OpenRouteService:ApiKey"] ?? "";
        }

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

        /// <summary>
        /// Nearest Neighbor Algorithm: luôn chọn điểm gần nhất chưa thăm.
        /// Đây là fallback khi API fail.
        /// </summary>
        private RouteOptimizationResult NearestNeighborOptimize(
            (double lat, double lng) start,
            List<(int orderId, string storeName, double lat, double lng)> points,
            (double lat, double lng) end)
        {
            var result = new RouteOptimizationResult { Success = true };
            var remaining = new List<int>(Enumerable.Range(0, points.Count));
            double currentLat = start.lat, currentLng = start.lng;
            double totalDist = 0;
            int seq = 1;

            while (remaining.Count > 0)
            {
                // Tìm điểm gần nhất chưa thăm
                double minDist = double.MaxValue;
                int bestIdx = -1;

                foreach (var idx in remaining)
                {
                    var d = Haversine(currentLat, currentLng, points[idx].lat, points[idx].lng);
                    if (d < minDist) { minDist = d; bestIdx = idx; }
                }

                if (bestIdx == -1) break;

                var p = points[bestIdx];
                totalDist += minDist;
                result.Stops.Add(new OptimizedStop
                {
                    OrderId = p.orderId,
                    StoreName = p.storeName,
                    Sequence = seq++,
                    Lat = p.lat,
                    Lng = p.lng,
                    DistanceFromPrev = Math.Round(minDist, 1)
                });

                currentLat = p.lat;
                currentLng = p.lng;
                remaining.Remove(bestIdx);
            }

            // Cộng khoảng cách đến điểm giao cuối
            totalDist += Haversine(currentLat, currentLng, end.lat, end.lng);

            result.TotalDistanceKm = Math.Round(totalDist, 1);
            result.EstimatedMinutes = Math.Round(totalDist * 3.0, 0); // ~3 phút/km đô thị
            return result;
        }

        public async Task<RouteOptimizationResult> OptimizeRoute(
            (double lat, double lng) shipperLocation,
            List<(int orderId, string storeName, double lat, double lng)> pickupPoints,
            (double lat, double lng) deliveryLocation)
        {
            var result = new RouteOptimizationResult();

            try
            {
                // =========== STEP 1: Try ORS Optimization API ===========
                var jobs = pickupPoints.Select((p, i) => new
                {
                    id = i + 1,
                    location = new[] { p.lng, p.lat },
                    service = 300
                }).ToArray();

                var optimizationBody = new
                {
                    jobs = jobs,
                    vehicles = new[]
                    {
                        new
                        {
                            id = 1,
                            profile = "driving-car",
                            start = new[] { shipperLocation.lng, shipperLocation.lat },
                            end = new[] { deliveryLocation.lng, deliveryLocation.lat }
                        }
                    }
                };

                var optimizationJson = JsonSerializer.Serialize(optimizationBody);
                var optimizationRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openrouteservice.org/optimization")
                {
                    Content = new StringContent(optimizationJson, Encoding.UTF8, "application/json")
                };
                optimizationRequest.Headers.TryAddWithoutValidation("Authorization", _apiKey);

                var optimizationResponse = await _httpClient.SendAsync(optimizationRequest);
                var optimizationContent = await optimizationResponse.Content.ReadAsStringAsync();

                bool orsSuccess = false;

                if (optimizationResponse.IsSuccessStatusCode)
                {
                    var optimizationData = JsonDocument.Parse(optimizationContent);

                    if (!optimizationData.RootElement.TryGetProperty("error", out _)
                        && optimizationData.RootElement.TryGetProperty("routes", out var routes)
                        && routes.GetArrayLength() > 0)
                    {
                        var route = routes[0];
                        var steps = route.GetProperty("steps");

                        double distanceMeters = 0, durationSeconds = 0;

                        if (route.TryGetProperty("distance", out var distProp))
                            distanceMeters = distProp.GetDouble();
                        else if (route.TryGetProperty("summary", out var sp) && sp.TryGetProperty("distance", out var sdp))
                            distanceMeters = sdp.GetDouble();

                        if (route.TryGetProperty("duration", out var durProp))
                            durationSeconds = durProp.GetDouble();
                        else if (route.TryGetProperty("summary", out var sp2) && sp2.TryGetProperty("duration", out var sdp2))
                            durationSeconds = sdp2.GetDouble();

                        result.TotalDistanceKm = Math.Round(distanceMeters / 1000.0, 1);
                        result.EstimatedMinutes = Math.Round(durationSeconds / 60.0, 0);

                        // Parse optimized order
                        int seq = 1;
                        double prevLat = shipperLocation.lat, prevLng = shipperLocation.lng;

                        foreach (var step in steps.EnumerateArray())
                        {
                            var stepType = step.GetProperty("type").GetString();
                            if (stepType == "job")
                            {
                                int jobId = 0;
                                if (step.TryGetProperty("id", out var idProp)) jobId = idProp.GetInt32();
                                else if (step.TryGetProperty("job", out var jobProp)) jobId = jobProp.GetInt32();

                                if (jobId > 0 && jobId <= pickupPoints.Count)
                                {
                                    var pickup = pickupPoints[jobId - 1];
                                    var distFromPrev = Haversine(prevLat, prevLng, pickup.lat, pickup.lng);
                                    result.Stops.Add(new OptimizedStop
                                    {
                                        OrderId = pickup.orderId,
                                        StoreName = pickup.storeName,
                                        Sequence = seq++,
                                        Lat = pickup.lat,
                                        Lng = pickup.lng,
                                        DistanceFromPrev = Math.Round(distFromPrev, 1)
                                    });
                                    prevLat = pickup.lat;
                                    prevLng = pickup.lng;
                                }
                            }
                        }

                        orsSuccess = result.Stops.Count > 0;
                    }
                }

                // =========== FALLBACK: Nearest Neighbor ===========
                if (!orsSuccess)
                {
                    Console.WriteLine("[ROUTE] ORS API failed, using Nearest Neighbor fallback.");
                    result = NearestNeighborOptimize(shipperLocation, pickupPoints, deliveryLocation);
                }

                // =========== STEP 2: Get GeoJSON polyline ===========
                try
                {
                    var waypoints = new List<double[]>();
                    waypoints.Add(new[] { shipperLocation.lng, shipperLocation.lat });

                    foreach (var stop in result.Stops.OrderBy(s => s.Sequence))
                        waypoints.Add(new[] { stop.Lng, stop.Lat });

                    waypoints.Add(new[] { deliveryLocation.lng, deliveryLocation.lat });

                    var directionsBody = new
                    {
                        coordinates = waypoints.ToArray(),
                        alternative_routes = new { target_count = 2 }
                    };

                    var directionsJson = JsonSerializer.Serialize(directionsBody);
                    var directionsRequest = new HttpRequestMessage(HttpMethod.Post,
                        "https://api.openrouteservice.org/v2/directions/driving-car/geojson")
                    {
                        Content = new StringContent(directionsJson, Encoding.UTF8, "application/json")
                    };
                    directionsRequest.Headers.TryAddWithoutValidation("Authorization", _apiKey);

                    var directionsResponse = await _httpClient.SendAsync(directionsRequest);
                    var directionsContent = await directionsResponse.Content.ReadAsStringAsync();

                    if (directionsResponse.IsSuccessStatusCode)
                    {
                        result.RouteGeoJson = directionsContent;
                    }
                }
                catch (Exception dirEx)
                {
                    Console.WriteLine($"[ROUTE] Directions API error: {dirEx.Message}");
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                // Ultimate fallback: still use Nearest Neighbor even if everything fails
                Console.WriteLine($"[ROUTE] Complete failure, using NN fallback: {ex.Message}");
                result = NearestNeighborOptimize(shipperLocation, pickupPoints, deliveryLocation);
            }

            return result;
        }
    }
}
