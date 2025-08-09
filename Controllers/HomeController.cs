#region Using

using System.Diagnostics;
using app_ocr_ai_models.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

#endregion

namespace app_ocr_ai_models.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        [Authorize]
        public IActionResult Index() 
        {
           return RedirectToAction("Index", "Nexus");
            return View();
        }
        

        [Route("dashboard-marketing")]
        public IActionResult DashboardMarketing() => View();

        [Route("dashboard-social")]
        public IActionResult SocialWall() => View();

        public IActionResult Inbox() => View();

        public IActionResult Chat() => View();

        public IActionResult Widgets() => View();
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
