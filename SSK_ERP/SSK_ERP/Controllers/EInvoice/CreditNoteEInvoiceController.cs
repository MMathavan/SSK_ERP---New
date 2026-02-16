using System;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class CreditNoteEInvoiceController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int CreditNoteRegisterId = 23;

        [Authorize(Roles = "CreditNoteEInvoiceIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "CreditNoteEInvoiceIndex")]
        public JsonResult GetAjaxData(JQueryDataTableParamModel param, string fromDate = null, string toDate = null)
        {
            try
            {
                DateTime? fd = null;
                if (!string.IsNullOrWhiteSpace(fromDate))
                {
                    DateTime parsed;
                    if (DateTime.TryParse(fromDate, out parsed))
                    {
                        fd = parsed.Date;
                    }
                }

                DateTime? exclusiveTo = null;
                if (!string.IsNullOrWhiteSpace(toDate))
                {
                    DateTime parsed;
                    if (DateTime.TryParse(toDate, out parsed))
                    {
                        exclusiveTo = parsed.Date.AddDays(1);
                    }
                }

                var query = db.TransactionMasters.Where(t => t.REGSTRID == CreditNoteRegisterId);

                if (fd.HasValue)
                {
                    query = query.Where(t => t.TRANDATE >= fd.Value);
                }

                if (exclusiveTo.HasValue)
                {
                    query = query.Where(t => t.TRANDATE < exclusiveTo.Value);
                }

                var masters = query
                    .OrderByDescending(t => t.TRANDATE)
                    .ThenByDescending(t => t.TRANMID)
                    .ToList();

                var data = masters
                    .Select(t => new
                    {
                        t.TRANMID,
                        t.TRANDATE,
                        t.TRANNO,
                        TRANDNO = t.TRANDNO ?? "0000",
                        TRANREFNO = !string.IsNullOrWhiteSpace(t.TRANTAXBILLNO)
                            ? t.TRANTAXBILLNO
                            : (t.TRANREFNO ?? "-"),
                        CustomerName = t.TRANREFNAME ?? string.Empty,
                        Amount = t.TRANNAMT,
                        AckNo = t.ACKNO ?? string.Empty,
                        Status = t.DISPSTATUS == 0 ? "Enabled" : "Disabled"
                    })
                    .ToList();

                return Json(new { data }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [Authorize(Roles = "CreditNoteEInvoiceUpload")]
        public ActionResult Upload(int id)
        {
            TempData["ErrorMessage"] = "Upload is not implemented yet.";
            return RedirectToAction("Index");
        }

        [Authorize(Roles = "CreditNoteEInvoicePrint")]
        public ActionResult Print(int id)
        {
            TempData["ErrorMessage"] = "Print is not implemented yet.";
            return RedirectToAction("Index");
        }
    }
}
