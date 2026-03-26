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
    public class ChellanController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int ChellanRegisterId = 27;
        private const int PurchaseInvoiceRegisterId = 18;

        [Authorize(Roles = "ChellanIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "ChellanIndex")]
        public JsonResult GetAjaxData(string fromDate = null, string toDate = null)
        {
            try
            {
                var query = db.TransactionMasters.Where(t => t.REGSTRID == ChellanRegisterId);

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

        [Authorize(Roles = "ChellanCreate,ChellanEdit")]
        public ActionResult Form(int? id, int? tranBType)
        {
            TransactionMaster model;
            var detailRows = new List<ChellanDetailRow>();

            if (id.HasValue && id.Value > 0)
            {
                if (!User.IsInRole("ChellanEdit"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to edit Chellan.";
                    return RedirectToAction("Index");
                }

                model = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == ChellanRegisterId);
                if (model == null)
                {
                    TempData["ErrorMessage"] = "Chellan not found.";
                    return RedirectToAction("Index");
                }

                var details = db.TransactionDetails.Where(d => d.TRANMID == model.TRANMID).ToList();

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

                    detailRows.Add(new ChellanDetailRow
                    {
                        MaterialId = d.TRANDREFID,
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
            }
            else
            {
                if (!User.IsInRole("ChellanCreate"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to create Chellan.";
                    return RedirectToAction("Index");
                }

                model = new TransactionMaster
                {
                    TRANDATE = DateTime.Today,
                    TRANTIME = DateTime.Now,
                    DISPSTATUS = 0,
                    TRANBTYPE = (short)(tranBType.HasValue ? tranBType.Value : 1)
                };

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == ChellanRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;
                model.TRANNO = nextTranNo;
                model.TRANDNO = FormatChellanTrandNo(nextTranNo, model.TRANDATE);
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

            ViewBag.DetailRowsJson = detailRows.Any()
                ? JsonConvert.SerializeObject(detailRows)
                : "[]";

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

        [HttpGet]
        public JsonResult GetMaterialsBySupplier(int tranRefId)
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
        public JsonResult GetBillNosBySupplierAndMaterial(int supplierId, int materialId)
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
        public JsonResult GetPurchaseInvoiceDetail(int supplierId, string billNo, int materialId)
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "ChellanCreate,ChellanEdit")]
        public ActionResult savedata(TransactionMaster master, string detailRowsJson)
        {
            try
            {
                bool isEdit = master.TRANMID > 0 &&
                              db.TransactionMasters.Any(t => t.TRANMID == master.TRANMID && t.REGSTRID == ChellanRegisterId);

                if (isEdit)
                {
                    if (!User.IsInRole("ChellanEdit"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to edit Chellan.";
                        return RedirectToAction("Index");
                    }
                }
                else
                {
                    if (!User.IsInRole("ChellanCreate"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to create Chellan.";
                        return RedirectToAction("Index");
                    }
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<ChellanDetailRow>()
                    : JsonConvert.DeserializeObject<List<ChellanDetailRow>>(detailRowsJson) ?? new List<ChellanDetailRow>();

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
                master.REGSTRID = ChellanRegisterId;
                if (master.TRANBTYPE != 0 && master.TRANBTYPE != 1)
                {
                    master.TRANBTYPE = 1;
                }
                master.EXPRTSTATUS = 0;
                master.TRANTIME = DateTime.Now;
                if (string.IsNullOrWhiteSpace(master.TRANREFNO))
                {
                    master.TRANREFNO = "-";
                }

                if (isEdit)
                {
                    var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANMID && t.REGSTRID == ChellanRegisterId);
                    if (existing == null)
                    {
                        TempData["ErrorMessage"] = "Chellan not found.";
                        return RedirectToAction("Index");
                    }

                    existing.TRANDATE = master.TRANDATE;
                    existing.TRANTIME = master.TRANTIME;
                    existing.TRANREFID = master.TRANREFID;
                    existing.TRANREFNAME = master.TRANREFNAME;
                    existing.TRANSTATETYPE = master.TRANSTATETYPE;
                    existing.TRANREFNO = master.TRANREFNO;
                    existing.TRANBTYPE = master.TRANBTYPE;
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
                    var maxTranNo = db.TransactionMasters
                        .Where(t => t.COMPYID == compyId && t.REGSTRID == ChellanRegisterId)
                        .Select(t => (int?)t.TRANNO)
                        .Max();

                    int nextTranNo = (maxTranNo ?? 0) + 1;
                    master.TRANNO = nextTranNo;
                    if (string.IsNullOrWhiteSpace(master.TRANDNO))
                    {
                        master.TRANDNO = FormatChellanTrandNo(nextTranNo, master.TRANDATE);
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

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public JsonResult Del(int id)
        {
            try
            {
                if (!User.IsInRole("ChellanDelete"))
                {
                    return Json("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }

                var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == ChellanRegisterId);
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

        [Authorize(Roles = "ChellanPrint")]
        public ActionResult Print(int id)
        {
            try
            {
                var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == ChellanRegisterId);
                if (master == null)
                {
                    TempData["ErrorMessage"] = "Chellan not found.";
                    return RedirectToAction("Index");
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
                    var detailIds = details.Select(d => d.TRANDID).ToList();

                    batchInfos = db.TransactionBatchDetails
                        .Where(b => detailIds.Contains(b.TRANDID))
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

                var items = new List<SSK_ERP.Controllers.Purchase.PurchaseReturnController.PurchaseReturnPrintItemViewModel>();

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
                    PackingMaster pack = null;

                    if (batchInfos != null && batchInfos.Count > 0)
                    {
                        batch = batchInfos.FirstOrDefault(b => b.TRANDID == d.TRANDID);
                    }

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

                    items.Add(new SSK_ERP.Controllers.Purchase.PurchaseReturnController.PurchaseReturnPrintItemViewModel
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

                var classSummaryDict = new Dictionary<decimal, SSK_ERP.Controllers.Purchase.PurchaseReturnController.PurchaseReturnClassSummaryViewModel>();

                for (int i = 0; i < items.Count && i < details.Count; i++)
                {
                    var item = items[i];
                    var d = details[i];

                    decimal gstPercent = item.SgstRate + item.CgstRate;

                    if (!classSummaryDict.TryGetValue(gstPercent, out var summary))
                    {
                        summary = new SSK_ERP.Controllers.Purchase.PurchaseReturnController.PurchaseReturnClassSummaryViewModel
                        {
                            ClassName = $"GST {gstPercent:0.00}%",
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

                var model = new SSK_ERP.Controllers.Purchase.PurchaseReturnController.PurchaseReturnPrintViewModel
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
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        private void InsertDetails(TransactionMaster master, List<ChellanDetailRow> details)
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

                        if (!string.IsNullOrWhiteSpace(d.BillNo) && master.TRANREFID > 0 && d.MaterialId > 0)
                        {
                            var invoiceMaster = db.TransactionMasters.FirstOrDefault(t =>
                                t.REGSTRID == PurchaseInvoiceRegisterId &&
                                t.TRANREFID == master.TRANREFID &&
                                t.TRANREFNO == d.BillNo);

                            if (invoiceMaster != null)
                            {
                                var invoiceDetail = db.TransactionDetails.FirstOrDefault(td =>
                                    td.TRANMID == invoiceMaster.TRANMID &&
                                    td.TRANDREFID == d.MaterialId);

                                if (invoiceDetail != null)
                                {
                                    sourceDetailId = invoiceDetail.TRANDID;
                                    sourceRefId = d.MaterialId;

                                    var invoiceBatch = db.TransactionBatchDetails.FirstOrDefault(tb =>
                                        tb.TRANDID == invoiceDetail.TRANDID &&
                                        tb.TRANBDNO == batchNo);

                                    if (invoiceBatch != null)
                                    {
                                        sourceBatchId = invoiceBatch.TRANBID;
                                    }
                                }
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

        private string FormatChellanTrandNo(int tranNo, DateTime tranDate)
        {
            int fyStartYear = tranDate.Month >= 4 ? tranDate.Year : tranDate.Year - 1;
            int fyEndYear = fyStartYear + 1;
            string fy = fyStartYear.ToString().Substring(2) + "-" + fyEndYear.ToString().Substring(2);
            return $"CH/{fy}/{tranNo.ToString().PadLeft(4, '0')}";
        }

        private class ChellanDetailRow
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
            public decimal Ptr { get; set; }
            public decimal Mrp { get; set; }
            public decimal BoxQty { get; set; }
            public string Packing { get; set; }
            public int SourceBatchId { get; set; }
            public int SourceDetailId { get; set; }
            public int SourceRefId { get; set; }
            public decimal ActualQty { get; set; }
        }

        public class ChellanPrintItemViewModel
        {
            public string Division { get; set; }
            public string MaterialName { get; set; }
            public string HsnCode { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public string BillNo { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public string Packing { get; set; }
            public decimal Boxes { get; set; }
        }

        public class ChellanPrintViewModel
        {
            public TransactionMaster Master { get; set; }
            public SupplierMaster Supplier { get; set; }
            public LocationMaster Location { get; set; }
            public StateMaster State { get; set; }
            public IList<ChellanPrintItemViewModel> Items { get; set; }
        }
    }
}
