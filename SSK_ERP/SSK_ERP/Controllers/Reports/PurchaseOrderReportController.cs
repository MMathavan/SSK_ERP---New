using System.Web.Mvc;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class PurchaseOrderReportController : Controller
    {
        [Authorize(Roles = "PurchaseOrderReport")]
        public ActionResult Index()
        {
            return View();
        }
    }
}
