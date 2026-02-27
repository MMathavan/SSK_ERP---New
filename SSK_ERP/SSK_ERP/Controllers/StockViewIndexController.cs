using System.Web.Mvc;

namespace SSK_ERP.Controllers
{
    public class StockViewIndexController : Controller
    {
        [Authorize(Roles = "StockViewIndex")]
        public ActionResult Index()
        {
            return View();
        }
    }
}
