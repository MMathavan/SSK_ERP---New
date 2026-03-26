using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using System.Data;
using System.Data.SqlClient;
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
        private const int PurchaseInvoiceRegisterId = 18;

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

        private class DebitNoteDetailRow
        {
            public bool IsSelected { get; set; }
            public int MaterialId { get; set; }
            public string MaterialName { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public string HsnCode { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int PackingId { get; set; }
            public decimal Ptr { get; set; }
            public decimal Mrp { get; set; }
            public decimal BoxQty { get; set; }
            public string Packing { get; set; }
            public string BillNo { get; set; }
            public int SourceBatchId { get; set; }
            public int SourceDetailId { get; set; }
            public int SourceRefId { get; set; }
            public decimal ActualQty { get; set; }
        }

        public class DebitNotePrintViewModel
        {
            public int TRANMID { get; set; }
            public int TRANNO { get; set; }
            public string TRANDNO { get; set; }
            public string TRANREFNO { get; set; }
            public DateTime TRANDATE { get; set; }

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

            public string CompanyName { get; set; }
            public string CompanyAddress { get; set; }
            public string CompanyGstNo { get; set; }

            public decimal GrossAmount { get; set; }
            public decimal NetAmount { get; set; }
            public string AmountInWords { get; set; }

            public int TotalItems { get; set; }
            public decimal TotalQty { get; set; }

            public decimal CgstAmount { get; set; }
            public decimal SgstAmount { get; set; }
            public decimal IgstAmount { get; set; }

            public decimal TotalDisc { get; set; }
            public decimal CourierCharges { get; set; }

            public string Narration { get; set; }
            public string Remarks { get; set; }
            public string UserName { get; set; }
            public DateTime? BillingTime { get; set; }

            public IList<DebitNotePrintItemViewModel> Items { get; set; }
            public IList<DebitNoteClassSummaryViewModel> ClassSummaries { get; set; }
        }

        public class DebitNoteClassSummaryViewModel
        {
            public string ClassName { get; set; }
            public decimal GstPercent { get; set; }
            public decimal Scheme { get; set; }
            public decimal Discount { get; set; }
            public decimal Total { get; set; }
            public decimal Sgst { get; set; }
            public decimal Cgst { get; set; }
            public decimal TotalGst { get; set; }
        }

        public class DebitNotePrintItemViewModel
        {
            public string Division { get; set; }
            public string MaterialName { get; set; }
            public string Pack { get; set; }
            public decimal Qty { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public string HSNCode { get; set; }
            public decimal Rate { get; set; }
            public decimal Ptr { get; set; }
            public decimal Mrp { get; set; }
            public decimal Dis1 { get; set; }
            public decimal DisPercent { get; set; }
            public decimal DiscountAmount { get; set; }
            public decimal SgstRate { get; set; }
            public decimal CgstRate { get; set; }
            public decimal Amount { get; set; }
            public decimal NetAmount { get; set; }
        }

        [Authorize(Roles = "ChellanToDebitNoteDebitNoteIndex")]
        public ActionResult DebitNoteIndex()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteIndex")]
        public JsonResult DebitNoteGetAjaxData(string fromDate = null, string toDate = null)
        {
            try
            {
                var query = db.TransactionMasters.Where(t => t.REGSTRID == PurchaseReturnRegisterId);

                DateTime parsedFrom;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out parsedFrom))
                {
                    query = query.Where(t => t.TRANDATE >= parsedFrom);
                }

                DateTime parsedTo;
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out parsedTo))
                {
                    var exclusiveTo = parsedTo.Date.AddDays(1);
                    query = query.Where(t => t.TRANDATE < exclusiveTo);
                }

                var items = query
                    .OrderByDescending(t => t.TRANDATE)
                    .ThenByDescending(t => t.TRANMID)
                    .ToList()
                    .Select(t => new
                    {
                        t.TRANMID,
                        t.TRANDATE,
                        t.TRANNO,
                        TRANDNO = t.TRANDNO ?? "0000",
                        t.TRANREFNO,
                        SupplierName = t.TRANREFNAME,
                        Amount = t.TRANNAMT,
                        TranBType = t.TRANBTYPE,
                        StatusDescription = t.DISPSTATUS == 0 ? "Enabled" : "Disabled"
                    })
                    .ToList();

                return Json(new { data = items }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [Authorize(Roles = "ChellanToDebitNoteDebitNoteEdit")]
        public ActionResult DebitNoteForm(int? id)
        {
            if (!id.HasValue || id.Value <= 0)
            {
                TempData["ErrorMessage"] = "Invalid Debit Note.";
                return RedirectToAction("DebitNoteIndex");
            }

            var model = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == PurchaseReturnRegisterId);
            if (model == null)
            {
                TempData["ErrorMessage"] = "Debit Note not found.";
                return RedirectToAction("DebitNoteIndex");
            }

            var detailRows = new List<DebitNoteDetailRow>();

            var details = db.TransactionDetails.Where(d => d.TRANMID == model.TRANMID).ToList();
            var materialIds = details.Select(d => d.TRANDREFID).Distinct().ToList();
            var materials = db.MaterialMasters
                .Where(m => materialIds.Contains(m.MTRLID))
                .ToDictionary(m => m.MTRLID, m => m);

            var hsnIds = materials.Values.Where(m => m.HSNID > 0).Select(m => m.HSNID).Distinct().ToList();
            var hsnMap = db.HSNCodeMasters
                .Where(h => hsnIds.Contains(h.HSNID))
                .ToDictionary(h => h.HSNID, h => h);

            var detailIds = details.Select(d => d.TRANDID).ToList();
            var batchDetails = db.TransactionBatchDetails
                .Where(b => detailIds.Contains(b.TRANDID))
                .ToList();

            var sourceBatchIds = batchDetails
                .Where(b => b.TRANBPID > 0)
                .Select(b => b.TRANBPID)
                .Distinct()
                .ToList();

            var sourceBatchMap = new Dictionary<int, TransactionBatchDetail>();
            if (sourceBatchIds.Any())
            {
                var srcBatches = db.TransactionBatchDetails
                    .Where(b => sourceBatchIds.Contains(b.TRANBID))
                    .ToList();

                sourceBatchMap = srcBatches.ToDictionary(b => b.TRANBID, b => b);
            }

            var packingIds = batchDetails
                .Select(b => b.PACKMID)
                .Distinct()
                .ToList();

            var packingMap = db.PackingMasters
                .Where(p => packingIds.Contains(p.PACKMID))
                .ToDictionary(p => p.PACKMID, p => p.PACKMDESC);

            foreach (var d in details)
            {
                materials.TryGetValue(d.TRANDREFID, out var material);
                string hsnCode = string.Empty;
                if (material != null && material.HSNID > 0 && hsnMap.TryGetValue(material.HSNID, out var hsn))
                {
                    hsnCode = hsn.HSNCODE;
                }

                var batch = batchDetails.FirstOrDefault(b => b.TRANDID == d.TRANDID);

                string packingDesc = string.Empty;
                if (batch != null && packingMap.TryGetValue(batch.PACKMID, out var pDesc))
                {
                    packingDesc = pDesc;
                }

                decimal actualQty = d.TRANDQTY;
                if (batch != null && batch.TRANBPID > 0 && sourceBatchMap.TryGetValue(batch.TRANBPID, out var srcBatch))
                {
                    actualQty = srcBatch.TRANPTQTY;
                }

                detailRows.Add(new DebitNoteDetailRow
                {
                    IsSelected = true,
                    MaterialId = d.TRANDREFID,
                    MaterialName = d.TRANDREFNAME,
                    Qty = d.TRANDQTY,
                    Rate = d.TRANDRATE,
                    Amount = d.TRANDGAMT,
                    HsnCode = hsnCode,
                    BatchNo = batch != null ? batch.TRANBDNO : null,
                    ExpiryDate = batch != null ? (DateTime?)batch.TRANBEXPDATE : null,
                    PackingId = batch != null ? batch.PACKMID : 0,
                    Ptr = batch != null ? batch.TRANBPTRRATE : 0m,
                    Mrp = batch != null ? batch.TRANBMRP : 0m,
                    BoxQty = batch != null ? batch.TRANBQTY : 0m,
                    Packing = packingDesc,
                    BillNo = d.TRANDREFNO,
                    SourceBatchId = batch != null ? batch.TRANBPID : 0,
                    SourceDetailId = batch != null ? batch.TRANDPID : 0,
                    SourceRefId = batch != null && batch.TRANBLMID.HasValue ? batch.TRANBLMID.Value : 0,
                    ActualQty = actualQty
                });
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
            ViewBag.DetailRowsJson = detailRows.Any() ? JsonConvert.SerializeObject(detailRows) : "[]";

            var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == model.TRANREFID);
            LocationMaster location = null;
            StateMaster state = null;
            if (supplier != null)
            {
                location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == supplier.LOCTID);
                state = db.StateMasters.FirstOrDefault(st => st.STATEID == supplier.STATEID);
            }

            ViewBag.SupplierAddr1 = supplier != null ? supplier.CATEADDR1 : string.Empty;
            ViewBag.SupplierCity = location != null ? location.LOCTDESC : string.Empty;
            ViewBag.SupplierState = state != null ? state.STATEDESC : string.Empty;
            ViewBag.SupplierPincode = supplier != null ? supplier.CATEADDR5 : string.Empty;

            var billNoList = db.TransactionMasters
                .Where(t => t.REGSTRID == PurchaseInvoiceRegisterId
                            && t.TRANREFID > 0
                            && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)
                            && !string.IsNullOrEmpty(t.TRANREFNO)
                            && t.TRANREFNO != "-")
                .Select(t => new
                {
                    SupplierId = t.TRANREFID,
                    BillNo = t.TRANREFNO
                })
                .ToList();

            ViewBag.BillNoListJson = JsonConvert.SerializeObject(billNoList);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteEdit")]
        public ActionResult DebitNoteSave(TransactionMaster master, string detailRowsJson)
        {
            try
            {
                bool isEdit = master.TRANMID > 0 &&
                              db.TransactionMasters.Any(t => t.TRANMID == master.TRANMID && t.REGSTRID == PurchaseReturnRegisterId);

                if (!isEdit)
                {
                    TempData["ErrorMessage"] = "Create is not allowed here.";
                    return RedirectToAction("DebitNoteIndex");
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<DebitNoteDetailRow>()
                    : JsonConvert.DeserializeObject<List<DebitNoteDetailRow>>(detailRowsJson) ?? new List<DebitNoteDetailRow>();

                details = details
                    .Where(d => d != null && d.IsSelected && d.MaterialId > 0 && d.Qty > 0)
                    .ToList();

                var invalidQtyRow = details.FirstOrDefault(d => d.ActualQty > 0 && d.Qty > d.ActualQty);
                if (invalidQtyRow != null)
                {
                    TempData["ErrorMessage"] = "Qty cannot be greater than Chellan Qty.";
                    return RedirectToAction("DebitNoteForm", new { id = (int?)master.TRANMID });
                }

                if (!details.Any())
                {
                    TempData["ErrorMessage"] = "Please add at least one detail row.";
                    return RedirectToAction("DebitNoteForm", new { id = (int?)master.TRANMID });
                }

                var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANMID && t.REGSTRID == PurchaseReturnRegisterId);
                if (existing == null)
                {
                    TempData["ErrorMessage"] = "Debit Note not found.";
                    return RedirectToAction("DebitNoteIndex");
                }

                var supplier = db.SupplierMasters.FirstOrDefault(c => c.CATEID == master.TRANREFID);
                short tranStateType = 0;
                if (supplier != null)
                {
                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);
                    if (state != null)
                    {
                        tranStateType = state.STATETYPE;
                    }
                    existing.TRANREFID = supplier.CATEID;
                    existing.TRANREFNAME = supplier.CATENAME;
                }
                else
                {
                    existing.TRANREFID = master.TRANREFID;
                    existing.TRANREFNAME = master.TRANREFNAME;
                }

                existing.TRANDATE = master.TRANDATE;
                existing.TRANREFNO = string.IsNullOrWhiteSpace(master.TRANREFNO) ? "-" : master.TRANREFNO;
                existing.TRANNARTN = master.TRANNARTN;
                existing.TRANRMKS = master.TRANRMKS;
                existing.DISPSTATUS = master.DISPSTATUS;
                existing.TRANSTATETYPE = tranStateType;

                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                existing.LMUSRID = userName;
                existing.PRCSDATE = DateTime.Now;
                existing.TRANTIME = DateTime.Now;

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

                InsertDebitNoteDetails(existing, details);
                TempData["SuccessMessage"] = "Debit Note saved successfully.";
                return RedirectToAction("DebitNoteIndex");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("DebitNoteForm", new { id = (int?)master.TRANMID });
            }
        }

        [HttpPost]
        public JsonResult DebitNoteDel(int id)
        {
            try
            {
                if (!User.IsInRole("ChellanToDebitNoteDebitNoteDelete"))
                {
                    return Json("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }

                var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == PurchaseReturnRegisterId);
                if (existing == null)
                {
                    return Json("Record not found");
                }

                var detailIds = db.TransactionDetails
                    .Where(d => d.TRANMID == existing.TRANMID)
                    .Select(d => d.TRANDID)
                    .ToList();

                if (detailIds.Any())
                {
                    db.Database.ExecuteSqlCommand(
                        $"DELETE FROM TRANSACTIONBATCHDETAIL WHERE TRANDID IN ({string.Join(",", detailIds)})");

                    var details = db.TransactionDetails
                        .Where(d => d.TRANMID == existing.TRANMID)
                        .ToList();

                    if (details.Any())
                    {
                        db.TransactionDetails.RemoveRange(details);
                    }
                }

                db.TransactionMasters.Remove(existing);
                db.SaveChanges();

                return Json("Successfully deleted");
            }
            catch (Exception ex)
            {
                return Json("Error: " + ex.Message);
            }
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteEdit")]
        public JsonResult DebitNoteGetMaterialsBySupplier(int tranRefId)
        {
            try
            {
                var results = new List<Dictionary<string, object>>();

                var connection = db.Database.Connection;
                try
                {
                    if (connection.State != ConnectionState.Open)
                    {
                        connection.Open();
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "PR_GET_MATERIALDETAILS_CRDBN";
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.Add(new SqlParameter("@PTRANREFID", tranRefId));

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var name = reader.GetName(i);
                                    object value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                    row[name] = value;
                                }
                                results.Add(row);
                            }
                        }
                    }
                }
                finally
                {
                    if (connection.State == ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }

                var materials = results.Select(r =>
                {
                    object tmp;

                    int id = 0;
                    try
                    {
                        if (r.TryGetValue("MTRLID", out tmp) && tmp != null)
                        {
                            id = Convert.ToInt32(tmp);
                        }
                        else if (r.TryGetValue("TRANDREFID", out tmp) && tmp != null)
                        {
                            id = Convert.ToInt32(tmp);
                        }
                    }
                    catch
                    {
                        id = 0;
                    }

                    string name = null;
                    if (r.TryGetValue("MTRLDESC", out tmp) && tmp != null)
                    {
                        name = tmp.ToString();
                    }
                    else if (r.TryGetValue("TRANDREFNAME", out tmp) && tmp != null)
                    {
                        name = tmp.ToString();
                    }

                    decimal rate = 0m;
                    try
                    {
                        if (r.TryGetValue("RATE", out tmp) && tmp != null)
                        {
                            rate = Convert.ToDecimal(tmp);
                        }
                        else if (r.TryGetValue("TRANDRATE", out tmp) && tmp != null)
                        {
                            rate = Convert.ToDecimal(tmp);
                        }
                    }
                    catch
                    {
                        rate = 0m;
                    }

                    string hsnCode = null;
                    if (r.TryGetValue("HSNCODE", out tmp) && tmp != null)
                    {
                        hsnCode = tmp.ToString();
                    }

                    return new
                    {
                        id = id,
                        name = name ?? string.Empty,
                        rate = rate,
                        hsnCode = hsnCode ?? string.Empty
                    };
                }).ToList();

                return Json(new { success = true, materials }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteEdit")]
        public JsonResult DebitNoteGetBillNosBySupplierAndMaterial(int supplierId, int materialId)
        {
            try
            {
                if (supplierId <= 0 || materialId <= 0)
                {
                    return Json(new { success = false, billNos = new string[0] }, JsonRequestBehavior.AllowGet);
                }

                var tranMids = db.TransactionDetails
                    .Where(d => d.TRANDREFID == materialId)
                    .Select(d => d.TRANMID)
                    .Distinct()
                    .ToList();

                if (!tranMids.Any())
                {
                    return Json(new { success = true, billNos = new string[0] }, JsonRequestBehavior.AllowGet);
                }

                var billNos = db.TransactionMasters
                    .Where(t => t.REGSTRID == PurchaseInvoiceRegisterId
                                && t.TRANREFID == supplierId
                                && tranMids.Contains(t.TRANMID)
                                && !string.IsNullOrEmpty(t.TRANREFNO)
                                && t.TRANREFNO != "-")
                    .Select(t => t.TRANREFNO)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                return Json(new { success = true, billNos = billNos }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteEdit")]
        public JsonResult DebitNoteGetPurchaseInvoiceDetail(int supplierId, string billNo, int materialId)
        {
            try
            {
                if (supplierId <= 0 || string.IsNullOrWhiteSpace(billNo) || materialId <= 0)
                {
                    return Json(new { success = false, message = "Invalid input." }, JsonRequestBehavior.AllowGet);
                }

                var tranMid = db.TransactionMasters
                    .Where(t => t.REGSTRID == PurchaseInvoiceRegisterId
                                && t.TRANREFID == supplierId
                                && t.TRANREFNO == billNo)
                    .Select(t => (int?)t.TRANMID)
                    .FirstOrDefault();

                if (!tranMid.HasValue)
                {
                    return Json(new { success = false, message = "Purchase invoice not found for the selected supplier and bill number." }, JsonRequestBehavior.AllowGet);
                }

                var results = new List<Dictionary<string, object>>();

                var connection = db.Database.Connection;
                try
                {
                    if (connection.State != ConnectionState.Open)
                    {
                        connection.Open();
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "PR_GET_PURCHASEINV_DET_CRDBN";
                        command.CommandType = CommandType.StoredProcedure;

                        command.Parameters.Add(new SqlParameter("@PTRANMID", tranMid.Value));
                        command.Parameters.Add(new SqlParameter("@PTRANDREFID", materialId));

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var name = reader.GetName(i);
                                    object value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                    row[name] = value;
                                }
                                results.Add(row);
                            }
                        }
                    }
                }
                finally
                {
                    if (connection.State == ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }

                if (!results.Any())
                {
                    return Json(new { success = false, message = "No invoice detail found for the selected bill and material." }, JsonRequestBehavior.AllowGet);
                }

                return Json(new { success = true, data = results.First() }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [Authorize(Roles = "ChellanToDebitNoteDebitNotePrint")]
        public ActionResult DebitNotePrint(int id)
        {
            try
            {
                var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == PurchaseReturnRegisterId);
                if (master == null)
                {
                    TempData["ErrorMessage"] = "Debit Note not found.";
                    return RedirectToAction("DebitNoteIndex");
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

                var materialIds = details
                    .Select(d => d.TRANDREFID)
                    .Distinct()
                    .ToList();

                var materials = db.MaterialMasters
                    .Where(m => materialIds.Contains(m.MTRLID))
                    .ToDictionary(m => m.MTRLID, m => m);

                Dictionary<int, MaterialGroupMaster> groupMap = null;
                if (materials.Count > 0)
                {
                    var groupIds = materials.Values
                        .Select(m => m.MTRLGID)
                        .Distinct()
                        .ToList();

                    if (groupIds.Count > 0)
                    {
                        groupMap = db.MaterialGroupMasters
                            .Where(g => groupIds.Contains(g.MTRLGID))
                            .ToDictionary(g => g.MTRLGID, g => g);
                    }
                }

                var hsnIds = materials.Values
                    .Where(m => m.HSNID > 0)
                    .Select(m => m.HSNID)
                    .Distinct()
                    .ToList();

                var hsnMap = db.HSNCodeMasters
                    .Where(h => hsnIds.Contains(h.HSNID))
                    .ToDictionary(h => h.HSNID, h => h);

                List<TransactionBatchDetail> batchInfos = null;
                Dictionary<int, PackingMaster> packMap = null;

                if (details.Count > 0)
                {
                    var detIds = details.Select(d => d.TRANDID).ToList();

                    batchInfos = db.TransactionBatchDetails
                        .Where(b => detIds.Contains(b.TRANDID))
                        .ToList();

                    if (batchInfos.Count > 0)
                    {
                        var packIds = batchInfos
                            .Select(b => b.PACKMID)
                            .Distinct()
                            .ToList();

                        if (packIds.Count > 0)
                        {
                            packMap = db.PackingMasters
                                .Where(p => packIds.Contains(p.PACKMID))
                                .ToDictionary(p => p.PACKMID, p => p);
                        }
                    }
                }

                var items = new List<DebitNotePrintItemViewModel>();

                for (int i = 0; i < details.Count; i++)
                {
                    var d = details[i];

                    materials.TryGetValue(d.TRANDREFID, out var material);
                    HSNCodeMaster hsn = null;
                    MaterialGroupMaster group = null;

                    if (material != null)
                    {
                        if (material.HSNID > 0)
                        {
                            hsnMap.TryGetValue(material.HSNID, out hsn);
                        }

                        if (groupMap != null)
                        {
                            groupMap.TryGetValue(material.MTRLGID, out group);
                        }
                    }

                    TransactionBatchDetail batch = null;
                    if (batchInfos != null && batchInfos.Count > 0)
                    {
                        batch = batchInfos.FirstOrDefault(b => b.TRANDID == d.TRANDID);
                    }

                    PackingMaster pack = null;
                    if (batch != null && packMap != null)
                    {
                        packMap.TryGetValue(batch.PACKMID, out pack);
                    }

                    decimal rate = d.TRANDRATE;
                    decimal qty = d.TRANDQTY;
                    decimal gross = d.TRANDGAMT > 0 ? d.TRANDGAMT : (qty * rate);
                    decimal net = d.TRANDNAMT > 0 ? d.TRANDNAMT : (gross + d.TRANDCGSTAMT + d.TRANDSGSTAMT + d.TRANDIGSTAMT);

                    decimal ptr = batch != null ? batch.TRANBPTRRATE : 0m;
                    decimal mrp = batch != null ? batch.TRANBMRP : 0m;
                    decimal dis1 = ptr > 0 ? ptr - rate : 0m;

                    decimal cgstRate = 0m;
                    decimal sgstRate = 0m;
                    if (hsn != null)
                    {
                        cgstRate = hsn.CGSTEXPRN;
                        sgstRate = hsn.SGSTEXPRN;
                    }

                    items.Add(new DebitNotePrintItemViewModel
                    {
                        Division = group != null ? group.MTRLGDESC : string.Empty,
                        MaterialName = d.TRANDREFNAME,
                        Pack = pack != null ? pack.PACKMDESC : string.Empty,
                        Qty = qty,
                        BatchNo = batch != null ? batch.TRANBDNO : string.Empty,
                        ExpiryDate = batch != null ? (DateTime?)batch.TRANBEXPDATE : null,
                        HSNCode = hsn != null ? hsn.HSNCODE : string.Empty,
                        Rate = rate,
                        Ptr = ptr,
                        Mrp = mrp,
                        Dis1 = dis1,
                        DisPercent = 0m,
                        DiscountAmount = 0m,
                        SgstRate = sgstRate,
                        CgstRate = cgstRate,
                        Amount = gross,
                        NetAmount = net
                    });
                }

                var classSummaryDict = new Dictionary<decimal, DebitNoteClassSummaryViewModel>();

                for (int i = 0; i < items.Count && i < details.Count; i++)
                {
                    var item = items[i];
                    var d = details[i];

                    decimal gstPercent = item.SgstRate + item.CgstRate;

                    if (!classSummaryDict.TryGetValue(gstPercent, out var summary))
                    {
                        summary = new DebitNoteClassSummaryViewModel
                        {
                            ClassName = $"GST {gstPercent.ToString("0.00")}%",
                            GstPercent = gstPercent,
                            Scheme = 0m,
                            Discount = 0m
                        };
                        classSummaryDict[gstPercent] = summary;
                    }

                    summary.Total += item.Amount;
                    summary.Sgst += d.TRANDSGSTAMT;
                    summary.Cgst += d.TRANDCGSTAMT;
                    summary.TotalGst = summary.Sgst + summary.Cgst;
                }

                var classSummaries = classSummaryDict.Values
                    .OrderBy(c => c.GstPercent)
                    .ToList();

                int totalItems = items.Count;
                decimal totalQty = items.Sum(x => x.Qty);

                decimal totalDisc = 0m;
                decimal courierCharges = 0m;

                var company = db.companymasters.FirstOrDefault(c => c.COMPID == master.COMPYID);
                if (company == null)
                {
                    company = db.companymasters.FirstOrDefault();
                }

                string companyAddress = string.Empty;
                string companyName = string.Empty;
                string companyGstNo = string.Empty;

                if (company != null)
                {
                    if (!string.IsNullOrWhiteSpace(company.COMPADDR))
                    {
                        companyAddress = company.COMPADDR;
                    }

                    if (!string.IsNullOrWhiteSpace(company.COMPNAME))
                    {
                        companyName = company.COMPNAME;
                    }

                    if (!string.IsNullOrWhiteSpace(company.COMPGSTNO))
                    {
                        companyGstNo = company.COMPGSTNO;
                    }
                }

                var model = new DebitNotePrintViewModel
                {
                    TRANMID = master.TRANMID,
                    TRANNO = master.TRANNO,
                    TRANDNO = master.TRANDNO,
                    TRANREFNO = master.TRANREFNO,
                    TRANDATE = master.TRANDATE,
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
                    CompanyAddress = companyAddress,
                    CompanyName = companyName,
                    CompanyGstNo = companyGstNo,
                    GrossAmount = master.TRANGAMT,
                    NetAmount = master.TRANNAMT,
                    AmountInWords = master.TRANAMTWRDS,
                    TotalItems = totalItems,
                    TotalQty = totalQty,
                    CgstAmount = master.TRANCGSTAMT,
                    SgstAmount = master.TRANSGSTAMT,
                    IgstAmount = master.TRANIGSTAMT,
                    TotalDisc = totalDisc,
                    CourierCharges = courierCharges,
                    Narration = master.TRANNARTN,
                    Remarks = master.TRANRMKS,
                    UserName = master.LMUSRID,
                    BillingTime = master.TRANTIME,
                    Items = items,
                    ClassSummaries = classSummaries
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading Debit Note: " + ex.Message;
                return RedirectToAction("DebitNoteIndex");
            }
        }

        private void InsertDebitNoteDetails(TransactionMaster master, List<DebitNoteDetailRow> details)
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
                        d.SourceBatchId,
                        d.SourceDetailId,
                        totalQtyInt,
                        d.SourceRefId
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

                if (User != null && User.IsInRole("ChellanToDebitNoteDebitNoteIndex"))
                {
                    return RedirectToAction("DebitNoteIndex", "ChellanToDebitNote");
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
