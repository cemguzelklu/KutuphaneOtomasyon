using Microsoft.AspNetCore.Mvc;
using KutuphaneOtomasyon.Services.Chat;

namespace KutuphaneOtomasyon.Controllers
{
    public class ChatController : Controller
    {
        private readonly IChatService _chat;

        public ChatController(IChatService chat)
        {
            _chat = chat;
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("IsAdmin") != "true")
                return RedirectToAction("Login", "Auth");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Ask(string message, CancellationToken ct)
        {
            if (HttpContext.Session.GetString("IsAdmin") != "true")
                return Unauthorized();

            var userId = HttpContext.Session.Id ?? "anon";
            var reply = await _chat.AskAsync(userId, message ?? "", ct);
            return Json(new { reply = reply.Text, provider = reply.Provider, fallback = reply.FromFallback });
        }
    }
}
