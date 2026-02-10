using System.Web.Mvc;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesInvoiceReportController : Controller
    {
        [Authorize(Roles = "SalesInvoiceReport")]
        public ActionResult Index()
        {
            return View();
        }
    }
}
