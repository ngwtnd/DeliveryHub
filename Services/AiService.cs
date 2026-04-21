using System.Text;
using System.Text.Json;

namespace DeliveryHubWeb.Services
{
    public interface IAiService
    {
        Task<string> GetAiResponseAsync(string userPrompt);
    }

    public class AiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiService> _logger;

        public AiService(HttpClient httpClient, IConfiguration configuration, ILogger<AiService> _logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            this._logger = _logger;
        }

        public async Task<string> GetAiResponseAsync(string userPrompt)
        {
            try
            {
                var apiKey = _configuration["OpenAI:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    return "Xin lỗi, hệ thống AI chưa được cấu hình phím API.";
                }

                var requestBody = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new { role = "system", content = "Bạn là trợ lý ảo của DeliveryHub, một nền tảng giao đồ ăn cao cấp tại Việt Nam. Bạn hãy tư vấn món ăn, giải đáp thắc mắc về đơn hàng và hỗ trợ người dùng một cách thân thiện, chuyên nghiệp. Hãy trả lời ngắn gọn, súc tích." },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.7
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"OpenAI API Error: {response.StatusCode} - {errorContent}");
                    return "Xin lỗi, tôi đang gặp một chút trục trặc kỹ thuật. Vui lòng thử lại sau!";
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(jsonResponse);
                var content = document.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return content ?? "Tôi không thể tìm thấy câu trả lời phù hợp.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in AiService");
                return "Đã có lỗi xảy ra khi kết nối với bộ não AI.";
            }
        }
    }
}
