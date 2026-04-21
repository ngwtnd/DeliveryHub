using DeliveryHubWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryHubWeb.Controllers
{
    public class AiController : Controller
    {
        private readonly IAiService _aiService;

        public AiController(IAiService aiService)
        {
            _aiService = aiService;
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            if (string.IsNullOrEmpty(request?.Message))
            {
                return BadRequest("Tin nhắn không được để trống.");
            }

            var response = await _aiService.GetAiResponseAsync(request.Message);
            return Json(new { response });
        }
    }

    public class ChatRequest
    {
        public string? Message { get; set; }
    }
}
