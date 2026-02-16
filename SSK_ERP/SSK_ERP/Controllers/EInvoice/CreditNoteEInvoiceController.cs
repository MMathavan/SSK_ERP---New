using System;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class CreditNoteEInvoiceController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        [Authorize(Roles = "CreditNoteEInvoiceIndex")]
        public ActionResult Index()
        {
            return View();
        }
    }
}
