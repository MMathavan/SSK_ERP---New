using System;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Purchase
{
    [SessionExpire]
    public class PurchaseInvoiceController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int PurchaseInvoiceRegisterId = 18;
        private const int SalesInvoiceRegisterId = 20;

        private class PurchaseInvoiceListRow
        {
            public int TRANMID { get; set; }
            public DateTime? EnteredDate { get; set; }
            public DateTime? PoDate { get; set; }
            public int TRANNO { get; set; }
            public string TRANDNO { get; set; }
            public string TRANREFNO { get; set; }
            public string CATENAME { get; set; }
            public decimal TRANNAMT { get; set; }
            public short? DISPSTATUS { get; set; }
            public string StatusDescription { get; set; }
        }

        private class PoNumberResult
        {
            public string POREFNO { get; set; }
        }


        // Index page for Purchase Invoice list
        [Authorize(Roles = "PurchaseInvoiceIndex")]
        public ActionResult Index()
        {
            return View();
        }

        // Ajax endpoint used by the PurchaseInvoice/Index DataTable.
        [HttpGet]
        [Authorize(Roles = "PurchaseInvoiceIndex")]
        public JsonResult GetAjaxData(JQueryDataTableParamModel param, string fromDate = null, string toDate = null)
        {
            try
            {
                var parameters = new System.Collections.Generic.List<object>();

                // Base query for purchase invoices (REGSTRID = 18).
                var sql = @"SELECT tm.TRANMID,
                                   tm.PRCSDATE AS EnteredDate,
                                   tm.TRANDATE AS PoDate,
                                   tm.TRANNO,
                                   tm.TRANDNO,
                                   tm.TRANREFNO,
                                   tm.TRANREFNAME AS CATENAME,
                                   ISNULL(tm.TRANNAMT, 0) as TRANNAMT,
                                   tm.DISPSTATUS,
                                   'N/A' as StatusDescription
                              FROM TRANSACTIONMASTER tm
                              WHERE tm.REGSTRID = 18";

                if (!string.IsNullOrEmpty(fromDate))
                {
                    sql += " AND tm.PRCSDATE >= @p0";
                    parameters.Add(DateTime.Parse(fromDate));
                }

                if (!string.IsNullOrEmpty(toDate))
                {
                    sql += " AND tm.PRCSDATE <= @p" + parameters.Count;
                    parameters.Add(DateTime.Parse(toDate).AddDays(1).AddSeconds(-1));
                }

                sql += " ORDER BY tm.PRCSDATE DESC, tm.TRANNO DESC";

                var invoices = db.Database.SqlQuery<PurchaseInvoiceListRow>(sql, parameters.ToArray()).ToList();

                var invoiceIds = invoices.Select(i => i.TRANMID).ToList();
                var salesInvoiceLinkSet = new System.Collections.Generic.HashSet<int>();
                if (invoiceIds.Any())
                {
                    salesInvoiceLinkSet = new System.Collections.Generic.HashSet<int>(
                        db.TransactionMasters
                            .Where(t => t.REGSTRID == SalesInvoiceRegisterId && t.TRANLMID > 0 && invoiceIds.Contains(t.TRANLMID))
                            .Select(t => t.TRANLMID)
                            .Distinct()
                            .ToList()
                    );
                }

                var allInvoices = invoices.Select(i => new
                {
                    TRANMID = i.TRANMID,
                    EnteredDate = i.EnteredDate,
                    PoDate = i.PoDate,
                    TRANNO = i.TRANNO,
                    TRANDNO = i.TRANDNO ?? "0000",
                    TRANREFNO = i.TRANREFNO ?? "-",
                    CATENAME = i.CATENAME ?? string.Empty,
                    TRANNAMT = i.TRANNAMT,
                    DISPSTATUS = i.DISPSTATUS,
                    StatusDescription = i.DISPSTATUS.HasValue
                        ? i.DISPSTATUS.Value.ToString()
                        : "N/A",
                    HasSalesInvoice = salesInvoiceLinkSet.Contains(i.TRANMID)
                }).ToList();

                return Json(new { aaData = allInvoices }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                // Fallback logic kept minimal here for brevity as it was just for specific legacy db issues
                System.Diagnostics.Debug.WriteLine("PurchaseInvoice GetAjaxData error: " + ex.Message);
                return Json(new { error = "Error loading data: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult DeleteInvoice(int id)
        {
            try
            {
                if (!User.IsInRole("PurchaseInvoiceDelete"))
                {
                    return Json(new { success = false, message = "Access Denied: You do not have permission to delete records. Please contact your administrator." });
                }

                var invoice = db.Database.SqlQuery<PurchaseInvoiceListRow>(
                    @"SELECT tm.TRANMID,
                             tm.PRCSDATE AS EnteredDate,
                             tm.TRANDATE AS PoDate,
                             tm.TRANNO,
                             tm.TRANDNO,
                             tm.TRANREFNO,
                             tm.TRANREFNAME AS CATENAME,
                             ISNULL(tm.TRANNAMT, 0) as TRANNAMT,
                             tm.DISPSTATUS,
                             'N/A' as StatusDescription
                      FROM TRANSACTIONMASTER tm
                      WHERE tm.TRANMID = @p0 AND tm.REGSTRID = 18",
                    id
                ).FirstOrDefault();

                if (invoice == null)
                {
                    return Json(new { success = false, message = "Invoice not found" });
                }

                bool hasSalesInvoice = db.TransactionMasters.Any(t => t.REGSTRID == SalesInvoiceRegisterId && t.TRANLMID == id);
                if (hasSalesInvoice)
                {
                    return Json(new { success = false, message = "Cannot delete this Purchase Invoice because a Sales Invoice has already been created." });
                }

                if (string.Equals(invoice.StatusDescription, "Approved", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = "Cannot delete an approved invoice. Please contact administrator if changes are needed." });
                }

                db.Database.ExecuteSqlCommand(
                    @"DELETE FROM TRANSACTIONBATCHDETAIL
                      WHERE TRANDID IN (SELECT TRANDID FROM TRANSACTIONDETAIL WHERE TRANMID = @p0)",
                    id);

                try
                {
                    db.Database.ExecuteSqlCommand(
                        "DELETE FROM TRANSACTIONMASTERFACTOR WHERE TRANMID = @p0",
                        id);
                }
                catch
                {
                    // Ignore if defined
                }

                db.Database.ExecuteSqlCommand(
                    "DELETE FROM TRANSACTIONDETAIL WHERE TRANMID = @p0",
                    id);

                db.Database.ExecuteSqlCommand(
                    "DELETE FROM TRANSACTIONMASTER WHERE TRANMID = @p0 AND REGSTRID = 18",
                    id);

                return Json(new { success = true, message = "Purchase invoice deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error deleting invoice: " + ex.Message });
            }
        }

        [Authorize(Roles = "PurchaseInvoiceCreate,PurchaseInvoiceEdit")]
        public ActionResult Form(int? id)
        {
            TransactionMaster model;
            var detailRows = new System.Collections.Generic.List<PurchaseInvoiceDetailRow>();

            if (id.HasValue && id.Value > 0)
            {
                if (!User.IsInRole("PurchaseInvoiceEdit"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to edit Purchase Invoices.";
                    return RedirectToAction("Index");
                }

                bool hasSalesInvoice = db.TransactionMasters.Any(t => t.REGSTRID == SalesInvoiceRegisterId && t.TRANLMID == id.Value);
                if (hasSalesInvoice)
                {
                    TempData["ErrorMessage"] = "This Purchase Invoice already has a Sales Invoice and cannot be edited.";
                    return RedirectToAction("Index");
                }

                model = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == PurchaseInvoiceRegisterId);
                if (model == null)
                {
                    TempData["ErrorMessage"] = "Purchase Invoice not found.";
                    return RedirectToAction("Index");
                }

                // Note: For editing, we need to fetch batch details too. 
                // Since TRANSACTIONBATCHDETAIL maps to TRANSACTIONDETAIL one-to-one (ideally) 
                // or one-to-many, we'll assume 1-to-1 for this form's simplified view if possible,
                // or take the first batch row.
                var details = db.TransactionDetails.Where(d => d.TRANMID == model.TRANMID).ToList();
                foreach (var d in details)
                {
                    var batch = db.Database.SqlQuery<TransactionBatchDetailRow>(
                        "SELECT TOP 1 * FROM TRANSACTIONBATCHDETAIL WHERE TRANDID = @p0", d.TRANDID).FirstOrDefault();

                    detailRows.Add(new PurchaseInvoiceDetailRow
                    {
                        MaterialId = d.TRANDREFID,
                        Qty = d.TRANDQTY, // Total Qty
                        Rate = d.TRANDRATE, // Price Per Unit
                        Amount = d.TRANDGAMT,
                        ActualRate = d.TRANDARATE,
                        // New fields
                        BatchNo = batch != null ? batch.TRANBDNO : "",
                        ExpiryDate = batch != null ? batch.TRANBEXPDATE : null,
                        PackingId = batch != null ? batch.PACKMID : 0,
                        Ptr = batch != null ? batch.TRANBPTRRATE : 0,
                        Mrp = batch != null ? batch.TRANBMRP : 0,
                        BoxQty = batch != null ? batch.TRANBQTY : 0
                    });
                }
            }
            else
            {
                if (!User.IsInRole("PurchaseInvoiceCreate"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to create Purchase Invoices.";
                    return RedirectToAction("Index");
                }

                model = new TransactionMaster
                {
                    TRANDATE = DateTime.Today,
                    TRANTIME = DateTime.Now,
                    DISPSTATUS = 0
                };

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseInvoiceRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;
                model.TRANNO = nextTranNo;
                model.TRANDNO = nextTranNo.ToString("D4");
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

            // If editing and TRANLMID is set, fetch PO Number using stored procedure
            string poNumberForDisplay = null;
            if (id.HasValue && id.Value > 0 && model.TRANLMID > 0)
            {
                try
                {
                    var poDetails = db.Database.SqlQuery<PoNumberResult>(
                        "EXEC PR_PONODETAILS_PUR_INV @Tranlmid",
                        new System.Data.SqlClient.SqlParameter("@Tranlmid", model.TRANLMID)
                    ).FirstOrDefault();
                    
                    if (poDetails != null && !string.IsNullOrEmpty(poDetails.POREFNO))
                    {
                        poNumberForDisplay = poDetails.POREFNO;
                    }
                }
                catch
                {
                    // If SP fails, fall back to showing empty or TRANREFNO
                    poNumberForDisplay = model.TRANREFNO;
                }
            }
            
            ViewBag.PoNumberForDisplay = poNumberForDisplay;
            ViewBag.IsEditMode = id.HasValue && id.Value > 0;


            ViewBag.DetailRowsJson = detailRows.Any()
                ? Newtonsoft.Json.JsonConvert.SerializeObject(detailRows)
                : "[]";

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
            // Pass full list for JS to use
            ViewBag.PackingListJson = Newtonsoft.Json.JsonConvert.SerializeObject(packingList);

            ViewBag.StateTypeList = new SelectList(
                new[]
                {
                    new { Value = "0", Text = "Local" },
                    new { Value = "1", Text = "Interstate" }
                },
                "Value",
                "Text",
                model.TRANSTATETYPE.ToString()
            );

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "PurchaseInvoiceCreate,PurchaseInvoiceEdit")]
        public ActionResult savedata(TransactionMaster master, string detailRowsJson)
        {
            try
            {
                bool isEdit = master.TRANMID > 0 &&
                              db.TransactionMasters.Any(t => t.TRANMID == master.TRANMID && t.REGSTRID == PurchaseInvoiceRegisterId);

                if (isEdit)
                {
                    if (!User.IsInRole("PurchaseInvoiceEdit"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to edit Purchase Invoices.";
                        return RedirectToAction("Index");
                    }
                }
                else
                {
                    if (!User.IsInRole("PurchaseInvoiceCreate"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to create Purchase Invoices.";
                        return RedirectToAction("Index");
                    }
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new System.Collections.Generic.List<PurchaseInvoiceDetailRow>()
                    : Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<PurchaseInvoiceDetailRow>>(detailRowsJson) ?? new System.Collections.Generic.List<PurchaseInvoiceDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0)
                    .ToList();

                if (!details.Any())
                {
                    TempData["ErrorMessage"] = "Please add at least one detail row.";
                    return RedirectToAction("Form", new { id = isEdit ? (int?)master.TRANMID : null });
                }

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;
                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                if (master.TRANREFID <= 0)
                {
                    TempData["ErrorMessage"] = "Please select a supplier.";
                    return RedirectToAction("Form", new { id = isEdit ? (int?)master.TRANMID : null });
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
                master.COMPYID = compyId;
                master.SDPTID = 0;
                master.REGSTRID = PurchaseInvoiceRegisterId;
                master.TRANBTYPE = 0;
                master.EXPRTSTATUS = 0;
                master.TRANTIME = DateTime.Now;
                if (string.IsNullOrWhiteSpace(master.TRANREFNO))
                {
                    master.TRANREFNO = "-";
                }

                if (isEdit)
                {
                    var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANMID && t.REGSTRID == PurchaseInvoiceRegisterId);
                    if (existing == null)
                    {
                        TempData["ErrorMessage"] = "Purchase Invoice not found.";
                        return RedirectToAction("Index");
                    }

                    existing.TRANDATE = master.TRANDATE;
                    existing.TRANTIME = master.TRANTIME;
                    existing.TRANREFID = master.TRANREFID;
                    existing.TRANREFNAME = master.TRANREFNAME;
                    existing.TRANSTATETYPE = master.TRANSTATETYPE;
                    // existing.TRANREFNO = master.TRANREFNO; // PO Number - Not updated in edit mode (comes from upload)
                    existing.TRANRMKS = master.TRANRMKS;
                    existing.DISPSTATUS = master.DISPSTATUS;
                    existing.LMUSRID = userName;
                    existing.PRCSDATE = DateTime.Now;

                    // Delete existing details (Batch details cascade delete via FK usually, but let's be safe or rely on cascade)
                    // We need to delete Batch Details manually if there's no cascade
                    var existingDetailIds = db.TransactionDetails.Where(d => d.TRANMID == existing.TRANMID).Select(d => d.TRANDID).ToList();
                    if (existingDetailIds.Any())
                    {
                        // Delete batch details
                         db.Database.ExecuteSqlCommand(
                             $"DELETE FROM TRANSACTIONBATCHDETAIL WHERE TRANDID IN ({string.Join(",", existingDetailIds)})");
                         
                         // Delete details
                         db.TransactionDetails.RemoveRange(db.TransactionDetails.Where(d => d.TRANMID == existing.TRANMID));
                    }
                    
                    try
                    {
                        db.Database.ExecuteSqlCommand("DELETE FROM TRANSACTIONMASTERFACTOR WHERE TRANMID = @p0", existing.TRANMID);
                    }
                    catch { }

                    db.SaveChanges(); // Commit delete first
                    InsertDetails(existing, details);
                    db.SaveChanges();
                }
                else
                {
                    var maxTranNo = db.TransactionMasters
                        .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseInvoiceRegisterId)
                        .Select(t => (int?)t.TRANNO)
                        .Max();

                    int nextTranNo = (maxTranNo ?? 0) + 1;
                    master.TRANNO = nextTranNo;
                    if (string.IsNullOrWhiteSpace(master.TRANDNO))
                    {
                        master.TRANDNO = nextTranNo.ToString("D4");
                    }

                    master.CUSRID = userName;
                    master.LMUSRID = userName;
                    master.PRCSDATE = DateTime.Now;
                    master.TRANPCOUNT = 0;

                    db.TransactionMasters.Add(master);
                    db.SaveChanges();

                    InsertDetails(master, details);
                    db.SaveChanges(); // Commit details to get IDs? No, `InsertDetails` calls SaveChanges.
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        private void InsertDetails(TransactionMaster master, System.Collections.Generic.List<PurchaseInvoiceDetailRow> details)
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
            decimal totalCgst = 0m;
            decimal totalSgst = 0m;
            decimal totalIgst = 0m;
            decimal totalNet = 0m;
            
            short tranStateType = master.TRANSTATETYPE;
            int tranMid = master.TRANMID;

            foreach (var d in details)
            {
                materials.TryGetValue(d.MaterialId, out var material);

                int hsnId = material != null ? material.HSNID : 0;
                hsnMap.TryGetValue(hsnId, out var hsn);

                decimal qty = d.Qty;
                decimal rate = d.Rate; // This is Price Per Unit
                decimal actualRate = d.ActualRate > 0 ? d.ActualRate : rate;
                
                decimal gross = d.Amount > 0 ? d.Amount : qty * rate; // Usually Amount = Qty * Price

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
                            cgstExpr = hsn.CGSTEXPRN;
                            cgstAmt = Math.Round((gross * hsn.CGSTEXPRN) / 100m, 2);
                        }

                        if (hsn.SGSTEXPRN > 0)
                        {
                            sgstExpr = hsn.SGSTEXPRN;
                            sgstAmt = Math.Round((gross * hsn.SGSTEXPRN) / 100m, 2);
                        }
                    }
                    else
                    {
                        if (hsn.IGSTEXPRN > 0)
                        {
                            igstExpr = hsn.IGSTEXPRN;
                            igstAmt = Math.Round((gross * hsn.IGSTEXPRN) / 100m, 2);
                        }
                    }
                }

                decimal net = gross + cgstAmt + sgstAmt + igstAmt;

                var detail = new TransactionDetail
                {
                    TRANMID = tranMid,
                    TRANDREFID = material != null ? material.MTRLID : d.MaterialId,
                    TRANDREFNO = material != null ? material.MTRLCODE : string.Empty,
                    TRANDREFNAME = material != null ? material.MTRLDESC : string.Empty,
                    TRANDMTRLPRFT = 0, 
                    HSNID = hsnId,
                    PACKMID = d.PackingId,
                    TRANDQTY = qty,
                    TRANDRATE = rate,
                    TRANDARATE = actualRate,
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
                // We need to save to get TRANDID to insert Batch Detail
                db.SaveChanges(); // This commits the transaction detail

                // Now insert into TRANSACTIONBATCHDETAIL
                // Schema from Upload Controller:
                // TRANDID, AMTRLID, HSNID, STKBID, TRANBDNO, TRANBEXPDATE, PACKMID, TRANPQTY, TRANBQTY, TRANBRATE, TRANBPTRRATE, TRANBMRP, TRANBGAMT...
                
                var queryInsertBatch = @"INSERT INTO TRANSACTIONBATCHDETAIL (
                    TRANDID, AMTRLID, HSNID, STKBID, TRANBDNO, TRANBEXPDATE, PACKMID, 
                    TRANPQTY, TRANBQTY, TRANBRATE, TRANBPTRRATE, TRANBMRP, 
                    TRANBGAMT, TRANBCGSTEXPRN, TRANBSGSTEXPRN, TRANBIGSTEXPRN, 
                    TRANBCGSTAMT, TRANBSGSTAMT, TRANBIGSTAMT, TRANBNAMT, 
                    TRANBPID, TRANDPID, TRANPTQTY
                ) VALUES (
                    @p0, @p1, @p2, @p3, @p4, @p5, @p6,
                    @p7, @p8, @p9, @p10, @p11,
                    @p12, @p13, @p14, @p15,
                    @p16, @p17, @p18, @p19,
                    @p20, @p21, @p22
                )";

                db.Database.ExecuteSqlCommand(queryInsertBatch,
                    detail.TRANDID,                     // p0 TRANDID
                    detail.TRANDREFID,                  // p1 AMTRLID
                    detail.HSNID,                       // p2 HSNID
                    0,                                  // p3 STKBID
                    d.BatchNo ?? "",                    // p4 TRANBDNO
                    d.ExpiryDate ?? DateTime.Today,     // p5 TRANBEXPDATE
                    d.PackingId,                        // p6 PACKMID
                    0,                                  // p7 TRANPQTY (Qty per box?) - Setting 0 as not asked
                    d.BoxQty,                           // p8 TRANBQTY (No of boxes)
                    rate,                               // p9 TRANBRATE (Price per unit)
                    d.Ptr,                              // p10 TRANBPTRRATE
                    d.Mrp,                              // p11 TRANBMRP
                    gross,                              // p12 TRANBGAMT
                    cgstExpr,                           // p13 TRANBCGSTEXPRN
                    sgstExpr,                           // p14 TRANBSGSTEXPRN
                    igstExpr,                           // p15 TRANBIGSTEXPRN
                    cgstAmt,                            // p16 TRANBCGSTAMT
                    sgstAmt,                            // p17 TRANBSGSTAMT
                    igstAmt,                            // p18 TRANBIGSTAMT
                    net,                                // p19 TRANBNAMT
                    0,                                  // p20 TRANBPID
                    0,                                  // p21 TRANDPID
                    qty                                 // p22 TRANPTQTY (Total Qty)
                );

                totalGross += gross;
                totalCgst += cgstAmt;
                totalSgst += sgstAmt;
                totalIgst += igstAmt;
                totalNet += net;
            }

            master.TRANGAMT = totalGross;
            master.TRANCGSTAMT = totalCgst;
            master.TRANSGSTAMT = totalSgst;
            master.TRANIGSTAMT = totalIgst;
            master.TRANNAMT = totalNet;
            master.TRANPCOUNT = 0;
            master.TRANAMTWRDS = ConvertAmountToWords(totalNet);

            db.SaveChanges(); // Update master totals
        }

        private class TransactionBatchDetailRow
        {
            public int TRANBID { get; set; }
            public int TRANDID { get; set; }
            public int AMTRLID { get; set; }
            public string TRANBDNO { get; set; }
            public DateTime? TRANBEXPDATE { get; set; }
            public int PACKMID { get; set; }
            public decimal TRANBPTRRATE { get; set; }
            public decimal TRANBMRP { get; set; }
            public int TRANBQTY { get; set; }
        }

        private class PurchaseInvoiceDetailRow
        {
            public int MaterialId { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; } // Matches Price Per Unit
            public decimal Amount { get; set; }
            public decimal ActualRate { get; set; }
            // New Fields
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int PackingId { get; set; }
            public decimal Ptr { get; set; }
            public decimal Mrp { get; set; }
            public decimal BoxQty { get; set; }
        }

        [HttpGet]
        public JsonResult GetSupplierDetails(int id)
        {
            try
            {
                var supplier = db.SupplierMasters.FirstOrDefault(c => c.CATEID == id && c.DISPSTATUS == 0);
                if (supplier == null)
                {
                    return Json(new { success = false, message = "Supplier not found." }, JsonRequestBehavior.AllowGet);
                }

                var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == supplier.LOCTID);
                var state = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);

                var data = new
                {
                    Id = supplier.CATEID,
                    Name = supplier.CATENAME,
                    Address1 = supplier.CATEADDR1,
                    Address2 = supplier.CATEADDR2,
                    Address3 = supplier.CATEADDR3,
                    Address4 = supplier.CATEADDR4,
                    Pincode = supplier.CATEADDR5,
                    City = location != null ? location.LOCTDESC : string.Empty,
                    State = state != null ? state.STATEDESC : string.Empty,
                    StateCode = state != null ? state.STATECODE : string.Empty,
                    StateType = state != null ? state.STATETYPE : (short)0,
                    Country = "India"
                };

                return Json(new { success = true, data }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetPurchaseMaterials()
        {
            try
            {
                var materials = (from m in db.MaterialMasters
                                 join h in db.HSNCodeMasters on m.HSNID equals h.HSNID into h_join
                                 from h in h_join.DefaultIfEmpty()
                                 orderby m.MTRLDESC
                                 select new
                                 {
                                     id = m.MTRLID,
                                     name = m.MTRLDESC,
                                     groupId = m.MTRLGID,
                                     rate = m.RATE,
                                     hsnCode = h != null ? h.HSNCODE : ""
                                 }).ToList();

                var groups = db.MaterialGroupMasters
                    .OrderBy(g => g.MTRLGDESC)
                    .Select(g => new
                    {
                        id = g.MTRLGID,
                        name = g.MTRLGDESC
                    })
                    .ToList();

                return Json(new { success = true, materials, groups }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetCostFactorsForPurchase()
        {
            try
            {
                var items = db.CostFactorMasters
                    .Where(c => c.DISPSTATUS == 0)
                    .OrderBy(c => c.CFDESC)
                    .Select(c => new
                    {
                        id = c.CFID,
                        name = c.CFDESC
                    })
                    .ToList();

                return Json(new { success = true, items }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult CalculatePurchaseInvoiceTax(short stateType, string detailRowsJson)
        {
            try
            {
                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new System.Collections.Generic.List<PurchaseInvoiceDetailRow>()
                    : Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<PurchaseInvoiceDetailRow>>(detailRowsJson) ?? new System.Collections.Generic.List<PurchaseInvoiceDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0)
                    .ToList();

                if (!details.Any())
                {
                    return Json(new
                    {
                        success = true,
                        gross = 0m,
                        cgst = 0m,
                        sgst = 0m,
                        igst = 0m,
                        net = 0m
                    });
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

                foreach (var d in details)
                {
                    materials.TryGetValue(d.MaterialId, out var material);

                    int hsnId = material != null ? material.HSNID : 0;
                    hsnMap.TryGetValue(hsnId, out var hsn);

                    decimal qty = d.Qty;
                    decimal rate = d.Rate; // Price per unit
                    // decimal actualRate = d.ActualRate > 0 ? d.ActualRate : rate;
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

                    totalGross += gross;
                    totalCgst += cgstAmt;
                    totalSgst += sgstAmt;
                    totalIgst += igstAmt;
                }

                decimal net = totalGross + totalCgst + totalSgst + totalIgst;

                return Json(new
                {
                    success = true,
                    gross = totalGross,
                    cgst = totalCgst,
                    sgst = totalSgst,
                    igst = totalIgst,
                    net
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        private string ConvertAmountToWords(decimal amount)
        {
            try
            {
                string[] ones = { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
                string[] teens = { "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
                string[] tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

                if (amount == 0) return "Zero Rupees Only";

                int rupees = (int)amount;
                int paise = (int)((amount - rupees) * 100);

                string words = string.Empty;

                if (rupees >= 10000000)
                {
                    words += ConvertNumberToWords(rupees / 10000000, ones, teens, tens) + " Crore ";
                    rupees %= 10000000;
                }
                if (rupees >= 100000)
                {
                    words += ConvertNumberToWords(rupees / 100000, ones, teens, tens) + " Lakh ";
                    rupees %= 100000;
                }
                if (rupees >= 1000)
                {
                    words += ConvertNumberToWords(rupees / 1000, ones, teens, tens) + " Thousand ";
                    rupees %= 1000;
                }
                if (rupees >= 100)
                {
                    words += ConvertNumberToWords(rupees / 100, ones, teens, tens) + " Hundred ";
                    rupees %= 100;
                }
                if (rupees > 0)
                {
                    words += ConvertNumberToWords(rupees, ones, teens, tens);
                }

                words = words.Trim() + " Rupees";

                if (paise > 0)
                {
                    words += " and " + ConvertNumberToWords(paise, ones, teens, tens) + " Paise";
                }

                return words + " Only";
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ConvertNumberToWords(int number, string[] ones, string[] teens, string[] tens)
        {
            if (number < 10) return ones[number];
            if (number < 20) return teens[number - 10];
            if (number < 100) return tens[number / 10] + (number % 10 > 0 ? " " + ones[number % 10] : string.Empty);
            return string.Empty;
        }

        [Authorize(Roles = "PurchaseInvoiceEdit")]
        public ActionResult Edit(int? id)
        {
            return RedirectToAction("Form", new { id = id });
        }

        [Authorize(Roles = "PurchaseInvoicePrint")]
        public ActionResult Print(int? id)
        {
            if (!id.HasValue)
            {
                return RedirectToAction("Index");
            }

            var model = new PurchaseInvoicePrintViewModel();
            var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == PurchaseInvoiceRegisterId);

            if (master == null)
            {
                return RedirectToAction("Index");
            }

            model.TRANMID = master.TRANMID;
            model.TRANDNO = master.TRANDNO;
            model.TRANDATE = master.TRANDATE;
            model.SupplierName = master.TRANREFNAME;
            model.PONumber = master.TRANREFNO;
            
            // Fetch Supplier Address
            var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == master.TRANREFID);
            if (supplier != null)
            {
                model.SupplierCode = supplier.CATECODE;
                model.Address1 = supplier.CATEADDR1;
                model.Address2 = supplier.CATEADDR2;
                model.Address3 = supplier.CATEADDR3;
                model.Address4 = supplier.CATEADDR4; // Often empty or Pincode
                model.StateCode = ""; // If needed from StateMaster
                model.GstNo = supplier.CATE_GST_NO;

                var state = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);
                if (state != null)
                {
                    model.State = state.STATEDESC;
                    model.StateCode = state.STATECODE;
                }
            }

            // Fetch Company Address (Hardcoded or from Session/DB)
            // Fetch Company Address - formatted for display
             model.CompanyAddress = "Room No. B, Kothari Warehouse, Madhavaram Redhills Road,\nVadaperumbakkam, Puzhal, Chennai 600060.";

            model.GrossAmount = master.TRANGAMT;
            model.CGSTAmount = master.TRANCGSTAMT;
            model.SGSTAmount = master.TRANSGSTAMT;
            model.IGSTAmount = master.TRANIGSTAMT;
            model.NetAmount = master.TRANNAMT;
            model.AmountInWords = master.TRANAMTWRDS;
            model.Remarks = master.TRANRMKS;

            // Fetch Items
            var details = db.TransactionDetails.Where(d => d.TRANMID == master.TRANMID).ToList();
            model.Items = new System.Collections.Generic.List<PurchaseInvoicePrintItem>();

            foreach (var d in details)
            {
                var batch = db.Database.SqlQuery<TransactionBatchDetailRow>(
                        "SELECT TOP 1 * FROM TRANSACTIONBATCHDETAIL WHERE TRANDID = @p0", d.TRANDID).FirstOrDefault();
                
                // Get HSN Code
                var hsnCode = "";
                if(d.HSNID > 0)
                {
                    var hsn = db.HSNCodeMasters.FirstOrDefault(h => h.HSNID == d.HSNID);
                    if(hsn != null) hsnCode = hsn.HSNCODE;
                }

                model.Items.Add(new PurchaseInvoicePrintItem
                {
                    MaterialName = d.TRANDREFNAME,
                    HSNCode = hsnCode,
                    BatchNo = batch != null ? batch.TRANBDNO : "",
                    ExpiryDate = batch != null ? batch.TRANBEXPDATE : null,
                    Qty = d.TRANDQTY,
                    Rate = d.TRANDRATE,
                    Amount = d.TRANDGAMT,
                    Mrp = batch != null ? batch.TRANBMRP : 0
                });
            }

            return View(model);
        }

        public class PurchaseInvoicePrintViewModel
        {
            public int TRANMID { get; set; }
            public string TRANDNO { get; set; }
            public DateTime TRANDATE { get; set; }
            public string PONumber { get; set; }
            
            public string SupplierName { get; set; }
            public string SupplierCode { get; set; }
            public string Address1 { get; set; }
            public string Address2 { get; set; }
            public string Address3 { get; set; }
            public string Address4 { get; set; }
            public string State { get; set; }
            public string StateCode { get; set; }
            public string GstNo { get; set; }
            
            public string CompanyAddress { get; set; }

            public decimal GrossAmount { get; set; }
            public decimal CGSTAmount { get; set; }
            public decimal SGSTAmount { get; set; }
            public decimal IGSTAmount { get; set; }
            public decimal NetAmount { get; set; }
            public string AmountInWords { get; set; }
            public string Remarks { get; set; }

            public System.Collections.Generic.List<PurchaseInvoicePrintItem> Items { get; set; }
        }

        public class PurchaseInvoicePrintItem
        {
            public string MaterialName { get; set; }
            public string HSNCode { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public decimal Mrp { get; set; }
        }
    }

}
