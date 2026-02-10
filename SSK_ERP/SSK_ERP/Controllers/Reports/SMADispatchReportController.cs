using System.Web.Mvc;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SMADispatchReportController : Controller
    {
        [Authorize(Roles = "SMADispatchReport")]
        public ActionResult Index()
        {
            return View();
        }
    }       
}
