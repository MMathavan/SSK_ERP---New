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
    public class ChellanToDebitNoteController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int ChellanRegisterId = 27;
        private const int PurchaseReturnRegisterId = 22;

        private class ChellanToDebitNoteDetailRow
        {
            public bool IsSelected { get; set; }
            public int MaterialId { get; set; }
            public string MaterialName { get; set; }
            public string HsnCode { get; set; }
            public string BillNo { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int PackingId { get; set; }
            public string Packing { get; set; }
            public decimal Ptr { get; set; }
            public decimal Mrp { get; set; }
            public decimal BoxQty { get; set; }

            public decimal OriginalQty { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }

        public class ChellanToDebitNoteViewModel
        {
            public int ChellanId { get; set; }
            public string ChellanNo { get; set; }
            public DateTime ChellanDate { get; set; }

            public int SupplierId { get; set; }
            public string SupplierName { get; set; }
            public string SupplierCode { get; set; }
            public string Address1 { get; set; }
            public string Address2 { get; set; }
            public string Address3 { get; set; }
            public string Address4 { get; set; }
            public string City { get; set; }
            public string Pincode { get; set; }
            public string State { get; set; }
            public string StateCode { get; set; }
            public string GstNo { get; set; }

            public DateTime DebitNoteDate { get; set; }
            public short Status { get; set; }
            public string Narration { get; set; }
            public string Remarks { get; set; }

            public short TranStateType { get; set; }

            public string DetailRowsJson { get; set; }
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNote")]
        public ActionResult Form(int id)
        {
            var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == ChellanRegisterId);
            if (master == null)
            {
                TempData["ErrorMessage"] = "Chellan not found.";
                return RedirectToAction("Index", "Chellan");
            }

            var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == master.TRANREFID);
            LocationMaster location = null;
            StateMaster state = null;

            if (supplier != null)
            {
                location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == supplier.LOCTID);
                state = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);
            }

            var details = db.TransactionDetails
                .Where(d => d.TRANMID == master.TRANMID)
                .OrderBy(d => d.TRANDID)
                .ToList();

            var detailIds = details.Select(d => d.TRANDID).ToList();
            var batchDetails = db.TransactionBatchDetails
                .Where(b => detailIds.Contains(b.TRANDID))
                .ToList();

            var packIds = batchDetails.Select(b => b.PACKMID).Distinct().ToList();
            var packMap = db.PackingMasters
                .Where(p => packIds.Contains(p.PACKMID))
                .ToDictionary(p => p.PACKMID, p => p);

            var materialIds = details.Select(d => d.TRANDREFID).Distinct().ToList();
            var materials = db.MaterialMasters
                .Where(m => materialIds.Contains(m.MTRLID))
                .ToDictionary(m => m.MTRLID, m => m);

            var hsnIds = materials.Values.Where(m => m.HSNID > 0).Select(m => m.HSNID).Distinct().ToList();
            var hsnMap = db.HSNCodeMasters
                .Where(h => hsnIds.Contains(h.HSNID))
                .ToDictionary(h => h.HSNID, h => h);

            var hsnTaxMap = hsnMap
                .Where(kvp => kvp.Value != null && !string.IsNullOrWhiteSpace(kvp.Value.HSNCODE))
                .GroupBy(kvp => kvp.Value.HSNCODE)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Cgst = g.First().Value.CGSTEXPRN,
                        Sgst = g.First().Value.SGSTEXPRN,
                        Igst = g.First().Value.IGSTEXPRN
                    }
                );

            var rows = new List<ChellanToDebitNoteDetailRow>();
            foreach (var d in details)
            {
                materials.TryGetValue(d.TRANDREFID, out var mat);
                HSNCodeMaster hsn = null;
                if (mat != null && mat.HSNID > 0)
                {
                    hsnMap.TryGetValue(mat.HSNID, out hsn);
                }

                var batch = batchDetails.FirstOrDefault(b => b.TRANDID == d.TRANDID);
                PackingMaster pack = null;
                if (batch != null)
                {
                    packMap.TryGetValue(batch.PACKMID, out pack);
                }

                decimal qty = d.TRANDQTY;
                decimal rate = d.TRANDRATE;
                decimal gross = d.TRANDGAMT > 0 ? d.TRANDGAMT : (qty * rate);

                rows.Add(new ChellanToDebitNoteDetailRow
                {
                    IsSelected = true,
                    MaterialId = d.TRANDREFID,
                    MaterialName = d.TRANDREFNAME,
                    HsnCode = hsn != null ? hsn.HSNCODE : string.Empty,
                    BillNo = d.TRANDREFNO,
                    BatchNo = batch != null ? batch.TRANBDNO : string.Empty,
                    ExpiryDate = batch != null ? (DateTime?)batch.TRANBEXPDATE : null,
                    PackingId = batch != null ? batch.PACKMID : 0,
                    Packing = pack != null ? pack.PACKMDESC : string.Empty,
                    Ptr = batch != null ? batch.TRANBPTRRATE : 0m,
                    Mrp = batch != null ? batch.TRANBMRP : 0m,
                    BoxQty = batch != null ? batch.TRANBQTY : 0m,
                    OriginalQty = qty,
                    Qty = qty,
                    Rate = rate,
                    Amount = gross
                });
            }

            var vm = new ChellanToDebitNoteViewModel
            {
                ChellanId = master.TRANMID,
                ChellanNo = master.TRANDNO,
                ChellanDate = master.TRANDATE,

                SupplierId = master.TRANREFID,
                SupplierName = supplier != null ? supplier.CATENAME : master.TRANREFNAME,
                SupplierCode = supplier != null ? supplier.CATECODE : string.Empty,
                Address1 = supplier != null ? supplier.CATEADDR1 : string.Empty,
                Address2 = supplier != null ? supplier.CATEADDR2 : string.Empty,
                Address3 = supplier != null ? supplier.CATEADDR3 : string.Empty,
                Address4 = supplier != null ? supplier.CATEADDR4 : string.Empty,
                City = location != null ? location.LOCTDESC : string.Empty,
                Pincode = supplier != null ? supplier.CATEADDR5 : string.Empty,
                State = state != null ? state.STATEDESC : string.Empty,
                StateCode = state != null ? state.STATECODE : string.Empty,
                GstNo = supplier != null ? supplier.CATE_GST_NO : string.Empty,

                DebitNoteDate = DateTime.Today,
                Status = 0,
                Narration = string.Empty,
                Remarks = string.Empty,
                TranStateType = master.TRANSTATETYPE,

                DetailRowsJson = JsonConvert.SerializeObject(rows)
            };

            ViewBag.HsnTaxMapJson = JsonConvert.SerializeObject(hsnTaxMap);

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "ChellanToDebitNote")]
        public ActionResult Save(ChellanToDebitNoteViewModel model)
        {
            try
            {
                if (model == null || model.ChellanId <= 0)
                {
                    TempData["ErrorMessage"] = "Invalid request.";
                    return RedirectToAction("Index", "Chellan");
                }

                var chellan = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == model.ChellanId && t.REGSTRID == ChellanRegisterId);
                if (chellan == null)
                {
                    TempData["ErrorMessage"] = "Chellan not found.";
                    return RedirectToAction("Index", "Chellan");
                }

                var rows = string.IsNullOrWhiteSpace(model.DetailRowsJson)
                    ? new List<ChellanToDebitNoteDetailRow>()
                    : (JsonConvert.DeserializeObject<List<ChellanToDebitNoteDetailRow>>(model.DetailRowsJson) ?? new List<ChellanToDebitNoteDetailRow>());

                rows = rows
                    .Where(r => r != null && r.IsSelected && r.MaterialId > 0 && r.Qty > 0)
                    .ToList();

                if (!rows.Any())
                {
                    TempData["ErrorMessage"] = "Please select at least one item row.";
                    return RedirectToAction("Form", new { id = model.ChellanId });
                }

                foreach (var r in rows)
                {
                    if (r.Qty > r.OriginalQty)
                    {
                        TempData["ErrorMessage"] = "Qty cannot be greater than Chellan Qty.";
                        return RedirectToAction("Form", new { id = model.ChellanId });
                    }
                }

                var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == chellan.TRANREFID);

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseReturnRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;

                string userName = User != null && User.Identity != null ? User.Identity.Name : string.Empty;

                var dnMaster = new TransactionMaster
                {
                    COMPYID = compyId,
                    SDPTID = 0,
                    REGSTRID = PurchaseReturnRegisterId,
                    TRANBTYPE = 1,

                    TRANNO = nextTranNo,
                    TRANDNO = FormatPurchaseReturnTrandNo(nextTranNo, model.DebitNoteDate),

                    TRANDATE = model.DebitNoteDate,
                    TRANTIME = DateTime.Now,

                    TRANREFID = chellan.TRANREFID,
                    TRANREFNAME = chellan.TRANREFNAME,
                    TRANSTATETYPE = chellan.TRANSTATETYPE,
                    TRANREFNO = chellan.TRANREFNO,

                    TRANLMID = chellan.TRANMID,

                    TRANNARTN = model.Narration,
                    TRANRMKS = model.Remarks,
                    DISPSTATUS = model.Status,

                    CUSRID = userName,
                    LMUSRID = userName,
                    PRCSDATE = DateTime.Now,
                    EXPRTSTATUS = 0,
                    TRANPCOUNT = 0
                };

                db.TransactionMasters.Add(dnMaster);
                db.SaveChanges();

                InsertDetails(dnMaster, rows);
                db.SaveChanges();

                if (User != null && User.IsInRole("PurchaseReturnIndex"))
                {
                    return RedirectToAction("Index", "PurchaseReturn");
                }

                return RedirectToAction("Index", "Chellan");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Form", new { id = model != null ? model.ChellanId : 0 });
            }
        }

        private void InsertDetails(TransactionMaster master, List<ChellanToDebitNoteDetailRow> details)
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

            var queryInsertBatch = @"INSERT INTO TRANSACTIONBATCHDETAIL (
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
                decimal gross = qty * rate;

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
                    TRANDREFNAME = material != null ? material.MTRLDESC : (d.MaterialName ?? string.Empty),
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
                        d.Ptr,
                        d.Mrp,
                        gross,
                        cgstExpr,
                        sgstExpr,
                        igstExpr,
                        cgstAmt,
                        sgstAmt,
                        igstAmt,
                        net,
                        0,
                        0,
                        totalQtyInt,
                        d.MaterialId
                    );
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
            master.TRANAMTWRDS = string.Empty;
        }

        private string FormatPurchaseReturnTrandNo(int tranNo, DateTime tranDate)
        {
            int fyStartYear = tranDate.Month >= 4 ? tranDate.Year : tranDate.Year - 1;
            int fyEndYear = fyStartYear + 1;
            string fyPrefix = (fyStartYear % 100).ToString("00") + "-" + (fyEndYear % 100).ToString("00");

            string seqText = tranNo.ToString("0000");
            return fyPrefix + "/DN" + seqText;
        }
    }
}
