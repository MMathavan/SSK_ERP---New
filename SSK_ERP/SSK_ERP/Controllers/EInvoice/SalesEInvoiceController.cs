using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesEInvoiceController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesInvoiceRegisterId = 20;

        private class SalesEInvoiceListRow
        {
            public int TRANMID { get; set; }
            public DateTime TRANDATE { get; set; }
            public int TRANNO { get; set; }
            public string TRANDNO { get; set; }
            public string TRANREFNO { get; set; }
            public string TRANTAXBILLNO { get; set; }
            public string TRANREFNAME { get; set; }
            public decimal TRANNAMT { get; set; }
            public short DISPSTATUS { get; set; }
            public string ACKNO { get; set; }
        }

        [Authorize(Roles = "SalesEInvoiceIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "SalesEInvoiceIndex")]
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

                try
                {
                    var sql =
                        "SELECT TRANMID, TRANDATE, TRANNO, TRANDNO, TRANREFNO, TRANTAXBILLNO, TRANREFNAME, TRANNAMT, DISPSTATUS, ACKNO " +
                        "FROM TRANSACTIONMASTER " +
                        "WHERE REGSTRID = @p0 " +
                        "AND (@p1 IS NULL OR TRANDATE >= @p1) " +
                        "AND (@p2 IS NULL OR TRANDATE < @p2)";

                    List<SalesEInvoiceListRow> masters = db.Database
                        .SqlQuery<SalesEInvoiceListRow>(
                            sql,
                            SalesInvoiceRegisterId,
                            (object)fd ?? DBNull.Value,
                            (object)exclusiveTo ?? DBNull.Value)
                        .ToList();

                    var data = masters
                        .OrderByDescending(t => t.TRANDATE)
                        .ThenByDescending(t => t.TRANMID)
                        .Select(t => new
                        {
                            t.TRANMID,
                            t.TRANDATE,
                            t.TRANNO,
                            TRANDNO = string.IsNullOrWhiteSpace(t.TRANDNO) ? "0000" : t.TRANDNO,
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
                catch
                {
                    var query = db.TransactionMasters.Where(t => t.REGSTRID == SalesInvoiceRegisterId);

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
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [Authorize(Roles = "SalesEInvoiceUpload")]
        public ActionResult Upload(int id)
        {
            TempData["ErrorMessage"] = "Upload is not implemented yet.";
            return RedirectToAction("Index");
        }

        [Authorize(Roles = "SalesEInvoicePrint")]
        public ActionResult Print(int id)
        {
            return RedirectToAction("Print", "SalesInvoice", new { id });
        }
    }
}
