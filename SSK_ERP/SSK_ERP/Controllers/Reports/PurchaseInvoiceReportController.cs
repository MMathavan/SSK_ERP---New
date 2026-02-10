using System.Web.Mvc;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class PurchaseInvoiceReportController : Controller
    {
        [Authorize(Roles = "PurchaseInvoiceReport")]
        public ActionResult Index()
        {
            return View();
        }
    }
}
