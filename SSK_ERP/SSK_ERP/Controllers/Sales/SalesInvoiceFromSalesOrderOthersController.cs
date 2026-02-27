using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
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
            public decimal Rate { get; set; }
            public decimal CgstRate { get; set; }
            public decimal SgstRate { get; set; }
            public decimal IgstRate { get; set; }
        }

        private class SalesInvoiceDetailRow
        {
            public int MaterialId { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal ActualRate { get; set; }
            public decimal Amount { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int PackingId { get; set; }
            public decimal BoxQty { get; set; }
            public decimal Ptr { get; set; }
            public decimal Mrp { get; set; }
            public bool IsSelected { get; set; }
        }

        private class SalesInvoiceTaxFactorInput
        {
            public int CostFactorId { get; set; }
            public string ExpressionType { get; set; }
            public string Mode { get; set; }
            public decimal ExpressionValue { get; set; }
            public decimal Amount { get; set; }
        }

        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceEdit,SalesInvoiceIndex")]
        public ActionResult Form(int? id)
        {
            TransactionMaster model;

            if (id.HasValue && id.Value > 0)
            {
                model = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == SalesInvoiceRegisterId);
                if (model == null)
                {
                    TempData["ErrorMessage"] = "Sales Invoice not found.";
                    return RedirectToAction("Index", "SalesInvoice");
                }
            }
            else
            {
                model = new TransactionMaster
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
            }

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
        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceEdit,SalesInvoiceIndex")]
        public JsonResult GetInvoiceData(int invoiceId)
        {
            try
            {
                var inv = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == invoiceId && t.REGSTRID == SalesInvoiceRegisterId);
                if (inv == null)
                {
                    return Json(new { success = false, message = "Sales Invoice not found." }, JsonRequestBehavior.AllowGet);
                }

                var customer = inv.TRANREFID > 0 ? db.CustomerMasters.FirstOrDefault(c => c.CATEID == inv.TRANREFID) : null;
                var location = customer != null ? db.LocationMasters.FirstOrDefault(l => l.LOCTID == customer.LOCTID) : null;
                var state = customer != null ? db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID) : null;

                var details = db.TransactionDetails
                    .Where(d => d.TRANMID == inv.TRANMID)
                    .OrderBy(d => d.TRANDID)
                    .ToList();

                var detailIds = details.Select(d => d.TRANDID).ToList();
                var batchMap = db.TransactionBatchDetails
                    .Where(b => detailIds.Contains(b.TRANDID))
                    .GroupBy(b => b.TRANDID)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TRANBID).FirstOrDefault());

                var packIds = batchMap.Values.Where(b => b != null).Select(b => b.PACKMID).Distinct().ToList();
                var packMap = db.PackingMasters
                    .Where(p => packIds.Contains(p.PACKMID))
                    .ToDictionary(p => p.PACKMID, p => p.PACKMDESC);

                var hsnIds = details.Where(d => d.HSNID > 0).Select(d => d.HSNID).Distinct().ToList();
                var hsnMap = db.HSNCodeMasters
                    .Where(h => hsnIds.Contains(h.HSNID))
                    .ToDictionary(h => h.HSNID, h => h);

                // If this Sales Invoice is linked to a Sales Order (Others), show ALL Sales Order rows.
                // Mark rows already present in this invoice as selected; keep remaining rows visible unchecked.
                var items = new List<object>();
                if (inv.TRANLMID > 0)
                {
                    int salesOrderId = inv.TRANLMID;
                    var soDetails = db.TransactionDetails
                        .Where(d => d.TRANMID == salesOrderId)
                        .OrderBy(d => d.TRANDID)
                        .ToList();

                    var soMaterialIds = soDetails.Select(d => d.TRANDREFID).Distinct().ToList();
                    var soMaterialMap = db.MaterialMasters
                        .Where(m => soMaterialIds.Contains(m.MTRLID))
                        .ToDictionary(m => m.MTRLID, m => m);

                    var soHsnIds = soMaterialMap.Values.Where(m => m.HSNID > 0).Select(m => m.HSNID).Distinct().ToList();
                    var soHsnMap = db.HSNCodeMasters
                        .Where(h => soHsnIds.Contains(h.HSNID))
                        .ToDictionary(h => h.HSNID, h => h);

                    // Consume matches so duplicate materials can be handled in order
                    var remainingInvoiceDetails = details
                        .Select(d => new { Detail = d, Qty = d.TRANDQTY, MaterialId = d.TRANDREFID })
                        .ToList();

                    foreach (var soDet in soDetails)
                    {
                        soMaterialMap.TryGetValue(soDet.TRANDREFID, out var mat);
                        int hsnId = mat != null ? mat.HSNID : soDet.HSNID;
                        soHsnMap.TryGetValue(hsnId, out var soHsn);

                        // Default values from Sales Order (rate with 5% markup)
                        decimal baseRate = soDet.TRANDRATE;
                        decimal rate = Math.Round(baseRate * 1.05m, 2);

                        var match = remainingInvoiceDetails
                            .FirstOrDefault(x => x.MaterialId == soDet.TRANDREFID && x.Qty == soDet.TRANDQTY);

                        if (match == null)
                        {
                            match = remainingInvoiceDetails.FirstOrDefault(x => x.MaterialId == soDet.TRANDREFID);
                        }

                        bool isSelected = match != null;
                        var invDet = match != null ? match.Detail : null;
                        if (match != null)
                        {
                            remainingInvoiceDetails.Remove(match);
                        }

                        TransactionBatchDetail b = null;
                        if (invDet != null)
                        {
                            batchMap.TryGetValue(invDet.TRANDID, out b);
                        }

                        var packingId = b != null ? b.PACKMID : (invDet != null ? invDet.PACKMID : 0);
                        string packingName = string.Empty;
                        if (packingId > 0 && packMap.TryGetValue(packingId, out var pd))
                        {
                            packingName = pd;
                        }

                        items.Add(new
                        {
                            MaterialId = soDet.TRANDREFID,
                            MaterialName = mat != null ? mat.MTRLDESC : soDet.TRANDREFNAME,
                            HsnCode = soHsn != null ? soHsn.HSNCODE : string.Empty,
                            Qty = isSelected && invDet != null ? invDet.TRANDQTY : soDet.TRANDQTY,
                            Rate = isSelected && invDet != null ? invDet.TRANDRATE : rate,
                            ActualRate = isSelected && invDet != null ? invDet.TRANDARATE : 0m,
                            Amount = isSelected && invDet != null ? invDet.TRANDGAMT : Math.Round(soDet.TRANDQTY * rate, 2),
                            PackingId = packingId,
                            PackingName = packingName,
                            BoxQty = b != null ? b.TRANBQTY : 0,
                            BatchNo = b != null ? b.TRANBDNO : string.Empty,
                            ExpiryDate = b != null ? (DateTime?)b.TRANBEXPDATE : (DateTime?)null,
                            Ptr = b != null ? b.TRANBPTRRATE : 0m,
                            Mrp = b != null ? b.TRANBMRP : 0m,
                            CgstRate = soHsn != null ? soHsn.CGSTEXPRN : 0m,
                            SgstRate = soHsn != null ? soHsn.SGSTEXPRN : 0m,
                            IgstRate = soHsn != null ? soHsn.IGSTEXPRN : 0m,
                            IsSelected = isSelected
                        });
                    }
                }
                else
                {
                    items = details.Select(d =>
                    {
                        batchMap.TryGetValue(d.TRANDID, out var b);
                        hsnMap.TryGetValue(d.HSNID, out var hsn);

                        var packingId = b != null ? b.PACKMID : d.PACKMID;
                        string packingName = string.Empty;
                        if (packingId > 0 && packMap.TryGetValue(packingId, out var pd))
                        {
                            packingName = pd;
                        }

                        return new
                        {
                            MaterialId = d.TRANDREFID,
                            MaterialName = d.TRANDREFNAME,
                            HsnCode = hsn != null ? hsn.HSNCODE : string.Empty,
                            Qty = d.TRANDQTY,
                            Rate = d.TRANDRATE,
                            ActualRate = d.TRANDARATE,
                            Amount = d.TRANDGAMT,
                            PackingId = packingId,
                            PackingName = packingName,
                            BoxQty = b != null ? b.TRANBQTY : 0,
                            BatchNo = b != null ? b.TRANBDNO : string.Empty,
                            ExpiryDate = b != null ? (DateTime?)b.TRANBEXPDATE : (DateTime?)null,
                            Ptr = b != null ? b.TRANBPTRRATE : 0m,
                            Mrp = b != null ? b.TRANBMRP : 0m,
                            CgstRate = hsn != null ? hsn.CGSTEXPRN : 0m,
                            SgstRate = hsn != null ? hsn.SGSTEXPRN : 0m,
                            IgstRate = hsn != null ? hsn.IGSTEXPRN : 0m,
                            IsSelected = true
                        };
                    }).Cast<object>().ToList();
                }

                var header = new
                {
                    CustomerId = inv.TRANREFID,
                    CustomerName = inv.TRANREFNAME,
                    Address1 = customer != null ? customer.CATEADDR1 : string.Empty,
                    Address2 = customer != null ? customer.CATEADDR2 : string.Empty,
                    Pincode = customer != null ? customer.CATEADDR5 : string.Empty,
                    City = location != null ? location.LOCTDESC : string.Empty,
                    State = state != null ? state.STATEDESC : string.Empty,
                    StateType = state != null ? state.STATETYPE : inv.TRANSTATETYPE,
                    Country = "India",
                    SalesOrderId = inv.TRANLMID
                };

                string taxFactorsJson = null;
                try
                {
                    var factorRows = db.Database.SqlQuery<SalesInvoiceTaxFactorInput>(@"
                        SELECT
                            CFID AS CostFactorId,
                            CASE WHEN DEDTYPE = 1 THEN 'Value' ELSE '%' END AS ExpressionType,
                            CASE WHEN LTRIM(RTRIM(ISNULL(CAST(DEDMODE AS NVARCHAR(10)), '0'))) = '1' THEN '-' ELSE '+' END AS Mode,
                            CAST(ISNULL(DEDEXPRN, 0) AS DECIMAL(18, 6)) AS ExpressionValue,
                            CAST(ISNULL(DEDVALUE, 0) AS DECIMAL(18, 6)) AS Amount
                        FROM TRANSACTIONMASTERFACTOR
                        WHERE TRANMID = @p0
                        ORDER BY DEDORDR", inv.TRANMID).ToList();

                    if (factorRows != null && factorRows.Count > 0)
                    {
                        taxFactorsJson = JsonConvert.SerializeObject(factorRows);
                    }
                }
                catch
                {
                }

                return Json(new { success = true, header, items, taxFactorsJson }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
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
        public JsonResult GetBatches(int materialId, int packingId)
        {
            try
            {
                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var data = new List<object>();

                var conn = db.Database.Connection;
                var wasClosed = conn.State == ConnectionState.Closed;
                if (wasClosed)
                {
                    conn.Open();
                }

                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "dbo.PR_GetBatchNO_SalesINV_Others";
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.Add(new SqlParameter("@MTRLID", materialId));
                        cmd.Parameters.Add(new SqlParameter("@COMPYID", compyId));
                        cmd.Parameters.Add(new SqlParameter("@PACKMID", packingId));

                        using (var reader = cmd.ExecuteReader())
                        {
                            int GetOrd(params string[] names)
                            {
                                for (int i = 0; i < names.Length; i++)
                                {
                                    try
                                    {
                                        return reader.GetOrdinal(names[i]);
                                    }
                                    catch
                                    {
                                        // ignore
                                    }
                                }
                                return -1;
                            }

                            var ordBatchId = GetOrd("TRANBID", "BatchId", "BATCHID");
                            var ordBatchNo = GetOrd("BATCHNO2", "BATCHNO", "TRANBDNO", "BatchNo");
                            var ordExp = GetOrd("TRANBEXPDATE", "ExpiryDate", "EXPDATE");
                            var ordStock = GetOrd("MTRLSTKQTY", "StockQty", "STKQTY");

                            while (reader.Read())
                            {
                                int batchId = 0;
                                if (ordBatchId >= 0 && !reader.IsDBNull(ordBatchId))
                                {
                                    batchId = Convert.ToInt32(reader.GetValue(ordBatchId));
                                }

                                string batchNo = string.Empty;
                                if (ordBatchNo >= 0 && !reader.IsDBNull(ordBatchNo))
                                {
                                    batchNo = Convert.ToString(reader.GetValue(ordBatchNo));
                                }

                                DateTime? exp = null;
                                if (ordExp >= 0 && !reader.IsDBNull(ordExp))
                                {
                                    exp = Convert.ToDateTime(reader.GetValue(ordExp));
                                }

                                decimal stock = 0m;
                                if (ordStock >= 0 && !reader.IsDBNull(ordStock))
                                {
                                    stock = Convert.ToDecimal(reader.GetValue(ordStock));
                                }

                                if (!string.IsNullOrWhiteSpace(batchNo))
                                {
                                    data.Add(new { BatchId = batchId, BatchNo = batchNo, ExpiryDate = exp, StockQty = stock });
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (wasClosed && conn.State != ConnectionState.Closed)
                    {
                        conn.Close();
                    }
                }

                data = data.OrderBy(x => ((dynamic)x).BatchNo).ToList();

                return Json(new { success = true, data }, JsonRequestBehavior.AllowGet);
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
                    decimal cgstRate = 0m;
                    decimal sgstRate = 0m;
                    decimal igstRate = 0m;
                    if (m != null && m.HSNID > 0 && hsnMap.TryGetValue(m.HSNID, out var hsn))
                    {
                        hsnCode = hsn.HSNCODE;
                        cgstRate = hsn.CGSTEXPRN;
                        sgstRate = hsn.SGSTEXPRN;
                        igstRate = hsn.IGSTEXPRN;
                    }

                    // Apply 5% markup on Sales Order rate for Price Unit
                    decimal baseRate = d.TRANDRATE;
                    decimal rate = Math.Round(baseRate * 1.05m, 2);

                    itemRows.Add(new SalesOrderItemRow
                    {
                        MaterialId = d.TRANDREFID,
                        MaterialCode = m != null ? m.MTRLCODE : d.TRANDREFNO,
                        MaterialName = m != null ? m.MTRLDESC : d.TRANDREFNAME,
                        HsnCode = hsnCode,
                        Qty = d.TRANDQTY,
                        Rate = rate,
                        CgstRate = cgstRate,
                        SgstRate = sgstRate,
                        IgstRate = igstRate
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
        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceEdit,SalesInvoiceIndex")]
        public ActionResult savedata(TransactionMaster master, int salesOrderId, string detailRowsJson, string TaxFactorsJson)
        {
            try
            {
                bool isEdit = master != null && master.TRANMID > 0;

                if (!isEdit)
                {
                    // Prevent duplicate Sales Invoice creation from the same Sales Order
                    if (salesOrderId > 0)
                    {
                        bool alreadyHasInvoice = db.TransactionMasters.Any(t => t.REGSTRID == SalesInvoiceRegisterId && t.TRANLMID == salesOrderId);
                        if (alreadyHasInvoice)
                        {
                            TempData["ErrorMessage"] = "Sales Invoice already exists for this Sales Order.";
                            return RedirectToAction("Index", "SalesOrder");
                        }
                    }
                }

                if (salesOrderId <= 0)
                {
                    TempData["ErrorMessage"] = "Invalid Sales Order.";
                    return RedirectToAction("Index", "SalesOrder");
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<SalesInvoiceDetailRow>()
                    : JsonConvert.DeserializeObject<List<SalesInvoiceDetailRow>>(detailRowsJson) ?? new List<SalesInvoiceDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0)
                    .ToList();
                if (!details.Any())
                {
                    TempData["ErrorMessage"] = "Please add at least one detail row.";
                    return RedirectToAction("Form");
                }

                TransactionMaster so = null;
                if (salesOrderId > 0)
                {
                    so = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == salesOrderId && t.REGSTRID == SalesOrderRegisterId && t.TRANETYPE == 1);
                }

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                int customerIdForTax = 0;
                if (so != null)
                {
                    customerIdForTax = so.TRANREFID;
                }
                else if (master.TRANREFID > 0)
                {
                    customerIdForTax = master.TRANREFID;
                }

                var customer = customerIdForTax > 0 ? db.CustomerMasters.FirstOrDefault(c => c.CATEID == customerIdForTax) : null;
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

                bool isEditMode = master.TRANMID > 0;
                TransactionMaster workingMaster;

                if (isEditMode)
                {
                    workingMaster = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANMID && t.REGSTRID == SalesInvoiceRegisterId);
                    if (workingMaster == null)
                    {
                        TempData["ErrorMessage"] = "Sales Invoice not found.";
                        return RedirectToAction("Form");
                    }

                    // Remove existing details + batch rows
                    var oldDetails = db.TransactionDetails.Where(d => d.TRANMID == workingMaster.TRANMID).ToList();
                    var oldDetailIds = oldDetails.Select(d => d.TRANDID).ToList();

                    var oldBatches = db.TransactionBatchDetails.Where(b => oldDetailIds.Contains(b.TRANDID)).ToList();
                    if (oldBatches.Any())
                    {
                        db.TransactionBatchDetails.RemoveRange(oldBatches);
                    }
                    if (oldDetails.Any())
                    {
                        db.TransactionDetails.RemoveRange(oldDetails);
                    }

                    workingMaster.TRANDATE = invoiceDate;
                    workingMaster.TRANSTATETYPE = tranStateType;
                    workingMaster.TRAN_CRDPRDT = master.TRAN_CRDPRDT;
                    workingMaster.DISPSTATUS = master.DISPSTATUS;
                    workingMaster.TRANRMKS = master.TRANRMKS;
                    workingMaster.TRANTAXBILLNO = string.IsNullOrWhiteSpace(master.TRANTAXBILLNO) ? null : master.TRANTAXBILLNO;

                    if (so != null)
                    {
                        workingMaster.TRANREFNO = string.IsNullOrWhiteSpace(so.TRANREFNO) ? "-" : so.TRANREFNO;
                        workingMaster.TRANREFID = so.TRANREFID;
                        workingMaster.TRANREFNAME = so.TRANREFNAME;
                        workingMaster.TRANLMID = so.TRANMID;
                    }
                }
                else
                {
                    if (so == null)
                    {
                        TempData["ErrorMessage"] = "Sales Order not found.";
                        return RedirectToAction("Form");
                    }

                    var maxTranNo = db.TransactionMasters
                        .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesInvoiceRegisterId)
                        .Select(t => (int?)t.TRANNO)
                        .Max();

                    int nextTranNo = (maxTranNo ?? 0) + 1;

                    workingMaster = new TransactionMaster
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

                    db.TransactionMasters.Add(workingMaster);
                }

                // Reset totals
                workingMaster.TRANGAMT = 0m;
                workingMaster.TRANNAMT = 0m;
                workingMaster.TRANCGSTAMT = 0m;
                workingMaster.TRANSGSTAMT = 0m;
                workingMaster.TRANIGSTAMT = 0m;

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

                    HSNCodeMaster hsn = null;
                    if (hsnId > 0)
                    {
                        hsnMap.TryGetValue(hsnId, out hsn);
                    }

                    decimal gross = r.Amount > 0 ? r.Amount : (r.Qty * r.Rate);

                    decimal cgstAmt = 0m;
                    decimal sgstAmt = 0m;
                    decimal igstAmt = 0m;

                    if (hsn != null)
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
                        TRANMID = workingMaster.TRANMID,
                        TRANDREFID = r.MaterialId,
                        TRANDREFNO = string.IsNullOrWhiteSpace(refNo) ? "-" : refNo,
                        TRANDREFNAME = string.IsNullOrWhiteSpace(refName) ? "-" : refName,
                        TRANDMTRLPRFT = 0m,
                        HSNID = hsnId,
                        PACKMID = r.PackingId,
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

                    // Save detail first so TRANDID is generated for batch row
                    db.SaveChanges();

                    // Persist batch details if provided
                    if (!string.IsNullOrWhiteSpace(r.BatchNo) || r.PackingId > 0 || r.BoxQty > 0 || r.Ptr > 0 || r.Mrp > 0)
                    {
                        var batch = new TransactionBatchDetail
                        {
                            TRANDID = det.TRANDID,
                            AMTRLID = det.TRANDREFID,
                            HSNID = det.HSNID,
                            STKBID = 0,
                            TRANBDNO = string.IsNullOrWhiteSpace(r.BatchNo) ? string.Empty : r.BatchNo,
                            TRANBEXPDATE = r.ExpiryDate ?? DateTime.Today,
                            PACKMID = r.PackingId,
                            TRANPQTY = 0,
                            TRANBQTY = Convert.ToInt32(Math.Round(r.BoxQty, 0, MidpointRounding.AwayFromZero)),
                            TRANBRATE = det.TRANDRATE,
                            TRANBPTRRATE = r.Ptr,
                            TRANBMRP = r.Mrp,
                            TRANBGAMT = det.TRANDGAMT,
                            TRANBCGSTEXPRN = hsn != null ? hsn.CGSTEXPRN : 0m,
                            TRANBSGSTEXPRN = hsn != null ? hsn.SGSTEXPRN : 0m,
                            TRANBIGSTEXPRN = hsn != null ? hsn.IGSTEXPRN : 0m,
                            TRANBCGSTAMT = det.TRANDCGSTAMT,
                            TRANBSGSTAMT = det.TRANDSGSTAMT,
                            TRANBIGSTAMT = det.TRANDIGSTAMT,
                            TRANBNAMT = det.TRANDNAMT,
                            TRANBPID = 0,
                            TRANDPID = 0,
                            TRANPTQTY = Convert.ToInt32(Math.Round(det.TRANDQTY, 0, MidpointRounding.AwayFromZero)),
                            TRANBLMID = det.TRANDREFID
                        };
                        db.TransactionBatchDetails.Add(batch);
                        db.SaveChanges();
                    }

                    totalGross += gross;
                    totalNet += net;
                    workingMaster.TRANCGSTAMT += cgstAmt;
                    workingMaster.TRANSGSTAMT += sgstAmt;
                    workingMaster.TRANIGSTAMT += igstAmt;
                }

                workingMaster.TRANGAMT = totalGross;
                workingMaster.TRANNAMT = totalNet;
                workingMaster.TRANAMTWRDS = string.Empty;

                db.SaveChanges();

                try
                {
                    var taxInputs = new List<SalesInvoiceTaxFactorInput>();
                    if (!string.IsNullOrWhiteSpace(TaxFactorsJson))
                    {
                        try
                        {
                            taxInputs = JsonConvert.DeserializeObject<List<SalesInvoiceTaxFactorInput>>(TaxFactorsJson)
                                        ?? new List<SalesInvoiceTaxFactorInput>();
                        }
                        catch
                        {
                            taxInputs = new List<SalesInvoiceTaxFactorInput>();
                        }
                    }

                    // Always clear existing factors; then insert if any.
                    db.Database.ExecuteSqlCommand(
                        "DELETE FROM TRANSACTIONMASTERFACTOR WHERE TRANMID = @p0",
                        workingMaster.TRANMID);

                    if (taxInputs != null && taxInputs.Count > 0)
                    {
                        var cfIds = taxInputs
                            .Where(t => t != null && t.CostFactorId > 0)
                            .Select(t => t.CostFactorId)
                            .Distinct()
                            .ToList();

                        var cfMap = db.CostFactorMasters
                            .Where(c => cfIds.Contains(c.CFID))
                            .ToDictionary(c => c.CFID, c => c);

                        int dedOrder = 1;
                        decimal manualTotal = 0m;

                        foreach (var tax in taxInputs)
                        {
                            if (tax == null || tax.CostFactorId <= 0)
                            {
                                continue;
                            }

                            cfMap.TryGetValue(tax.CostFactorId, out var cf);

                            decimal expr = tax.ExpressionValue;
                            int dedType = (tax.ExpressionType != null &&
                                           tax.ExpressionType.Equals("Value", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
                            int dedMode = (tax.Mode != null && tax.Mode.Trim() == "-") ? 1 : 0;

                            decimal baseNet = totalNet;
                            decimal amount = 0m;
                            if (dedType == 1)
                            {
                                amount = expr;
                            }
                            else
                            {
                                amount = Math.Round((baseNet * expr) / 100m, 2);
                            }

                            if (dedMode == 1)
                            {
                                amount = -Math.Abs(amount);
                            }
                            else
                            {
                                amount = Math.Abs(amount);
                            }

                            manualTotal += amount;

                            short cfOptn = cf != null ? cf.CFOPTN : (short)0;
                            short dordrId = cf != null ? cf.DORDRID : (short)0;
                            string desc = cf != null ? cf.CFDESC : string.Empty;

                            db.Database.ExecuteSqlCommand(@"
                                INSERT INTO TRANSACTIONMASTERFACTOR (
                                    TRANMID, CFID, DEDEXPRN, DEDMODE, DEDTYPE, DEDORDR,
                                    CFOPTN, DORDRID, DEDVALUE, TRANCFDESC, CFHSNID,
                                    TRANCFCGSTEXPRN, TRANCFSGSTEXPRN, TRANCFIGSTEXPRN,
                                    TRANCFCGSTAMT, TRANCFSGSTAMT, TRANCFIGSTAMT
                                ) VALUES (
                                    @p0, @p1, @p2, @p3, @p4, @p5,
                                    @p6, @p7, @p8, @p9, @p10,
                                    @p11, @p12, @p13,
                                    @p14, @p15, @p16
                                )",
                                workingMaster.TRANMID,
                                tax.CostFactorId,
                                expr,
                                dedMode,
                                dedType,
                                dedOrder++,
                                cfOptn,
                                dordrId,
                                amount,
                                desc,
                                0,
                                0.00m,
                                0.00m,
                                0.00m,
                                0.00m,
                                0.00m,
                                0.00m
                            );
                        }

                        if (manualTotal != 0m)
                        {
                            workingMaster.TRANNAMT = totalNet + manualTotal;
                            db.SaveChanges();
                        }
                    }
                    else
                    {
                        // If no factors, ensure net is base net.
                        workingMaster.TRANNAMT = totalNet;
                        db.SaveChanges();
                    }
                }
                catch
                {
                }

                TempData["SuccessMessage"] = isEdit ? "Sales Invoice updated successfully." : "Sales Invoice created successfully.";
                return RedirectToAction("Index", "SalesInvoice");
            }
            catch (Exception ex)
            {
                var baseEx = ex.GetBaseException();
                string msg = baseEx != null ? baseEx.Message : ex.Message;
                if (ex.InnerException != null && baseEx != null && !string.Equals(baseEx.Message, ex.Message, StringComparison.OrdinalIgnoreCase))
                {
                    msg = ex.Message + " | " + baseEx.Message;
                }

                TempData["ErrorMessage"] = msg;
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
