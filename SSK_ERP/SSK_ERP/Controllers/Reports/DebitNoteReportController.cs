using System.Web.Mvc;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class DebitNoteReportController : Controller
    {
        [Authorize(Roles = "DebitNoteReport")]
        public ActionResult Index()
        {
            return View();
        }
    }
}
