using Microsoft.AspNetCore.Mvc;

namespace KutuphaneOtomasyon.Controllers
{
    public class AuthController : Controller
    {
        public const string adminUsername = "admin";
        public const string adminPassword = "1234";
        public IActionResult Login()
        {
            return View();
        }
        [HttpPost]
        public IActionResult Login(string username, string password)
        {
            if (username == adminUsername && password == adminPassword)
            {
                HttpContext.Session.SetString("IsAdmin", "true");
                return RedirectToAction("Index","Home");
            }
            ViewBag.Error = "Kullanıcı adı veya şifre yanlış.";
            return View();
        }
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login","Auth");
        }
    }
}
