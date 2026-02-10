using System.Web.Mvc;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class CreditNoteReportController : Controller
    {
        [Authorize(Roles = "CreditNoteReport")]
        public ActionResult Index()
        {
            return View();
        }
    }
}
