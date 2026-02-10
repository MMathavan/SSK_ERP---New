using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Purchase
{
    [SessionExpire]
    public class CreateDirectPurchaseReturnController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int PurchaseReturnRegisterId = 22;

        [Authorize(Roles = "PurchaseReturnCreate,PurchaseReturnEdit")]
        public ActionResult Direct(int? id)
        {
            TransactionMaster model;
            var detailRows = new List<DirectPurchaseReturnDetailRow>();

            if (id.HasValue && id.Value > 0)
            {
                // Edit existing Direct Purchase Return
                if (!User.IsInRole("PurchaseReturnEdit"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to edit Direct Purchase Returns.";
                    return RedirectToAction("Index", "PurchaseReturn");
                }

                model = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == PurchaseReturnRegisterId && t.TRANBTYPE == 0);
                if (model == null)
                {
                    TempData["ErrorMessage"] = "Direct Purchase Return not found.";
                    return RedirectToAction("Index", "PurchaseReturn");
                }

                var details = db.TransactionDetails.Where(d => d.TRANMID == model.TRANMID).ToList();

                if (details.Any())
                {
                    var materialIds = details
                        .Select(d => d.TRANDREFID)
                        .Distinct()
                        .ToList();

                    var materials = db.MaterialMasters
                        .Where(m => materialIds.Contains(m.MTRLID))
                        .ToDictionary(m => m.MTRLID, m => m);

                    var hsnIds = materials.Values
                        .Where(m => m.HSNID > 0)
                        .Select(m => m.HSNID)
                        .Distinct()
                        .ToList();

                    var hsnMap = db.HSNCodeMasters
                        .Where(h => hsnIds.Contains(h.HSNID))
                        .ToDictionary(h => h.HSNID, h => h);

                    var detailIds = details.Select(d => d.TRANDID).ToList();

                    var batchDetails = db.TransactionBatchDetails
                        .Where(b => detailIds.Contains(b.TRANDID))
                        .ToList();

                    foreach (var d in details)
                    {
                        materials.TryGetValue(d.TRANDREFID, out var material);
                        string hsnCode = string.Empty;
                        if (material != null && material.HSNID > 0 && hsnMap.TryGetValue(material.HSNID, out var hsn))
                        {
                            hsnCode = hsn.HSNCODE;
                        }

                        var batch = batchDetails.FirstOrDefault(b => b.TRANDID == d.TRANDID);

                        detailRows.Add(new DirectPurchaseReturnDetailRow
                        {
                            MaterialId = d.TRANDREFID,
                            Qty = d.TRANDQTY,
                            Rate = d.TRANDRATE,
                            Amount = d.TRANDGAMT,
                            HsnCode = hsnCode,
                            BillNo = d.TRANDREFNO,
                            BatchNo = batch != null ? batch.TRANBDNO : null,
                            ExpiryDate = batch != null ? (DateTime?)batch.TRANBEXPDATE : null,
                            PackingId = batch != null ? batch.PACKMID : 0,
                            BoxQty = batch != null ? batch.TRANBQTY : 0m
                        });
                    }
                }
            }
            else
            {
                // New Direct Purchase Return
                if (!User.IsInRole("PurchaseReturnCreate"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to create Direct Purchase Returns.";
                    return RedirectToAction("Index", "PurchaseReturn");
                }

                model = new TransactionMaster
                {
                    TRANDATE = DateTime.Today,
                    TRANTIME = DateTime.Now,
                    DISPSTATUS = 0,
                    TRANBTYPE = 0 // Direct Purchase Return
                };

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseReturnRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;
                model.TRANNO = nextTranNo;
                model.TRANDNO = FormatPurchaseReturnTrandNo(nextTranNo, model.TRANDATE);
            }

            ViewBag.StatusList = new SelectList(
                new[]
                {
                    new { Value = "0", Text = "Enabled" },
                    new { Value = "1", Text = "Disabled" }
                },
                "Value",
                "Text",
                model.DISPSTATUS.ToString()
            );

            var supplierList = db.SupplierMasters
                .Where(c => c.DISPSTATUS == 0)
                .OrderBy(c => c.CATENAME)
                .Select(c => new
                {
                    c.CATEID,
                    c.CATENAME
                })
                .ToList();

            ViewBag.SupplierList = new SelectList(supplierList, "CATEID", "CATENAME", model.TRANREFID);

            var packingList = db.PackingMasters
                .Where(p => p.DISPSTATUS == 0)
                .OrderBy(p => p.PACKMDESC)
                .Select(p => new
                {
                    p.PACKMID,
                    p.PACKMDESC
                })
                .ToList();

            ViewBag.PackingListJson = JsonConvert.SerializeObject(packingList);
            ViewBag.DetailRowsJson = detailRows.Any()
                ? JsonConvert.SerializeObject(detailRows, new JsonSerializerSettings { DateFormatString = "yyyy-MM-dd" })
                : "[]";

            return View("Direct", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "PurchaseReturnCreate,PurchaseReturnEdit")] 
        public ActionResult savedata(TransactionMaster master, string detailRowsJson)
        {
            try
            {
                bool isEdit = master.TRANMID > 0 &&
                              db.TransactionMasters.Any(t => t.TRANMID == master.TRANMID &&
                                                             t.REGSTRID == PurchaseReturnRegisterId &&
                                                             t.TRANBTYPE == 0);

                if (isEdit)
                {
                    if (!User.IsInRole("PurchaseReturnEdit"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to edit Direct Purchase Returns.";
                        return RedirectToAction("Index", "PurchaseReturn");
                    }
                }
                else
                {
                    if (!User.IsInRole("PurchaseReturnCreate"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to create Direct Purchase Returns.";
                        return RedirectToAction("Index", "PurchaseReturn");
                    }
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<DirectPurchaseReturnDetailRow>()
                    : JsonConvert.DeserializeObject<List<DirectPurchaseReturnDetailRow>>(detailRowsJson) ?? new List<DirectPurchaseReturnDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0)
                    .ToList();

                if (!details.Any())
                {
                    TempData["ErrorMessage"] = "Please add at least one detail row.";
                    if (isEdit)
                    {
                        return RedirectToAction("Direct", new { id = master.TRANMID });
                    }
                    return RedirectToAction("Direct");
                }

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;
                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                if (master.TRANREFID <= 0)
                {
                    TempData["ErrorMessage"] = "Please select a supplier.";
                    if (isEdit)
                    {
                        return RedirectToAction("Direct", new { id = master.TRANMID });
                    }
                    return RedirectToAction("Direct");
                }

                short tranStateType = 0;
                var supplier = db.SupplierMasters.FirstOrDefault(c => c.CATEID == master.TRANREFID);
                if (supplier != null)
                {
                    master.TRANREFID = supplier.CATEID;
                    master.TRANREFNAME = supplier.CATENAME;

                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);
                    if (state != null)
                    {
                        tranStateType = state.STATETYPE;
                    }
                }

                master.TRANSTATETYPE = tranStateType;
                master.TRANTIME = DateTime.Now;
                if (string.IsNullOrWhiteSpace(master.TRANREFNO))
                {
                    master.TRANREFNO = "-";
                }

                if (isEdit)
                {
                    var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANMID &&
                                                                             t.REGSTRID == PurchaseReturnRegisterId &&
                                                                             t.TRANBTYPE == 0);
                    if (existing == null)
                    {
                        TempData["ErrorMessage"] = "Direct Purchase Return not found.";
                        return RedirectToAction("Index", "PurchaseReturn");
                    }

                    existing.TRANDATE = master.TRANDATE;
                    existing.TRANTIME = master.TRANTIME;
                    existing.TRANREFID = master.TRANREFID;
                    existing.TRANREFNAME = master.TRANREFNAME;
                    existing.TRANSTATETYPE = master.TRANSTATETYPE;
                    existing.TRANREFNO = master.TRANREFNO;
                    existing.TRANNARTN = master.TRANNARTN;
                    existing.TRANRMKS = master.TRANRMKS;
                    existing.DISPSTATUS = master.DISPSTATUS;
                    existing.LMUSRID = userName;
                    existing.PRCSDATE = DateTime.Now;

                    var existingDetailIds = db.TransactionDetails
                        .Where(d => d.TRANMID == existing.TRANMID)
                        .Select(d => d.TRANDID)
                        .ToList();

                    if (existingDetailIds.Any())
                    {
                        db.Database.ExecuteSqlCommand(
                            $"DELETE FROM TRANSACTIONBATCHDETAIL WHERE TRANDID IN ({string.Join(",", existingDetailIds)})");

                        var existingDetails = db.TransactionDetails
                            .Where(d => d.TRANMID == existing.TRANMID)
                            .ToList();

                        if (existingDetails.Any())
                        {
                            db.TransactionDetails.RemoveRange(existingDetails);
                        }

                        db.SaveChanges();
                    }

                    InsertDetails(existing, details);
                    db.SaveChanges();
                }
                else
                {
                    master.COMPYID = compyId;
                    master.SDPTID = 0;
                    master.REGSTRID = PurchaseReturnRegisterId;
                    master.TRANBTYPE = 0; // Always Direct
                    master.EXPRTSTATUS = 0;

                    var maxTranNo = db.TransactionMasters
                        .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseReturnRegisterId)
                        .Select(t => (int?)t.TRANNO)
                        .Max();

                    int nextTranNo = (maxTranNo ?? 0) + 1;
                    master.TRANNO = nextTranNo;
                    if (string.IsNullOrWhiteSpace(master.TRANDNO))
                    {
                        master.TRANDNO = FormatPurchaseReturnTrandNo(nextTranNo, master.TRANDATE);
                    }

                    master.CUSRID = userName;
                    master.LMUSRID = userName;
                    master.PRCSDATE = DateTime.Now;
                    master.TRANPCOUNT = 0;

                    db.TransactionMasters.Add(master);
                    db.SaveChanges();

                    InsertDetails(master, details);
                    db.SaveChanges();
                }

                return RedirectToAction("Index", "PurchaseReturn");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index", "PurchaseReturn");
            }
        }

        [HttpPost]
        public JsonResult CalculateDirectPurchaseReturnTax(short stateType, string detailRowsJson)
        {
            try
            {
                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<DirectPurchaseReturnDetailRow>()
                    : JsonConvert.DeserializeObject<List<DirectPurchaseReturnDetailRow>>(detailRowsJson) ?? new List<DirectPurchaseReturnDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0)
                    .ToList();

                if (!details.Any())
                {
                    return Json(new { success = true, gross = 0m, cgst = 0m, sgst = 0m, igst = 0m, net = 0m }, JsonRequestBehavior.AllowGet);
                }

                var materialIds = details.Select(d => d.MaterialId).Distinct().ToList();
                var materials = db.MaterialMasters
                    .Where(m => materialIds.Contains(m.MTRLID))
                    .ToDictionary(m => m.MTRLID, m => m);

                var hsnIds = materials.Values
                    .Where(m => m.HSNID > 0)
                    .Select(m => m.HSNID)
                    .Distinct()
                    .ToList();

                var hsnMap = db.HSNCodeMasters
                    .Where(h => hsnIds.Contains(h.HSNID))
                    .ToDictionary(h => h.HSNID, h => h);

                decimal totalGross = 0m;
                decimal totalCgst = 0m;
                decimal totalSgst = 0m;
                decimal totalIgst = 0m;
                decimal totalNet = 0m;

                foreach (var d in details)
                {
                    materials.TryGetValue(d.MaterialId, out var material);

                    int hsnId = material != null ? material.HSNID : 0;
                    hsnMap.TryGetValue(hsnId, out var hsn);

                    decimal qty = d.Qty;
                    decimal rate = d.Rate;
                    decimal gross = d.Amount > 0 ? d.Amount : qty * rate;

                    decimal cgstAmt = 0m;
                    decimal sgstAmt = 0m;
                    decimal igstAmt = 0m;

                    if (hsn != null)
                    {
                        if (stateType == 0)
                        {
                            if (hsn.CGSTEXPRN > 0)
                            {
                                cgstAmt = Math.Round((gross * hsn.CGSTEXPRN) / 100m, 2);
                            }

                            if (hsn.SGSTEXPRN > 0)
                            {
                                sgstAmt = Math.Round((gross * hsn.SGSTEXPRN) / 100m, 2);
                            }
                        }
                        else
                        {
                            if (hsn.IGSTEXPRN > 0)
                            {
                                igstAmt = Math.Round((gross * hsn.IGSTEXPRN) / 100m, 2);
                            }
                        }
                    }

                    decimal net = gross + cgstAmt + sgstAmt + igstAmt;

                    totalGross += gross;
                    totalCgst += cgstAmt;
                    totalSgst += sgstAmt;
                    totalIgst += igstAmt;
                    totalNet += net;
                }

                return Json(new
                {
                    success = true,
                    gross = totalGross,
                    cgst = totalCgst,
                    sgst = totalSgst,
                    igst = totalIgst,
                    net = totalNet
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void InsertDetails(TransactionMaster master, List<DirectPurchaseReturnDetailRow> details)
        {
            if (details == null || !details.Any())
            {
                return;
            }

            var materialIds = details.Select(d => d.MaterialId).Distinct().ToList();
            var materials = db.MaterialMasters
                .Where(m => materialIds.Contains(m.MTRLID))
                .ToDictionary(m => m.MTRLID, m => m);

            var hsnIds = materials.Values
                .Where(m => m.HSNID > 0)
                .Select(m => m.HSNID)
                .Distinct()
                .ToList();

            var hsnMap = db.HSNCodeMasters
                .Where(h => hsnIds.Contains(h.HSNID))
                .ToDictionary(h => h.HSNID, h => h);

            decimal totalGross = 0m;
            decimal totalNet = 0m;
            decimal totalCgst = 0m;
            decimal totalSgst = 0m;
            decimal totalIgst = 0m;
            short tranStateType = master.TRANSTATETYPE;
            int tranMid = master.TRANMID;

            const string queryInsertBatch = @"INSERT INTO TRANSACTIONBATCHDETAIL (
                    TRANDID, AMTRLID, HSNID, STKBID, TRANBDNO, TRANBEXPDATE, PACKMID, 
                    TRANPQTY, TRANBQTY, TRANBRATE, TRANBPTRRATE, TRANBMRP, 
                    TRANBGAMT, TRANBCGSTEXPRN, TRANBSGSTEXPRN, TRANBIGSTEXPRN, 
                    TRANBCGSTAMT, TRANBSGSTAMT, TRANBIGSTAMT, TRANBNAMT, 
                    TRANBPID, TRANDPID, TRANPTQTY, TRANBLMID
                ) VALUES (
                    @p0, @p1, @p2, @p3, @p4, @p5, @p6,
                    @p7, @p8, @p9, @p10, @p11,
                    @p12, @p13, @p14, @p15,
                    @p16, @p17, @p18, @p19,
                    @p20, @p21, @p22, @p23
                )";

            foreach (var d in details)
            {
                materials.TryGetValue(d.MaterialId, out var material);
                int hsnId = material != null ? material.HSNID : 0;
                hsnMap.TryGetValue(hsnId, out var hsn);

                decimal qty = d.Qty;
                decimal rate = d.Rate;
                decimal gross = d.Amount > 0 ? d.Amount : qty * rate;

                decimal cgstAmt = 0m;
                decimal sgstAmt = 0m;
                decimal igstAmt = 0m;
                decimal cgstExpr = 0m;
                decimal sgstExpr = 0m;
                decimal igstExpr = 0m;

                if (hsn != null)
                {
                    if (tranStateType == 0)
                    {
                        if (hsn.CGSTEXPRN > 0)
                        {
                            cgstAmt = Math.Round((gross * hsn.CGSTEXPRN) / 100m, 2);
                            cgstExpr = hsn.CGSTEXPRN;
                        }

                        if (hsn.SGSTEXPRN > 0)
                        {
                            sgstAmt = Math.Round((gross * hsn.SGSTEXPRN) / 100m, 2);
                            sgstExpr = hsn.SGSTEXPRN;
                        }
                    }
                    else
                    {
                        if (hsn.IGSTEXPRN > 0)
                        {
                            igstAmt = Math.Round((gross * hsn.IGSTEXPRN) / 100m, 2);
                            igstExpr = hsn.IGSTEXPRN;
                        }
                    }
                }

                decimal net = gross + cgstAmt + sgstAmt + igstAmt;

                string billNo = d.BillNo ?? string.Empty;
                if (billNo.Length > 15)
                {
                    billNo = billNo.Substring(0, 15);
                }

                var detail = new TransactionDetail
                {
                    TRANMID = tranMid,
                    TRANDREFID = material != null ? material.MTRLID : d.MaterialId,
                    TRANDREFNO = billNo,
                    TRANDREFNAME = material != null ? material.MTRLDESC : string.Empty,
                    TRANDMTRLPRFT = 0,
                    HSNID = hsnId,
                    PACKMID = d.PackingId,
                    TRANDQTY = qty,
                    TRANDRATE = rate,
                    TRANDARATE = rate,
                    TRANDGAMT = gross,
                    TRANDCGSTAMT = cgstAmt,
                    TRANDSGSTAMT = sgstAmt,
                    TRANDIGSTAMT = igstAmt,
                    TRANDNAMT = net,
                    TRANDAID = 0,
                    TRANDNARTN = null,
                    TRANDRMKS = null
                };

                db.TransactionDetails.Add(detail);
                db.SaveChanges();

                if (detail.TRANDID > 0)
                {
                    var batchNo = d.BatchNo ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(batchNo))
                    {
                        var expiryDate = d.ExpiryDate ?? DateTime.Today;
                        int boxQtyInt = (int)Math.Round(d.BoxQty);
                        int totalQtyInt = (int)Math.Round(qty);

                        int packQtyInt = totalQtyInt;
                        if (boxQtyInt > 0 && totalQtyInt > 0)
                        {
                            packQtyInt = (int)Math.Round((decimal)totalQtyInt / boxQtyInt);
                        }

                        int resolvedPackMid = d.PackingId;
                        if (packQtyInt > 0)
                        {
                            var matchingPack = db.PackingMasters
                                .FirstOrDefault(p => p.PACKMNOU == packQtyInt && p.DISPSTATUS == 1)
                                ?? db.PackingMasters.FirstOrDefault(p => p.PACKMNOU == packQtyInt);

                            if (matchingPack != null)
                            {
                                resolvedPackMid = matchingPack.PACKMID;
                            }
                        }

                        int sourceBatchId = 0;
                        int sourceDetailId = 0;
                        int sourceRefId = 0;

                        db.Database.ExecuteSqlCommand(
                            queryInsertBatch,
                            detail.TRANDID,
                            detail.TRANDREFID,
                            detail.HSNID,
                            0,
                            batchNo,
                            expiryDate,
                            resolvedPackMid,
                            packQtyInt,
                            boxQtyInt,
                            rate,
                            0m,
                            0m,
                            gross,
                            cgstExpr,
                            sgstExpr,
                            igstExpr,
                            cgstAmt,
                            sgstAmt,
                            igstAmt,
                            net,
                            sourceBatchId,
                            sourceDetailId,
                            totalQtyInt,
                            sourceRefId
                        );
                    }
                }

                totalGross += gross;
                totalNet += net;
                totalCgst += cgstAmt;
                totalSgst += sgstAmt;
                totalIgst += igstAmt;
            }

            master.TRANGAMT = totalGross;
            master.TRANCGSTAMT = totalCgst;
            master.TRANSGSTAMT = totalSgst;
            master.TRANIGSTAMT = totalIgst;
            master.TRANNAMT = totalNet;
            master.TRANPCOUNT = 0;
            master.TRANAMTWRDS = ConvertAmountToWords(totalNet);
        }

        private string ConvertAmountToWords(decimal amount)
        {
            if (amount == 0) return "ZERO RUPEES ONLY";

            long integerPart = (long)Math.Floor(amount);
            int decimalPart = (int)Math.Round((amount - integerPart) * 100);

            string words = NumberToWords(integerPart) + " RUPEES";

            if (decimalPart > 0)
            {
                words += " AND " + NumberToWords(decimalPart) + " PAISE";
            }

            words += " ONLY";
            return words;
        }

        private string NumberToWords(long number)
        {
            if (number == 0) return "ZERO";

            if (number < 0) return "MINUS " + NumberToWords(Math.Abs(number));

            string[] unitsMap = { "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "TEN", "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN", "EIGHTEEN", "NINETEEN" };
            string[] tensMap = { "ZERO", "TEN", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY" };

            string words = "";

            if ((number / 10000000) > 0)
            {
                words += NumberToWords(number / 10000000) + " CRORE ";
                number %= 10000000;
            }

            if ((number / 100000) > 0)
            {
                words += NumberToWords(number / 100000) + " LAKH ";
                number %= 100000;
            }

            if ((number / 1000) > 0)
            {
                words += NumberToWords(number / 1000) + " THOUSAND ";
                number %= 1000;
            }

            if ((number / 100) > 0)
            {
                words += NumberToWords(number / 100) + " HUNDRED ";
                number %= 100;
            }

            if (number > 0)
            {
                if (words != "")
                    words += "AND ";

                if (number < 20)
                    words += unitsMap[number];
                else
                {
                    words += tensMap[number / 10];
                    if ((number % 10) > 0)
                        words += " " + unitsMap[number % 10];
                }
            }

            return words.Trim();
        }

        private string FormatPurchaseReturnTrandNo(int tranNo, DateTime tranDate)
        {
            int fyStartYear = tranDate.Month >= 4 ? tranDate.Year : tranDate.Year - 1;
            int fyEndYear = fyStartYear + 1;
            string fyPrefix = (fyStartYear % 100).ToString("00") + "-" + (fyEndYear % 100).ToString("00");

            string seqText = tranNo.ToString("0000");
            return fyPrefix + "/DN" + seqText;
        }

        private class DirectPurchaseReturnDetailRow
        {
            public int MaterialId { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public string HsnCode { get; set; }
            public string BillNo { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int PackingId { get; set; }
            public decimal BoxQty { get; set; }
        }
    }
}
