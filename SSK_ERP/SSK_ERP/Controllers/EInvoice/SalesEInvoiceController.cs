using System;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesEInvoiceController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        [Authorize(Roles = "SalesEInvoiceIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "SalesEInvoiceUpload")]
        public ActionResult Upload()
        {
            return View("Index");
        }

        [Authorize(Roles = "SalesEInvoicePrint")]
        public ActionResult Print()
        {
            return View("Index");
        }
    }
}
