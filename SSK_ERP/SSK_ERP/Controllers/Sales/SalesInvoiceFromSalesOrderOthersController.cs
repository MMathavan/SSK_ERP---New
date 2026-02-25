using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesInvoiceFromSalesOrderOthersController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesOrderRegisterId = 1;
        private const int SalesInvoiceRegisterId = 20;

        private class SalesOrderItemRow
        {
            public int MaterialId { get; set; }
            public string MaterialCode { get; set; }
            public string MaterialName { get; set; }
            public string HsnCode { get; set; }
            public decimal Qty { get; set; }
        }

        private class SalesInvoiceDetailRow
        {
            public int MaterialId { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal ActualRate { get; set; }
            public decimal Amount { get; set; }
        }

        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceIndex")]
        public ActionResult Form()
        {
            var model = new TransactionMaster
            {
                TRANDATE = DateTime.Today,
                TRANTIME = DateTime.Now,
                DISPSTATUS = 0
            };

            var compyObj = Session["CompyId"] ?? Session["compyid"];
            int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

            var maxTranNo = db.TransactionMasters
                .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesInvoiceRegisterId)
                .Select(t => (int?)t.TRANNO)
                .Max();

            int nextTranNo = (maxTranNo ?? 0) + 1;
            model.TRANNO = nextTranNo;
            model.TRANDNO = GenerateSalesInvoiceNumber(model.TRANDATE, nextTranNo);

            ViewBag.StatusList = new SelectList(
                new[]
                {
                    new { Value = "0", Text = "Enabled" },
                    new { Value = "1", Text = "Disabled" }
                },
                "Value",
                "Text",
                model.DISPSTATUS.ToString(CultureInfo.InvariantCulture));

            ViewBag.DetailRowsJson = "[]";

            return View(model);
        }

        [HttpGet]
        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceIndex")]
        public JsonResult GetCustomers()
        {
            try
            {
                var customers = db.CustomerMasters
                    .Select(c => new
                    {
                        Id = c.CATEID,
                        Name = c.CATENAME
                    })
                    .OrderBy(x => x.Name)
                    .ToList();

                return Json(new { success = true, data = customers }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceIndex")]
        public JsonResult GetBillNos(int customerId)
        {
            try
            {
                var bills = db.TransactionMasters
                    .Where(t => t.REGSTRID == SalesOrderRegisterId && t.TRANETYPE == 1 && t.TRANREFID == customerId)
                    .OrderByDescending(t => t.TRANDATE)
                    .ThenByDescending(t => t.TRANMID)
                    .Select(t => new
                    {
                        SalesOrderId = t.TRANMID,
                        BillNo = t.TRANREFNO,
                        TranDNo = t.TRANDNO
                    })
                    .ToList();

                return Json(new { success = true, data = bills }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceIndex")]
        public JsonResult GetPackings()
        {
            try
            {
                var packings = db.PackingMasters
                    .Where(p => p.DISPSTATUS == 0)
                    .OrderBy(p => p.PACKMDESC)
                    .Select(p => new
                    {
                        Id = p.PACKMID,
                        Name = p.PACKMDESC
                    })
                    .ToList();

                return Json(new { success = true, data = packings }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceIndex")]
        public JsonResult GetSalesOrderData(int salesOrderId)
        {
            try
            {
                var so = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == salesOrderId && t.REGSTRID == SalesOrderRegisterId && t.TRANETYPE == 1);
                if (so == null)
                {
                    return Json(new { success = false, message = "Sales Order not found." }, JsonRequestBehavior.AllowGet);
                }

                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == so.TRANREFID);
                var location = customer != null ? db.LocationMasters.FirstOrDefault(l => l.LOCTID == customer.LOCTID) : null;
                var state = customer != null ? db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID) : null;

                var details = db.TransactionDetails
                    .Where(d => d.TRANMID == so.TRANMID)
                    .OrderBy(d => d.TRANDID)
                    .ToList();

                var materialIds = details.Select(d => d.TRANDREFID).Distinct().ToList();
                var materialMap = db.MaterialMasters
                    .Where(m => materialIds.Contains(m.MTRLID))
                    .ToDictionary(m => m.MTRLID, m => m);

                var hsnIds = materialMap.Values.Where(m => m.HSNID > 0).Select(m => m.HSNID).Distinct().ToList();
                var hsnMap = db.HSNCodeMasters
                    .Where(h => hsnIds.Contains(h.HSNID))
                    .ToDictionary(h => h.HSNID, h => h);

                var itemRows = new List<SalesOrderItemRow>();
                foreach (var d in details)
                {
                    materialMap.TryGetValue(d.TRANDREFID, out var m);
                    string hsnCode = string.Empty;
                    if (m != null && m.HSNID > 0 && hsnMap.TryGetValue(m.HSNID, out var hsn))
                    {
                        hsnCode = hsn.HSNCODE;
                    }

                    itemRows.Add(new SalesOrderItemRow
                    {
                        MaterialId = d.TRANDREFID,
                        MaterialCode = m != null ? m.MTRLCODE : d.TRANDREFNO,
                        MaterialName = m != null ? m.MTRLDESC : d.TRANDREFNAME,
                        HsnCode = hsnCode,
                        Qty = d.TRANDQTY
                    });
                }

                var header = new
                {
                    CustomerId = so.TRANREFID,
                    CustomerName = so.TRANREFNAME,
                    Address1 = customer != null ? customer.CATEADDR1 : string.Empty,
                    Address2 = customer != null ? customer.CATEADDR2 : string.Empty,
                    Pincode = customer != null ? customer.CATEADDR5 : string.Empty,
                    City = location != null ? location.LOCTDESC : string.Empty,
                    State = state != null ? state.STATEDESC : string.Empty,
                    StateType = state != null ? state.STATETYPE : (short)0,
                    Country = "India"
                };

                return Json(new { success = true, header, items = itemRows }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceIndex")]
        public ActionResult savedata(TransactionMaster master, int salesOrderId, string detailRowsJson)
        {
            try
            {
                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<SalesInvoiceDetailRow>()
                    : JsonConvert.DeserializeObject<List<SalesInvoiceDetailRow>>(detailRowsJson) ?? new List<SalesInvoiceDetailRow>();

                details = details.Where(d => d != null && d.MaterialId > 0 && d.Qty > 0).ToList();
                if (!details.Any())
                {
                    TempData["ErrorMessage"] = "Please add at least one detail row.";
                    return RedirectToAction("Form");
                }

                var so = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == salesOrderId && t.REGSTRID == SalesOrderRegisterId && t.TRANETYPE == 1);
                if (so == null)
                {
                    TempData["ErrorMessage"] = "Sales Order not found.";
                    return RedirectToAction("Form");
                }

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == so.TRANREFID);
                short tranStateType = 0;
                if (customer != null)
                {
                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
                    if (state != null)
                    {
                        tranStateType = state.STATETYPE;
                    }
                }

                var invoiceDate = master.TRANDATE == default(DateTime) ? DateTime.Today : master.TRANDATE;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesInvoiceRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;

                var newMaster = new TransactionMaster
                {
                    COMPYID = compyId,
                    SDPTID = 0,
                    REGSTRID = SalesInvoiceRegisterId,
                    TRANDATE = invoiceDate,
                    TRANTIME = DateTime.Now,
                    TRANNO = nextTranNo,
                    TRANDNO = GenerateSalesInvoiceNumber(invoiceDate, nextTranNo),
                    TRANREFNO = string.IsNullOrWhiteSpace(so.TRANREFNO) ? "-" : so.TRANREFNO,
                    TRANREFID = so.TRANREFID,
                    TRANREFNAME = so.TRANREFNAME,
                    TRANSTATETYPE = tranStateType,
                    TRAN_CRDPRDT = master.TRAN_CRDPRDT,
                    TRANGAMT = 0m,
                    TRANNAMT = 0m,
                    TRANCGSTAMT = 0m,
                    TRANSGSTAMT = 0m,
                    TRANIGSTAMT = 0m,
                    TRANAMTWRDS = string.Empty,
                    TRANBTYPE = 0,
                    EXPRTSTATUS = 0,
                    TRANPCOUNT = 0,
                    TRANLMID = so.TRANMID,
                    CUSRID = userName,
                    LMUSRID = userName,
                    PRCSDATE = DateTime.Now,
                    DISPSTATUS = master.DISPSTATUS,
                    TRANRMKS = master.TRANRMKS,
                    TRANPONUM = null,
                    TRANTAXBILLNO = string.IsNullOrWhiteSpace(master.TRANTAXBILLNO) ? null : master.TRANTAXBILLNO
                };

                db.TransactionMasters.Add(newMaster);
                db.SaveChanges();

                var materialIds = details.Select(d => d.MaterialId).Distinct().ToList();
                var materialMap = db.MaterialMasters
                    .Where(m => materialIds.Contains(m.MTRLID))
                    .ToDictionary(m => m.MTRLID, m => m);

                var hsnIds = materialMap.Values.Where(m => m.HSNID > 0).Select(m => m.HSNID).Distinct().ToList();
                var hsnMap = db.HSNCodeMasters
                    .Where(h => hsnIds.Contains(h.HSNID))
                    .ToDictionary(h => h.HSNID, h => h);

                decimal totalGross = 0m;
                decimal totalNet = 0m;

                foreach (var r in details)
                {
                    materialMap.TryGetValue(r.MaterialId, out var m);
                    int hsnId = m != null ? m.HSNID : 0;
                    string refNo = m != null ? m.MTRLCODE : string.Empty;
                    string refName = m != null ? m.MTRLDESC : string.Empty;

                    decimal gross = r.Amount > 0 ? r.Amount : (r.Qty * r.Rate);

                    decimal cgstAmt = 0m;
                    decimal sgstAmt = 0m;
                    decimal igstAmt = 0m;

                    if (hsnId > 0 && hsnMap.TryGetValue(hsnId, out var hsn))
                    {
                        if (tranStateType == 0)
                        {
                            if (hsn.CGSTEXPRN > 0) cgstAmt = Math.Round((gross * hsn.CGSTEXPRN) / 100m, 2);
                            if (hsn.SGSTEXPRN > 0) sgstAmt = Math.Round((gross * hsn.SGSTEXPRN) / 100m, 2);
                        }
                        else
                        {
                            if (hsn.IGSTEXPRN > 0) igstAmt = Math.Round((gross * hsn.IGSTEXPRN) / 100m, 2);
                        }
                    }

                    decimal net = gross + cgstAmt + sgstAmt + igstAmt;

                    var det = new TransactionDetail
                    {
                        TRANMID = newMaster.TRANMID,
                        TRANDREFID = r.MaterialId,
                        TRANDREFNO = string.IsNullOrWhiteSpace(refNo) ? "-" : refNo,
                        TRANDREFNAME = string.IsNullOrWhiteSpace(refName) ? "-" : refName,
                        TRANDMTRLPRFT = 0m,
                        HSNID = hsnId,
                        PACKMID = 0,
                        TRANDQTY = r.Qty,
                        TRANDRATE = r.Rate,
                        TRANDARATE = r.ActualRate,
                        TRANDGAMT = gross,
                        TRANDCGSTAMT = cgstAmt,
                        TRANDSGSTAMT = sgstAmt,
                        TRANDIGSTAMT = igstAmt,
                        TRANDNAMT = net,
                        TRANDAID = 0,
                        TRANDNARTN = null,
                        TRANDRMKS = null
                    };

                    db.TransactionDetails.Add(det);

                    totalGross += gross;
                    totalNet += net;
                    newMaster.TRANCGSTAMT += cgstAmt;
                    newMaster.TRANSGSTAMT += sgstAmt;
                    newMaster.TRANIGSTAMT += igstAmt;
                }

                newMaster.TRANGAMT = totalGross;
                newMaster.TRANNAMT = totalNet;
                newMaster.TRANAMTWRDS = string.Empty;

                db.SaveChanges();

                TempData["SuccessMessage"] = "Sales Invoice created successfully.";
                return RedirectToAction("Index", "SalesInvoice");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Form");
            }
        }

        private string GenerateSalesInvoiceNumber(DateTime invoiceDate, int tranNo)
        {
            var year = invoiceDate.Year;
            int startYear;
            int endYear;

            if (invoiceDate.Month >= 4)
            {
                startYear = year;
                endYear = year + 1;
            }
            else
            {
                startYear = year - 1;
                endYear = year;
            }

            string fyPart = string.Format("{0:00}-{1:00}", startYear % 100, endYear % 100);
            string runningPart = tranNo.ToString("D5");
            return fyPart + "/A" + runningPart;
        }
    }
}
