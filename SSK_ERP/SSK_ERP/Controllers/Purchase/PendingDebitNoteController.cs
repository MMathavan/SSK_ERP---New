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

namespace SSK_ERP.Controllers.Purchase
{
    [SessionExpire]
    public class PendingDebitNoteController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int ChellanRegisterId = 27;
        private const int PurchaseReturnRegisterId = 22;

        private class PendingDebitNoteDetailRow
        {
            public bool IsSelected { get; set; }
            public int MaterialId { get; set; }
            public string MaterialName { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public string HsnCode { get; set; }
            public string BillNo { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int PackingId { get; set; }
            public string Packing { get; set; }
            public decimal BoxQty { get; set; }
            public int SourceBatchId { get; set; }
            public int SourceDetailId { get; set; }
            public int SourceRefId { get; set; }
            public decimal ActualQty { get; set; }
        }

        public class PendingDebitNoteFormViewModel
        {
            public int DebitNoteId { get; set; }
            public string AutoNo { get; set; }
            public int SupplierId { get; set; }
            public string SupplierName { get; set; }
            public string SupplierAddr1 { get; set; }
            public string SupplierAddr2 { get; set; }
            public string SupplierAddr3 { get; set; }
            public string SupplierAddr4 { get; set; }
            public string SupplierPincode { get; set; }
            public string SupplierLocation { get; set; }
            public string SupplierState { get; set; }
            public string SupplierCountry { get; set; }
            public int ChellanId { get; set; }
            public string ChellanNo { get; set; }
            public DateTime DebitNoteDate { get; set; }
            public short Status { get; set; }
            public string BillNo { get; set; }
            public string Narration { get; set; }
            public string Remarks { get; set; }
            public short TranStateType { get; set; }
            public string DetailRowsJson { get; set; }
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteIndex")]
        public ActionResult Form(int? id)
        {
            var vm = new PendingDebitNoteFormViewModel
            {
                DebitNoteId = 0,
                AutoNo = string.Empty,
                SupplierId = 0,
                SupplierName = string.Empty,
                SupplierAddr1 = string.Empty,
                SupplierAddr2 = string.Empty,
                SupplierAddr3 = string.Empty,
                SupplierAddr4 = string.Empty,
                SupplierPincode = string.Empty,
                SupplierLocation = string.Empty,
                SupplierState = string.Empty,
                SupplierCountry = string.Empty,
                ChellanId = 0,
                DebitNoteDate = DateTime.Today,
                Status = 0,
                BillNo = string.Empty,
                Narration = string.Empty,
                Remarks = string.Empty,
                TranStateType = 0,
                DetailRowsJson = "[]"
            };

            if (id.HasValue && id.Value > 0)
            {
                var dn = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == PurchaseReturnRegisterId && t.TRANBTYPE == 2);
                if (dn == null)
                {
                    TempData["ErrorMessage"] = "Debit Note not found.";
                    return RedirectToAction("DebitNoteIndex", "ChellanToDebitNote");
                }

                vm.DebitNoteId = dn.TRANMID;
                vm.AutoNo = dn.TRANDNO;
                vm.SupplierId = dn.TRANREFID;
                vm.ChellanId = (int)dn.TRANLMID;
                vm.DebitNoteDate = dn.TRANDATE;
                vm.Status = (short)dn.DISPSTATUS;
                vm.BillNo = dn.TRANREFNO;
                vm.Narration = dn.TRANNARTN;
                vm.Remarks = dn.TRANRMKS;
                vm.TranStateType = dn.TRANSTATETYPE;

                FillSupplierAddress(vm, vm.SupplierId);

                if (vm.ChellanId > 0)
                {
                    var chellan = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == vm.ChellanId && t.REGSTRID == ChellanRegisterId);
                    vm.ChellanNo = chellan != null ? chellan.TRANDNO : string.Empty;
                }

                var savedRows = LoadDebitNoteRows(dn);
                var pendingRows = vm.ChellanId > 0 ? LoadPendingRows(vm.ChellanId) : new List<PendingDebitNoteDetailRow>();

                var savedKey = new HashSet<int>(savedRows.Where(x => x.SourceDetailId > 0).Select(x => x.SourceDetailId));
                foreach (var pr in pendingRows)
                {
                    if (!savedKey.Contains(pr.SourceDetailId))
                    {
                        pr.IsSelected = false;
                        savedRows.Add(pr);
                    }
                }

                vm.DetailRowsJson = JsonConvert.SerializeObject(savedRows);
            }
            else
            {
                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseReturnRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;
                vm.AutoNo = FormatPurchaseReturnTrandNo(nextTranNo, vm.DebitNoteDate);

                if (vm.SupplierId > 0)
                {
                    FillSupplierAddress(vm, vm.SupplierId);
                }
            }

            FillSupplierViewBags(vm.SupplierId);
            ViewBag.ChellanListJson = "[]";

            return View(vm);
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteIndex")]
        public JsonResult GetSupplierAddress(int supplierId)
        {
            try
            {
                if (supplierId <= 0)
                {
                    return Json(new
                    {
                        success = true,
                        supplierName = string.Empty,
                        addr1 = string.Empty,
                        addr2 = string.Empty,
                        addr3 = string.Empty,
                        addr4 = string.Empty,
                        pincode = string.Empty,
                        location = string.Empty,
                        state = string.Empty,
                        country = string.Empty
                    }, JsonRequestBehavior.AllowGet);
                }

                var vm = new PendingDebitNoteFormViewModel();
                FillSupplierAddress(vm, supplierId);
                return Json(new
                {
                    success = true,
                    supplierName = vm.SupplierName,
                    addr1 = vm.SupplierAddr1,
                    addr2 = vm.SupplierAddr2,
                    addr3 = vm.SupplierAddr3,
                    addr4 = vm.SupplierAddr4,
                    pincode = vm.SupplierPincode,
                    location = vm.SupplierLocation,
                    state = vm.SupplierState,
                    country = vm.SupplierCountry
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteEdit")]
        public ActionResult Save(PendingDebitNoteFormViewModel model)
        {
            try
            {
                if (model == null || model.SupplierId <= 0 || model.ChellanId <= 0)
                {
                    TempData["ErrorMessage"] = "Invalid request.";
                    return RedirectToAction("Form");
                }

                var chellan = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == model.ChellanId && t.REGSTRID == ChellanRegisterId);
                if (chellan == null)
                {
                    TempData["ErrorMessage"] = "Chellan not found.";
                    return RedirectToAction("Form");
                }

                var rows = string.IsNullOrWhiteSpace(model.DetailRowsJson)
                    ? new List<PendingDebitNoteDetailRow>()
                    : (JsonConvert.DeserializeObject<List<PendingDebitNoteDetailRow>>(model.DetailRowsJson) ?? new List<PendingDebitNoteDetailRow>());

                rows = rows.Where(r => r != null && r.IsSelected && r.MaterialId > 0 && r.Qty > 0).ToList();
                if (!rows.Any())
                {
                    TempData["ErrorMessage"] = "Please select at least one item row.";
                    return RedirectToAction("Form", new { id = model.DebitNoteId > 0 ? (int?)model.DebitNoteId : null });
                }

                var invalidQty = rows.FirstOrDefault(r => r.ActualQty > 0 && r.Qty > r.ActualQty);
                if (invalidQty != null)
                {
                    TempData["ErrorMessage"] = "Qty cannot be greater than pending qty.";
                    return RedirectToAction("Form", new { id = model.DebitNoteId > 0 ? (int?)model.DebitNoteId : null });
                }

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                TransactionMaster dnMaster;
                bool isEdit = model.DebitNoteId > 0;
                if (isEdit)
                {
                    dnMaster = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == model.DebitNoteId && t.REGSTRID == PurchaseReturnRegisterId && t.TRANBTYPE == 2);
                    if (dnMaster == null)
                    {
                        TempData["ErrorMessage"] = "Debit Note not found.";
                        return RedirectToAction("DebitNoteIndex", "ChellanToDebitNote");
                    }

                    dnMaster.TRANDATE = model.DebitNoteDate;
                    dnMaster.TRANREFID = model.SupplierId;
                    dnMaster.TRANREFNAME = chellan.TRANREFNAME;
                    dnMaster.TRANREFNO = string.IsNullOrWhiteSpace(model.BillNo) ? "-" : model.BillNo;
                    dnMaster.TRANNARTN = model.Narration;
                    dnMaster.TRANRMKS = model.Remarks;
                    dnMaster.DISPSTATUS = model.Status;
                    dnMaster.LMUSRID = userName;
                    dnMaster.PRCSDATE = DateTime.Now;
                    dnMaster.TRANTIME = DateTime.Now;

                    var existingDetailIds = db.TransactionDetails
                        .Where(d => d.TRANMID == dnMaster.TRANMID)
                        .Select(d => d.TRANDID)
                        .ToList();

                    if (existingDetailIds.Any())
                    {
                        db.Database.ExecuteSqlCommand(
                            $"DELETE FROM TRANSACTIONBATCHDETAIL WHERE TRANDID IN ({string.Join(",", existingDetailIds)})");

                        var existingDetails = db.TransactionDetails
                            .Where(d => d.TRANMID == dnMaster.TRANMID)
                            .ToList();

                        if (existingDetails.Any())
                        {
                            db.TransactionDetails.RemoveRange(existingDetails);
                        }

                        db.SaveChanges();
                    }
                }
                else
                {
                    var maxTranNo = db.TransactionMasters
                        .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseReturnRegisterId)
                        .Select(t => (int?)t.TRANNO)
                        .Max();

                    int nextTranNo = (maxTranNo ?? 0) + 1;

                    dnMaster = new TransactionMaster
                    {
                        COMPYID = compyId,
                        SDPTID = 0,
                        REGSTRID = PurchaseReturnRegisterId,
                        TRANBTYPE = 2,
                        TRANNO = nextTranNo,
                        TRANDNO = FormatPurchaseReturnTrandNo(nextTranNo, model.DebitNoteDate),
                        TRANDATE = model.DebitNoteDate,
                        TRANTIME = DateTime.Now,
                        TRANREFID = model.SupplierId,
                        TRANREFNAME = chellan.TRANREFNAME,
                        TRANSTATETYPE = model.TranStateType,
                        TRANREFNO = string.IsNullOrWhiteSpace(model.BillNo) ? "-" : model.BillNo,
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
                }

                InsertDetails(dnMaster, rows);
                db.SaveChanges();

                TempData["SuccessMessage"] = "Debit Note saved successfully.";
                return RedirectToAction("DebitNoteIndex", "ChellanToDebitNote");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Form", new { id = model != null && model.DebitNoteId > 0 ? (int?)model.DebitNoteId : null });
            }
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteIndex")]
        public JsonResult GetChellansBySupplier(int supplierId)
        {
            try
            {
                if (supplierId <= 0)
                {
                    return Json(new { success = true, chellans = new object[0] }, JsonRequestBehavior.AllowGet);
                }

                var chellans = new List<object>();
                var connection = db.Database.Connection;
                try
                {
                    if (connection.State != ConnectionState.Open)
                    {
                        connection.Open();
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "PR_Get_Chellan_det";
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.Add(new SqlParameter("@Tranrefid", supplierId));

                        using (var reader = command.ExecuteReader())
                        {
                            int ordTranMid = -1;
                            int ordTrandNo = -1;
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var name = reader.GetName(i);
                                if (ordTranMid < 0 && name.Equals("TRANMID", StringComparison.OrdinalIgnoreCase)) ordTranMid = i;
                                if (ordTrandNo < 0 && name.Equals("TRANDNO", StringComparison.OrdinalIgnoreCase)) ordTrandNo = i;
                            }

                            while (reader.Read())
                            {
                                int id = 0;
                                string text = string.Empty;

                                if (ordTranMid >= 0 && !reader.IsDBNull(ordTranMid))
                                {
                                    id = Convert.ToInt32(reader.GetValue(ordTranMid));
                                }

                                if (ordTrandNo >= 0 && !reader.IsDBNull(ordTrandNo))
                                {
                                    text = Convert.ToString(reader.GetValue(ordTrandNo));
                                }

                                if (id > 0)
                                {
                                    chellans.Add(new { id, text });
                                }
                            }
                        }
                    }
                }
                catch
                {
                    chellans = new List<object>();
                }

                return Json(new { success = true, chellans }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "ChellanToDebitNoteDebitNoteIndex")]
        public JsonResult GetPendingRows(int chellanId)
        {
            try
            {
                var rows = chellanId > 0 ? LoadPendingRows(chellanId) : new List<PendingDebitNoteDetailRow>();
                return Json(new { success = true, rows }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private class ChellanDropdownSpRow
        {
            public int TRANMID { get; set; }
            public string TRANDNO { get; set; }
        }

        private void FillSupplierViewBags(int? selectedSupplierId)
        {
            var supplierList = db.SupplierMasters
                .Where(c => c.DISPSTATUS == 0)
                .OrderBy(c => c.CATENAME)
                .Select(c => new { c.CATEID, c.CATENAME })
                .ToList();

            ViewBag.SupplierList = new SelectList(supplierList, "CATEID", "CATENAME", selectedSupplierId);

            var billNoList = db.TransactionMasters
                .Where(t => t.REGSTRID == 18
                            && t.TRANREFID > 0
                            && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)
                            && !string.IsNullOrEmpty(t.TRANREFNO)
                            && t.TRANREFNO != "-")
                .Select(t => new { SupplierId = t.TRANREFID, BillNo = t.TRANREFNO })
                .ToList();

            ViewBag.BillNoListJson = JsonConvert.SerializeObject(billNoList);
        }

        private void FillSupplierAddress(PendingDebitNoteFormViewModel vm, int supplierId)
        {
            if (vm == null) return;

            vm.SupplierName = string.Empty;
            vm.SupplierAddr1 = string.Empty;
            vm.SupplierAddr2 = string.Empty;
            vm.SupplierAddr3 = string.Empty;
            vm.SupplierAddr4 = string.Empty;
            vm.SupplierPincode = string.Empty;
            vm.SupplierLocation = string.Empty;
            vm.SupplierState = string.Empty;
            vm.SupplierCountry = string.Empty;

            if (supplierId <= 0) return;

            var s = db.SupplierMasters.FirstOrDefault(x => x.CATEID == supplierId);
            if (s == null) return;

            vm.SupplierName = s.CATENAME;
            vm.SupplierAddr1 = s.CATEADDR1;
            vm.SupplierAddr2 = s.CATEADDR2;
            vm.SupplierAddr3 = s.CATEADDR3;
            vm.SupplierAddr4 = s.CATEADDR4;
            vm.SupplierPincode = s.CATEADDR5;

            if (s.LOCTID > 0)
            {
                vm.SupplierLocation = db.LocationMasters.Where(l => l.LOCTID == s.LOCTID).Select(l => l.LOCTDESC).FirstOrDefault();
            }
            if (s.STATEID > 0)
            {
                vm.SupplierState = db.StateMasters.Where(st => st.STATEID == s.STATEID).Select(st => st.STATEDESC).FirstOrDefault();
            }

            vm.SupplierCountry = "India";
        }

        private List<PendingDebitNoteDetailRow> LoadDebitNoteRows(TransactionMaster debitNote)
        {
            var detailRows = new List<PendingDebitNoteDetailRow>();

            var details = db.TransactionDetails.Where(d => d.TRANMID == debitNote.TRANMID).ToList();
            if (!details.Any()) return detailRows;

            var detailIds = details.Select(d => d.TRANDID).ToList();
            var batchDetails = db.TransactionBatchDetails.Where(b => detailIds.Contains(b.TRANDID)).ToList();

            var materialIds = details.Select(d => d.TRANDREFID).Distinct().ToList();
            var materials = db.MaterialMasters.Where(m => materialIds.Contains(m.MTRLID)).ToDictionary(m => m.MTRLID, m => m);

            var hsnIds = materials.Values.Where(m => m.HSNID > 0).Select(m => m.HSNID).Distinct().ToList();
            var hsnMap = db.HSNCodeMasters.Where(h => hsnIds.Contains(h.HSNID)).ToDictionary(h => h.HSNID, h => h);

            var packingIds = batchDetails.Select(b => b.PACKMID).Distinct().ToList();
            var packingMap = db.PackingMasters.Where(p => packingIds.Contains(p.PACKMID)).ToDictionary(p => p.PACKMID, p => p.PACKMDESC);

            var sourceBatchIds = batchDetails.Where(b => b.TRANBPID > 0).Select(b => b.TRANBPID).Distinct().ToList();
            var sourceBatchMap = db.TransactionBatchDetails
                .Where(b => sourceBatchIds.Contains(b.TRANBID))
                .ToDictionary(b => b.TRANBID, b => b);

            foreach (var d in details)
            {
                var batch = batchDetails.FirstOrDefault(b => b.TRANDID == d.TRANDID);
                string packingDesc = string.Empty;
                if (batch != null && packingMap.TryGetValue(batch.PACKMID, out var pdesc))
                {
                    packingDesc = pdesc;
                }

                materials.TryGetValue(d.TRANDREFID, out var material);
                string hsnCode = string.Empty;
                if (material != null && material.HSNID > 0 && hsnMap.TryGetValue(material.HSNID, out var hsn))
                {
                    hsnCode = hsn.HSNCODE;
                }

                decimal actualQty = d.TRANDQTY;
                if (batch != null && batch.TRANBPID > 0 && sourceBatchMap.TryGetValue(batch.TRANBPID, out var srcBatch))
                {
                    actualQty = srcBatch.TRANPTQTY;
                }

                detailRows.Add(new PendingDebitNoteDetailRow
                {
                    IsSelected = true,
                    MaterialId = d.TRANDREFID,
                    MaterialName = d.TRANDREFNAME,
                    Qty = d.TRANDQTY,
                    Rate = d.TRANDRATE,
                    Amount = d.TRANDGAMT,
                    HsnCode = hsnCode,
                    BillNo = d.TRANDREFNO,
                    BatchNo = batch != null ? batch.TRANBDNO : null,
                    ExpiryDate = batch != null ? (DateTime?)batch.TRANBEXPDATE : null,
                    PackingId = batch != null ? batch.PACKMID : 0,
                    Packing = packingDesc,
                    BoxQty = batch != null ? batch.TRANBQTY : 0m,
                    SourceBatchId = batch != null ? batch.TRANBPID : 0,
                    SourceDetailId = batch != null ? batch.TRANDPID : 0,
                    SourceRefId = batch != null && batch.TRANBLMID.HasValue ? batch.TRANBLMID.Value : 0,
                    ActualQty = actualQty
                });
            }

            return detailRows;
        }

        private List<PendingDebitNoteDetailRow> LoadPendingRows(int chellanId)
        {
            var chellan = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == chellanId && t.REGSTRID == ChellanRegisterId);
            if (chellan == null) return new List<PendingDebitNoteDetailRow>();

            var chellanDetails = db.TransactionDetails
                .Where(d => d.TRANMID == chellan.TRANMID)
                .OrderBy(d => d.TRANDID)
                .ToList();

            if (!chellanDetails.Any()) return new List<PendingDebitNoteDetailRow>();

            var chellanDetailIds = chellanDetails.Select(d => d.TRANDID).ToList();
            var chellanBatches = db.TransactionBatchDetails
                .Where(b => chellanDetailIds.Contains(b.TRANDID))
                .ToList();

            var chellanBatchIds = chellanBatches.Select(b => b.TRANBID).Distinct().ToList();

            var returnBatchLinks = db.TransactionBatchDetails
                .Where(b => b.TRANBPID > 0 && chellanBatchIds.Contains(b.TRANBPID))
                .ToList();

            var returnDetailIds = returnBatchLinks.Select(b => b.TRANDID).Distinct().ToList();
            var returnDetailQtyMap = db.TransactionDetails
                .Where(d => returnDetailIds.Contains(d.TRANDID))
                .ToList()
                .ToDictionary(d => d.TRANDID, d => d.TRANDQTY);

            var returnedQtyBySourceBatch = new Dictionary<int, decimal>();
            foreach (var link in returnBatchLinks)
            {
                if (link.TRANBPID <= 0) continue;
                if (!returnDetailQtyMap.TryGetValue(link.TRANDID, out var q)) q = 0m;
                if (!returnedQtyBySourceBatch.ContainsKey(link.TRANBPID)) returnedQtyBySourceBatch[link.TRANBPID] = 0m;
                returnedQtyBySourceBatch[link.TRANBPID] += q;
            }

            var materialIds = chellanDetails.Select(d => d.TRANDREFID).Distinct().ToList();
            var materials = db.MaterialMasters.Where(m => materialIds.Contains(m.MTRLID)).ToDictionary(m => m.MTRLID, m => m);
            var hsnIds = materials.Values.Where(m => m.HSNID > 0).Select(m => m.HSNID).Distinct().ToList();
            var hsnMap = db.HSNCodeMasters.Where(h => hsnIds.Contains(h.HSNID)).ToDictionary(h => h.HSNID, h => h);

            var packingIds = chellanBatches.Select(b => b.PACKMID).Distinct().ToList();
            var packingMap = db.PackingMasters.Where(p => packingIds.Contains(p.PACKMID)).ToDictionary(p => p.PACKMID, p => p.PACKMDESC);

            var rows = new List<PendingDebitNoteDetailRow>();
            foreach (var cd in chellanDetails)
            {
                var cbatch = chellanBatches.FirstOrDefault(b => b.TRANDID == cd.TRANDID);
                int sourceBatchId = cbatch != null ? cbatch.TRANBID : 0;

                decimal returnedQty = 0m;
                if (sourceBatchId > 0 && returnedQtyBySourceBatch.TryGetValue(sourceBatchId, out var rq))
                {
                    returnedQty = rq;
                }

                decimal remainingQty = cd.TRANDQTY - returnedQty;
                if (remainingQty <= 0m) continue;

                materials.TryGetValue(cd.TRANDREFID, out var material);
                string hsnCode = string.Empty;
                if (material != null && material.HSNID > 0 && hsnMap.TryGetValue(material.HSNID, out var hsn))
                {
                    hsnCode = hsn.HSNCODE;
                }

                string packingDesc = string.Empty;
                if (cbatch != null && packingMap.TryGetValue(cbatch.PACKMID, out var pdesc))
                {
                    packingDesc = pdesc;
                }

                rows.Add(new PendingDebitNoteDetailRow
                {
                    IsSelected = true,
                    MaterialId = cd.TRANDREFID,
                    MaterialName = cd.TRANDREFNAME,
                    Qty = remainingQty,
                    Rate = cd.TRANDRATE,
                    Amount = remainingQty * cd.TRANDRATE,
                    HsnCode = hsnCode,
                    BillNo = cd.TRANDREFNO,
                    BatchNo = cbatch != null ? cbatch.TRANBDNO : null,
                    ExpiryDate = cbatch != null ? (DateTime?)cbatch.TRANBEXPDATE : null,
                    PackingId = cbatch != null ? cbatch.PACKMID : 0,
                    Packing = packingDesc,
                    BoxQty = cbatch != null ? cbatch.TRANBQTY : 0m,
                    SourceBatchId = sourceBatchId,
                    SourceDetailId = cd.TRANDID,
                    SourceRefId = chellan.TRANMID,
                    ActualQty = remainingQty
                });
            }

            return rows;
        }

        private void InsertDetails(TransactionMaster master, List<PendingDebitNoteDetailRow> details)
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
                    TRANDAID = d.SourceDetailId,
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
            master.TRANAMTWRDS = ConvertAmountToWords(totalNet);
        }

        private string FormatPurchaseReturnTrandNo(int tranNo, DateTime tranDate)
        {
            int fyStartYear = tranDate.Month >= 4 ? tranDate.Year : tranDate.Year - 1;
            int fyEndYear = fyStartYear + 1;
            string fyPrefix = (fyStartYear % 100).ToString("00") + "-" + (fyEndYear % 100).ToString("00");

            string seqText = tranNo.ToString("0000");
            return fyPrefix + "/DN" + seqText;
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
    }
}
