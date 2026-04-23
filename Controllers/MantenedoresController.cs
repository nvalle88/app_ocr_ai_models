using app_ocr_ai_models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SmartAdmin.Web.Controllers
{
    [Authorize]
    public class MantenedoresController : Controller
    {
        private readonly OCRDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public MantenedoresController(OCRDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.ConfigCount      = await _db.OPAIConfiguration.CountAsync(x => x.IsActive);
            ViewBag.AgentCount       = await _db.Agent.CountAsync(x => x.IsActive);
            ViewBag.PromptCount      = await _db.OPAIPrompt.CountAsync(x => x.IsActive);
            ViewBag.AsignacionCount  = await _db.OPAIModelPrompt.CountAsync();
            ViewBag.ProcesoCount     = await _db.Process.CountAsync();
            ViewBag.AgentProcessCount= await _db.AgentProcesses.CountAsync();
            ViewBag.PolicyCount      = await _db.AccessAgentPolicies.CountAsync(x => x.Status);
            ViewBag.BlobCount        = await _db.AzureBlobConf.CountAsync();
            ViewBag.UserCount        = await _userManager.Users.CountAsync();
            return View();
        }
    }
}
