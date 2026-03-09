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
    public class SalesOrderController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesOrderRegisterId = 1;
        private const int PurchaseRegisterId = 2;
        private const int PurchaseInvoiceRegisterId = 18;
        private const int SalesInvoiceRegisterId = 20;

        [Authorize(Roles = "SalesOrderIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "SalesOrderPrint")]
        public ActionResult Print(int id)
        {
            try
            {
                var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == SalesOrderRegisterId);
                if (master == null)
                {
                    TempData["ErrorMessage"] = "Sales Order not found.";
                    return RedirectToAction("Index");
                }

                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == master.TRANREFID);
                LocationMaster location = null;
                StateMaster state = null;

                if (customer != null)
                {
                    location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == customer.LOCTID);
                    state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
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

                var groupIds = materials.Values
                    .Select(m => m.MTRLGID)
                    .Distinct()
                    .ToList();

                var groups = db.MaterialGroupMasters
                    .Where(g => groupIds.Contains(g.MTRLGID))
                    .ToDictionary(g => g.MTRLGID, g => g);

                var items = details
                    .Select(d =>
                    {
                        MaterialMaster material;
                        materials.TryGetValue(d.TRANDREFID, out material);

                        MaterialGroupMaster group = null;
                        if (material != null)
                        {
                            groups.TryGetValue(material.MTRLGID, out group);
                        }

                        return new SalesOrderPrintItemViewModel
                        {
                            MaterialName = d.TRANDREFNAME,
                            MaterialGroupName = group != null ? group.MTRLGDESC : string.Empty,
                            ProfitPercent = d.TRANDMTRLPRFT,
                            Qty = d.TRANDQTY,
                            Rate = d.TRANDRATE,
                            ActualRate = d.TRANDARATE,
                            Amount = d.TRANDNAMT
                        };
                    })
                    .ToList();

                var company = db.companymasters.FirstOrDefault(c => c.COMPID == master.COMPYID);
                if (company == null)
                {
                    company = db.companymasters.FirstOrDefault();
                }

                string companyAddress = string.Empty;
                if (company != null && !string.IsNullOrWhiteSpace(company.COMPADDR))
                {
                    companyAddress = company.COMPADDR;
                }

                var model = new SalesOrderPrintViewModel
                {
                    TRANMID = master.TRANMID,
                    TRANNO = master.TRANNO,
                    TRANDNO = master.TRANDNO,
                    TRANREFNO = master.TRANREFNO,
                    TRANDATE = master.TRANDATE,
                    CustomerName = customer != null ? customer.CATENAME : master.TRANREFNAME,
                    CustomerCode = customer != null ? customer.CATECODE : string.Empty,
                    Address1 = customer != null ? customer.CATEADDR1 : string.Empty,
                    Address2 = customer != null ? customer.CATEADDR2 : string.Empty,
                    Address3 = customer != null ? customer.CATEADDR3 : string.Empty,
                    Address4 = customer != null ? customer.CATEADDR4 : string.Empty,
                    City = location != null ? location.LOCTDESC : string.Empty,
                    Pincode = customer != null ? customer.CATEADDR5 : string.Empty,
                    State = state != null ? state.STATEDESC : string.Empty,
                    StateCode = state != null ? state.STATECODE : string.Empty,
                    GstNo = customer != null ? customer.CATE_GST_NO : string.Empty,
                    GrossAmount = master.TRANGAMT,
                    NetAmount = master.TRANNAMT,
                    AmountInWords = ConvertAmountToWords(master.TRANNAMT),
                    Items = items,
                    CompanyAddress = companyAddress
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading Sales Order: " + ex.Message;
                return RedirectToAction("Index");
            }
        }



        [Authorize(Roles = "SalesOrderCreate,SalesOrderEdit")]
        public ActionResult Form(int? id)
        {
            TransactionMaster model;
            var detailRows = new List<SalesOrderDetailRow>();
            bool hasPo = false;

            if (id.HasValue && id.Value > 0)
            {
                if (!User.IsInRole("SalesOrderEdit"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to edit Sales Orders.";
                    return RedirectToAction("Index");
                }

                model = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == SalesOrderRegisterId);
                if (model == null)
                {
                    TempData["ErrorMessage"] = "Sales Order not found.";
                    return RedirectToAction("Index");
                }

                // Prevent editing a Sales Order once a Sales Invoice has been created for it
                bool hasSi = db.TransactionMasters.Any(t => t.REGSTRID == SalesInvoiceRegisterId && t.TRANLMID == model.TRANMID);
                if (hasSi)
                {
                    TempData["ErrorMessage"] = "This Sales Order already has a Sales Invoice and cannot be edited.";
                    return RedirectToAction("Index");
                }

                // If a PO is already created for this Sales Order, lock materials that are part of the PO.
                hasPo = db.TransactionMasters.Any(t => t.REGSTRID == PurchaseRegisterId && t.TRANLMID == model.TRANMID);

                Dictionary<string, int> poKeyCounts = null;
                Dictionary<int, int> poMaterialCounts = null;
                if (hasPo)
                {
                    var poMaster = db.TransactionMasters
                        .Where(t => t.REGSTRID == PurchaseRegisterId && t.TRANLMID == model.TRANMID)
                        .OrderByDescending(t => t.TRANMID)
                        .FirstOrDefault();

                    if (poMaster != null)
                    {
                        var poDetails = db.TransactionDetails
                            .Where(d => d.TRANMID == poMaster.TRANMID)
                            .OrderBy(d => d.TRANDID)
                            .ToList();

                        poKeyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        poMaterialCounts = new Dictionary<int, int>();
                        foreach (var d in poDetails)
                        {
                            var key = BuildRowKey(d.TRANDREFID, d.TRANDQTY, d.TRANDRATE);

                            if (poKeyCounts.ContainsKey(key))
                            {
                                poKeyCounts[key]++;
                            }
                            else
                            {
                                poKeyCounts[key] = 1;
                            }

                            if (poMaterialCounts.ContainsKey(d.TRANDREFID))
                            {
                                poMaterialCounts[d.TRANDREFID]++;
                            }
                            else
                            {
                                poMaterialCounts[d.TRANDREFID] = 1;
                            }
                        }
                    }
                }

                var details = db.TransactionDetails
                    .Where(d => d.TRANMID == model.TRANMID)
                    .OrderBy(d => d.TRANDID)
                    .ToList();

                // Two-pass lock:
                // 1) Exact match by MaterialId+Qty+Rate across ALL SO rows
                // 2) Fallback by MaterialId-only for remaining counts
                var lockedByIndex = new bool[details.Count];

                if (poKeyCounts != null)
                {
                    for (int i = 0; i < details.Count; i++)
                    {
                        var d = details[i];
                        var soKey = BuildRowKey(d.TRANDREFID, d.TRANDQTY, d.TRANDRATE);

                        if (poKeyCounts.TryGetValue(soKey, out var keyRemaining) && keyRemaining > 0)
                        {
                            lockedByIndex[i] = true;
                            poKeyCounts[soKey] = keyRemaining - 1;

                            if (poMaterialCounts != null && poMaterialCounts.TryGetValue(d.TRANDREFID, out var matRemaining) && matRemaining > 0)
                            {
                                poMaterialCounts[d.TRANDREFID] = matRemaining - 1;
                            }
                        }
                    }
                }

                if (poMaterialCounts != null)
                {
                    for (int i = 0; i < details.Count; i++)
                    {
                        if (lockedByIndex[i])
                        {
                            continue;
                        }

                        var d = details[i];
                        if (poMaterialCounts.TryGetValue(d.TRANDREFID, out var remaining) && remaining > 0)
                        {
                            lockedByIndex[i] = true;
                            poMaterialCounts[d.TRANDREFID] = remaining - 1;
                        }
                    }
                }

                for (int i = 0; i < details.Count; i++)
                {
                    var d = details[i];
                    detailRows.Add(new SalesOrderDetailRow
                    {
                        DetailId = d.TRANDID,
                        MaterialId = d.TRANDREFID,
                        Qty = d.TRANDQTY,
                        Rate = d.TRANDRATE,
                        Amount = d.TRANDGAMT,
                        ProfitPercent = d.TRANDMTRLPRFT,
                        ActualRate = d.TRANDARATE,
                        IsLocked = lockedByIndex[i]
                    });
                }
            }
            else
            {
                if (!User.IsInRole("SalesOrderCreate"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to create Sales Orders.";
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
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesOrderRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;
                model.TRANNO = nextTranNo;
                model.TRANDNO = nextTranNo.ToString("D4");
            }

            ViewBag.HasPo = hasPo;

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

            ViewBag.DetailRowsJson = detailRows.Any()
                ? JsonConvert.SerializeObject(detailRows)
                : "[]";

            var customerList = db.CustomerMasters
                .Where(c => c.DISPSTATUS == 0)
                .OrderBy(c => c.CATENAME)
                .Select(c => new
                {
                    c.CATEID,
                    c.CATENAME
                })
                .ToList();

            ViewBag.CustomerList = new SelectList(customerList, "CATEID", "CATENAME", model.TRANREFID);

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
        [Authorize(Roles = "SalesOrderCreate,SalesOrderEdit")]
        public ActionResult savedata(TransactionMaster master, string detailRowsJson)
        {
            bool isEdit = master != null && master.TRANMID > 0 &&
                          db.TransactionMasters.Any(t => t.TRANMID == master.TRANMID && t.REGSTRID == SalesOrderRegisterId);
            try
            {
                if (isEdit)
                {
                    if (!User.IsInRole("SalesOrderEdit"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to edit Sales Orders.";
                        return RedirectToAction("Index");
                    }
                }
                else
                {
                    if (!User.IsInRole("SalesOrderCreate"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to create Sales Orders.";
                        return RedirectToAction("Index");
                    }
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<SalesOrderDetailRow>()
                    : JsonConvert.DeserializeObject<List<SalesOrderDetailRow>>(detailRowsJson) ?? new List<SalesOrderDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0 && d.Rate >= 0)
                    .ToList();

                // If PO exists for this Sales Order, prevent changing/removing PO-linked material rows.
                if (isEdit)
                {
                    bool hasPo = db.TransactionMasters.Any(t => t.REGSTRID == PurchaseRegisterId && t.TRANLMID == master.TRANMID);
                    if (hasPo)
                    {
                        var poMaster = db.TransactionMasters
                            .Where(t => t.REGSTRID == PurchaseRegisterId && t.TRANLMID == master.TRANMID)
                            .OrderByDescending(t => t.TRANMID)
                            .FirstOrDefault();

                        if (poMaster != null)
                        {
                            var poDetails = db.TransactionDetails
                                .Where(d => d.TRANMID == poMaster.TRANMID)
                                .OrderBy(d => d.TRANDID)
                                .ToList();

                            var requiredKeyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            var requiredMaterialCounts = new Dictionary<int, int>();
                            foreach (var d in poDetails)
                            {
                                var key = BuildRowKey(d.TRANDREFID, d.TRANDQTY, d.TRANDRATE);

                                if (requiredKeyCounts.ContainsKey(key))
                                {
                                    requiredKeyCounts[key]++;
                                }
                                else
                                {
                                    requiredKeyCounts[key] = 1;
                                }

                                if (requiredMaterialCounts.ContainsKey(d.TRANDREFID))
                                {
                                    requiredMaterialCounts[d.TRANDREFID]++;
                                }
                                else
                                {
                                    requiredMaterialCounts[d.TRANDREFID] = 1;
                                }
                            }

                            // Full lock: for PO-linked rows, do not allow changing Qty/Rate in Sales Order.
                            var existingSoDetails = db.TransactionDetails
                                .Where(d => d.TRANMID == master.TRANMID)
                                .OrderBy(d => d.TRANDID)
                                .ToList();

                            var newKeyCounts = details
                                .GroupBy(d => BuildRowKey(d.MaterialId, d.Qty, d.Rate))
                                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                            foreach (var req in requiredKeyCounts)
                            {
                                int provided = newKeyCounts.ContainsKey(req.Key) ? newKeyCounts[req.Key] : 0;
                                if (provided < req.Value)
                                {
                                    TempData["ErrorMessage"] = "This Sales Order has already been converted to PO. You cannot edit or delete PO-generated rows.";
                                    return RedirectToAction("Form", new { id = master.TRANMID });
                                }
                            }

                            // For PO-linked rows, ensure the locked rows remain unchanged in Sales Order (Material/Qty/Rate)
                            // by comparing the specific existing SalesOrder detail IDs (TRANDID) that were consumed by the PO.
                            var remainingLockKeyCounts = new Dictionary<string, int>(requiredKeyCounts, StringComparer.OrdinalIgnoreCase);
                            var remainingLockMaterialCounts = new Dictionary<int, int>(requiredMaterialCounts);
                            var lockedDetailIds = new List<int>();
                            var lockedIndex = new bool[existingSoDetails.Count];

                            // Pass 1: exact key match
                            for (int i = 0; i < existingSoDetails.Count; i++)
                            {
                                var d = existingSoDetails[i];
                                var soKey = BuildRowKey(d.TRANDREFID, d.TRANDQTY, d.TRANDRATE);

                                if (remainingLockKeyCounts.TryGetValue(soKey, out var keyRemaining) && keyRemaining > 0)
                                {
                                    lockedIndex[i] = true;
                                    remainingLockKeyCounts[soKey] = keyRemaining - 1;
                                    lockedDetailIds.Add(d.TRANDID);

                                    if (remainingLockMaterialCounts.TryGetValue(d.TRANDREFID, out var matRemaining) && matRemaining > 0)
                                    {
                                        remainingLockMaterialCounts[d.TRANDREFID] = matRemaining - 1;
                                    }
                                }
                            }

                            // Pass 2: fallback by material
                            for (int i = 0; i < existingSoDetails.Count; i++)
                            {
                                if (lockedIndex[i])
                                {
                                    continue;
                                }

                                var d = existingSoDetails[i];
                                if (remainingLockMaterialCounts.TryGetValue(d.TRANDREFID, out var remaining) && remaining > 0)
                                {
                                    lockedIndex[i] = true;
                                    remainingLockMaterialCounts[d.TRANDREFID] = remaining - 1;
                                    lockedDetailIds.Add(d.TRANDID);
                                }
                            }

                            var submittedById = details
                                .Where(x => x != null && x.DetailId > 0)
                                .ToDictionary(x => x.DetailId, x => x);

                            foreach (var lockedId in lockedDetailIds)
                            {
                                var existingRow = existingSoDetails.FirstOrDefault(x => x.TRANDID == lockedId);
                                if (existingRow == null)
                                {
                                    continue;
                                }

                                if (!submittedById.TryGetValue(lockedId, out var submittedRow) || submittedRow == null)
                                {
                                    TempData["ErrorMessage"] = "This Sales Order has already been converted to PO. PO-generated rows cannot be edited (Material/Qty/Rate) or deleted.";
                                    return RedirectToAction("Form", new { id = master.TRANMID });
                                }

                                if (submittedRow.MaterialId != existingRow.TRANDREFID || submittedRow.Qty != existingRow.TRANDQTY || submittedRow.Rate != existingRow.TRANDRATE)
                                {
                                    TempData["ErrorMessage"] = "This Sales Order has already been converted to PO. PO-generated rows cannot be edited (Material/Qty/Rate) or deleted.";
                                    return RedirectToAction("Form", new { id = master.TRANMID });
                                }
                            }
                        }
                    }
                }

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
                    TempData["ErrorMessage"] = "Please select a customer.";
                    return RedirectToAction("Form", new { id = isEdit ? (int?)master.TRANMID : null });
                }

                if (!isEdit && string.IsNullOrWhiteSpace(master.TRANREFNO))
                {
                    TempData["ErrorMessage"] = "Please enter Bill No.";
                    return RedirectToAction("Form", new { id = isEdit ? (int?)master.TRANMID : null });
                }

                short tranStateType = 0;
                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == master.TRANREFID);
                if (customer != null)
                {
                    master.TRANREFID = customer.CATEID;
                    master.TRANREFNAME = customer.CATENAME;

                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
                    if (state != null)
                    {
                        tranStateType = state.STATETYPE;
                    }
                }

                master.TRANSTATETYPE = tranStateType;

                if (!string.IsNullOrWhiteSpace(master.TRANREFNO) && master.TRANREFNO.Trim().Length > 100)
                {
                    TempData["ErrorMessage"] = "Bill No cannot be greater than 100 characters.";
                    return RedirectToAction("Form", new { id = isEdit ? (int?)master.TRANMID : null });
                }

                master.COMPYID = compyId;
                master.SDPTID = 0;
                master.REGSTRID = SalesOrderRegisterId;
                master.TRANBTYPE = 0;
                if (master.TRANETYPE != 0 && master.TRANETYPE != 1)
                {
                    master.TRANETYPE = 0;
                }
                master.EXPRTSTATUS = 0;
                master.TRANTIME = DateTime.Now;
                if (string.IsNullOrWhiteSpace(master.TRANREFNO))
                {
                    master.TRANREFNO = "-";
                }

                if (isEdit)
                {
                    var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANMID && t.REGSTRID == SalesOrderRegisterId);
                    if (existing == null)
                    {
                        TempData["ErrorMessage"] = "Sales Order not found.";
                        return RedirectToAction("Index");
                    }

                    existing.TRANDATE = master.TRANDATE;
                    existing.TRANTIME = master.TRANTIME;
                    existing.TRANREFID = master.TRANREFID;
                    existing.TRANREFNAME = master.TRANREFNAME;
                    existing.TRANSTATETYPE = master.TRANSTATETYPE;
                    existing.TRANETYPE = master.TRANETYPE;
                    existing.TRANRMKS = master.TRANRMKS;
                    existing.DISPSTATUS = master.DISPSTATUS;
                    existing.LMUSRID = userName;
                    existing.PRCSDATE = DateTime.Now;
                    existing.TRANPCOUNT = 0;

                    var existingDetails = db.TransactionDetails.Where(d => d.TRANMID == existing.TRANMID).ToList();
                    if (existingDetails.Any())
                    {
                        db.TransactionDetails.RemoveRange(existingDetails);
                    }

                    InsertDetails(existing, details);
                    db.SaveChanges();
                }
                else
                {
                    var maxTranNo = db.TransactionMasters
                        .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesOrderRegisterId)
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
                    db.SaveChanges();
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Form", new { id = isEdit ? (int?)master.TRANMID : null });
            }
        }

        [HttpGet]
        public JsonResult GetAjaxData(string fromDate = null, string toDate = null)
        {
            try
            {
                // Base query: only Sales Orders. Company filter intentionally NOT applied
                // so that the index always shows all Sales Orders as requested.
                var query = db.TransactionMasters
                    .Where(t => t.REGSTRID == SalesOrderRegisterId);

                // Optional date filters (values come as yyyy-MM-dd from <input type="date">)
                DateTime parsedFromDate;
                if (!string.IsNullOrWhiteSpace(fromDate) &&
                    DateTime.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedFromDate))
                {
                    query = query.Where(t => t.TRANDATE >= parsedFromDate);
                }

                DateTime parsedToDate;
                if (!string.IsNullOrWhiteSpace(toDate) &&
                    DateTime.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedToDate))
                {
                    // include the full toDate day
                    var exclusiveToDate = parsedToDate.Date.AddDays(1);
                    query = query.Where(t => t.TRANDATE < exclusiveToDate);
                }

                // Materialize masters first to avoid nested db-calls in LINQ-to-Entities
                var masters = query
                    .OrderByDescending(t => t.TRANDATE)
                    .ThenByDescending(t => t.TRANMID)
                    .ToList();

                // Preload all Sales Orders that already have a PO
                var linkedTranIds = db.TransactionMasters
                    .Where(po => po.REGSTRID == PurchaseRegisterId && po.TRANLMID > 0)
                    .Select(po => po.TRANLMID)
                    .ToList();

                var poLinkSet = new HashSet<int>(linkedTranIds.Cast<int>());

                // Preload all Sales Orders that already have a Sales Invoice
                var salesInvoiceLinkedIds = db.TransactionMasters
                    .Where(si => si.REGSTRID == SalesInvoiceRegisterId && si.TRANLMID > 0)
                    .Select(si => si.TRANLMID)
                    .ToList();

                var siLinkSet = new HashSet<int>(salesInvoiceLinkedIds.Cast<int>());

                // Determine PO partial status: PO exists but does not cover all current SalesOrder detail lines.
                var soIds = masters.Select(m => m.TRANMID).ToList();
                var linkedPos = db.TransactionMasters
                    .Where(po => po.REGSTRID == PurchaseRegisterId && po.TRANLMID > 0 && soIds.Contains(po.TRANLMID))
                    .Select(po => new { po.TRANMID, po.TRANLMID })
                    .ToList();

                // For each SalesOrder, pick the latest linked PO.
                var latestPoBySo = linkedPos
                    .GroupBy(x => x.TRANLMID)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TRANMID).First().TRANMID);

                var poIds = latestPoBySo.Values.Distinct().ToList();

                // Preload all Purchase Orders that already have a Purchase Invoice
                var piLinkedPoIds = poIds.Any()
                    ? db.TransactionMasters
                        .Where(pi => pi.REGSTRID == PurchaseInvoiceRegisterId && pi.TRANLMID > 0 && poIds.Contains(pi.TRANLMID))
                        .Select(pi => pi.TRANLMID)
                        .ToList()
                    : new List<int>();

                var poWithPiSet = new HashSet<int>(piLinkedPoIds);
                var soWithPiSet = new HashSet<int>();
                foreach (var kv in latestPoBySo)
                {
                    int soId = kv.Key;
                    int poId = kv.Value;
                    if (poWithPiSet.Contains(poId))
                    {
                        soWithPiSet.Add(soId);
                    }
                }

                var soDetailCounts = db.TransactionDetails
                    .Where(d => soIds.Contains(d.TRANMID))
                    .GroupBy(d => new { d.TRANMID, d.TRANDREFID })
                    .Select(g => new { SoId = g.Key.TRANMID, MaterialId = g.Key.TRANDREFID, Cnt = g.Count() })
                    .ToList();

                var poDetailCounts = new List<PoDetailCountRow>();
                if (poIds.Any())
                {
                    poDetailCounts = db.TransactionDetails
                        .Where(d => poIds.Contains(d.TRANMID))
                        .GroupBy(d => new { d.TRANMID, d.TRANDREFID })
                        .Select(g => new PoDetailCountRow
                        {
                            PoId = g.Key.TRANMID,
                            MaterialId = g.Key.TRANDREFID,
                            Cnt = g.Count()
                        })
                        .ToList();
                }

                var soCountMap = soDetailCounts
                    .GroupBy(x => x.SoId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.ToDictionary(x => x.MaterialId, x => x.Cnt)
                    );

                var poCountMap = new Dictionary<int, Dictionary<int, int>>();
                foreach (var poId in poIds)
                {
                    poCountMap[poId] = new Dictionary<int, int>();
                }

                foreach (var x in poDetailCounts)
                {
                    int poId = x.PoId;
                    int materialId = x.MaterialId;
                    int cnt = x.Cnt;
                    if (!poCountMap.TryGetValue(poId, out var matMap))
                    {
                        matMap = new Dictionary<int, int>();
                        poCountMap[poId] = matMap;
                    }
                    matMap[materialId] = cnt;
                }

                var partialSoSet = new HashSet<int>();
                foreach (var kv in latestPoBySo)
                {
                    int soId = kv.Key;
                    int poId = kv.Value;

                    if (!soCountMap.TryGetValue(soId, out var soMatCounts) || soMatCounts == null)
                    {
                        continue;
                    }

                    poCountMap.TryGetValue(poId, out var poMatCounts);
                    poMatCounts = poMatCounts ?? new Dictionary<int, int>();

                    bool isPartial = false;
                    foreach (var m in soMatCounts)
                    {
                        int soCnt = m.Value;
                        int poCnt = poMatCounts.ContainsKey(m.Key) ? poMatCounts[m.Key] : 0;
                        if (poCnt < soCnt)
                        {
                            isPartial = true;
                            break;
                        }
                    }

                    if (isPartial)
                    {
                        partialSoSet.Add(soId);
                    }
                }

                var data = masters
                    .Select(t => new
                    {
                        t.TRANMID,
                        t.TRANDATE,
                        t.TRANNO,
                        t.TRANREFNO,
                        t.TRANETYPE,
                        CustomerName = t.TRANREFNAME,
                        Amount = t.TRANNAMT,
                        Status = t.DISPSTATUS == 0 ? "Enabled" : "Disabled",
                        HasPo = poLinkSet.Contains(t.TRANMID),
                        HasSi = siLinkSet.Contains(t.TRANMID),
                        HasPoPartial = partialSoSet.Contains(t.TRANMID),
                        HasPi = soWithPiSet.Contains(t.TRANMID)
                    })
                    .ToList();

                return Json(new { data }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Returns customer address and state details for the selected customer,
        /// including STATETYPE (0 = Local, 1 = Interstate) for use in TRANSTATETYPE.
        /// </summary>
        [HttpGet]
        public JsonResult GetCustomerDetails(int id)
        {
            try
            {
                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == id && c.DISPSTATUS == 0);
                if (customer == null)
                {
                    return Json(new { success = false, message = "Customer not found." }, JsonRequestBehavior.AllowGet);
                }

                var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == customer.LOCTID);
                var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);

                var data = new
                {
                    Id = customer.CATEID,
                    Name = customer.CATENAME,
                    Address1 = customer.CATEADDR1,
                    Address2 = customer.CATEADDR2,
                    Address3 = customer.CATEADDR3,
                    Address4 = customer.CATEADDR4,
                    Pincode = customer.CATEADDR5,
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

        [HttpPost]
        public ActionResult Del(int id)
        {
            try
            {
                if (!User.IsInRole("SalesOrderDelete"))
                {
                    return Json("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }

                var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == SalesOrderRegisterId);
                if (existing == null)
                {
                    return Json("Record not found");
                }

                // Prevent deleting a Sales Order once a PO has been created for it
                bool hasPo = db.TransactionMasters.Any(t => t.REGSTRID == PurchaseRegisterId && t.TRANLMID == existing.TRANMID);
                if (hasPo)
                {
                    return Json("Cannot delete this Sales Order because a PO has already been created.");
                }

                // Prevent deleting a Sales Order once a Sales Invoice has been created for it
                bool hasSi = db.TransactionMasters.Any(t => t.REGSTRID == SalesInvoiceRegisterId && t.TRANLMID == existing.TRANMID);
                if (hasSi)
                {
                    return Json("Cannot delete this Sales Order because a Sales Invoice has already been created.");
                }

                var details = db.TransactionDetails.Where(d => d.TRANMID == existing.TRANMID).ToList();
                if (details.Any())
                {
                    db.TransactionDetails.RemoveRange(details);
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

        /// <summary>
        /// Returns material and material group data for the Sales Order Customer Order Details grid.
        /// Used by the SalesOrder/Form.cshtml script to populate dropdowns and auto-fill material group
        /// when a material is selected.
        /// </summary>
        [HttpGet]
        public JsonResult GetMaterialAndGroups()
        {
            try
            {
                var materials = db.MaterialMasters
                    .OrderBy(m => m.MTRLDESC)
                    .Select(m => new
                    {
                        id = m.MTRLID,
                        name = m.MTRLDESC,
                        groupId = m.MTRLGID,
                        rate = m.RATE,
                        profitPercent = m.MTRLPRFT
                    })
                    .ToList();

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

        /// <summary>
        /// Returns active cost factors from COSTFACTORMASTER for use in the Sales Order TAX / Cost Factor popup.
        /// </summary>
        [HttpGet]
        public JsonResult GetCostFactorsForSales()
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
        public JsonResult CalculateSalesOrderTax(short stateType, string detailRowsJson)
        {
            try
            {
                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<SalesOrderDetailRow>()
                    : JsonConvert.DeserializeObject<List<SalesOrderDetailRow>>(detailRowsJson) ?? new List<SalesOrderDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0 && d.Rate >= 0)
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
                    decimal rate = d.Rate;
                    decimal actualRate = d.ActualRate > 0 ? d.ActualRate : rate;
                    decimal gross = d.Amount > 0 ? d.Amount : qty * actualRate;

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

        private void InsertDetails(TransactionMaster master, List<SalesOrderDetailRow> details)
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
            int lineIndex = 0;

            short tranStateType = master.TRANSTATETYPE;
            int tranMid = master.TRANMID;

            foreach (var d in details)
            {
                lineIndex++;

                materials.TryGetValue(d.MaterialId, out var material);

                int hsnId = material != null ? material.HSNID : 0;
                hsnMap.TryGetValue(hsnId, out var hsn);

                decimal qty = d.Qty;
                decimal rate = d.Rate;
                decimal profitPercent = material != null ? material.MTRLPRFT : 0m;
                decimal actualRate = d.ActualRate > 0 ? d.ActualRate : rate;
                if (actualRate <= 0 && rate > 0 && profitPercent != 0)
                {
                    actualRate = Math.Round(rate + ((rate * profitPercent) / 100m), 2);
                }

                decimal gross = d.Amount > 0 ? d.Amount : qty * actualRate;

                decimal cgstAmt = 0m;
                decimal sgstAmt = 0m;
                decimal igstAmt = 0m;

                if (hsn != null)
                {
                    if (tranStateType == 0)
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

                var detail = new TransactionDetail
                {
                    TRANMID = tranMid,
                    TRANDREFID = material != null ? material.MTRLID : d.MaterialId,
                    TRANDREFNO = material != null ? material.MTRLCODE : string.Empty,
                    TRANDREFNAME = material != null ? material.MTRLDESC : string.Empty,
                    TRANDMTRLPRFT = profitPercent,
                    HSNID = hsnId,
                    PACKMID = 0,
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

            db.SaveChanges();
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

        private class SalesOrderDetailRow
        {
            public int DetailId { get; set; }
            public int MaterialId { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public decimal ProfitPercent { get; set; }
            public decimal ActualRate { get; set; }
            public bool IsLocked { get; set; }
        }

        private class PoDetailCountRow
        {
            public int PoId { get; set; }
            public int MaterialId { get; set; }
            public int Cnt { get; set; }
        }

        private static string BuildRowKey(int materialId, decimal qty, decimal rate)
        {
            return string.Concat(
                materialId,
                "|",
                NormalizeDecimalKey(qty),
                "|",
                NormalizeDecimalKey(rate)
            );
        }

        private static string NormalizeDecimalKey(decimal value)
        {
            // avoid false mismatches like 96 vs 96.00
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public class SalesOrderPrintItemViewModel
    {
        public string MaterialName { get; set; }
        public string MaterialGroupName { get; set; }
        public decimal ProfitPercent { get; set; }
        public decimal Qty { get; set; }
        public decimal Rate { get; set; }
        public decimal ActualRate { get; set; }
        public decimal Amount { get; set; }
    }

    public class SalesOrderPrintViewModel
    {
        public int TRANMID { get; set; }
        public int TRANNO { get; set; }
        public string TRANDNO { get; set; }
        public string TRANREFNO { get; set; }
        public DateTime TRANDATE { get; set; }

        public string CustomerName { get; set; }
        public string CustomerCode { get; set; }
        public string Address1 { get; set; }
        public string Address2 { get; set; }
        public string Address3 { get; set; }
        public string Address4 { get; set; }
        public string City { get; set; }
        public string Pincode { get; set; }
        public string State { get; set; }
        public string StateCode { get; set; }
        public string GstNo { get; set; }

        public decimal GrossAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string AmountInWords { get; set; }
        public string CompanyAddress { get; set; }

        public List<SalesOrderPrintItemViewModel> Items { get; set; }
    }
}
