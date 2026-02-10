using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Purchase
{
    [SessionExpire]
    public class PurchaseOrderController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesOrderRegisterId = 1;
        private const int PurchaseRegisterId = 2;

        [Authorize(Roles = "PurchaseOrderIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "PurchaseOrderCreate,PurchaseOrderEdit")]
        public ActionResult Form()
        {
            return View();
        }

        [Authorize(Roles = "PurchaseOrderCreate,PurchaseOrderEdit")]
        public ActionResult PoEdit(int id)
        {
            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Invalid Purchase Order.";
                return RedirectToAction("Index");
            }

            var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == PurchaseRegisterId);
            if (master == null)
            {
                TempData["ErrorMessage"] = "Purchase Order not found.";
                return RedirectToAction("Index");
            }

            var detailRows = new List<PurchaseOrderDetailRow>();
            var details = db.TransactionDetails
                .Where(d => d.TRANMID == master.TRANMID)
                .OrderBy(d => d.TRANDID)
                .ToList();

            foreach (var d in details)
            {
                detailRows.Add(new PurchaseOrderDetailRow
                {
                    MaterialId = d.TRANDREFID,
                    Qty = d.TRANDQTY,
                    Rate = d.TRANDRATE,
                    Amount = d.TRANDGAMT,
                    ProfitPercent = d.TRANDMTRLPRFT,
                    ActualRate = d.TRANDARATE
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
                master.DISPSTATUS.ToString()
            );

            // Determine whether this PO is linked to a Supplier or a Customer.
            var linkedSupplier = db.SupplierMasters
                .FirstOrDefault(s => s.CATEID == master.TRANREFID && (s.DISPSTATUS == 0 || s.DISPSTATUS == null));

            if (linkedSupplier != null)
            {
                // Supplier-based PO (new flow). Populate dropdown with suppliers and treat as PO-from-Sales (Supplier Name).
                var supplierList = db.SupplierMasters
                    .Where(s => s.DISPSTATUS == 0 || s.DISPSTATUS == null)
                    .OrderBy(s => s.CATENAME)
                    .Select(s => new
                    {
                        s.CATEID,
                        Name = s.CATENAME
                    })
                    .ToList();

                ViewBag.CustomerList = new SelectList(supplierList, "CATEID", "Name", master.TRANREFID);
                ViewBag.IsPoFromSalesOrder = true;
            }
            else
            {
                // Legacy PO that still references a Customer. Use customers list and normal Sales Order behaviour.
                var customerList = db.CustomerMasters
                    .Where(c => c.DISPSTATUS == 0)
                    .OrderBy(c => c.CATENAME)
                    .Select(c => new
                    {
                        c.CATEID,
                        c.CATENAME
                    })
                    .ToList();

                ViewBag.CustomerList = new SelectList(customerList, "CATEID", "CATENAME", master.TRANREFID);
                ViewBag.IsPoFromSalesOrder = false;
            }

            ViewBag.StateTypeList = new SelectList(
                new[]
                {
                    new { Value = "0", Text = "Local" },
                    new { Value = "1", Text = "Interstate" }
                },
                "Value",
                "Text",
                master.TRANSTATETYPE.ToString()
            );

            ViewBag.DetailRowsJson = detailRows.Any()
                ? JsonConvert.SerializeObject(detailRows)
                : "[]";

            ViewBag.FormAction = "SavePoEdit";
            ViewBag.FormController = "PurchaseOrder";

            return View("~/Views/SalesOrder/Form.cshtml", master);
        }

        [HttpGet]
        public JsonResult GetAjaxData(string fromDate = null, string toDate = null)
        {
            try
            {
                var query = db.TransactionMasters
                    .Where(t => t.REGSTRID == PurchaseRegisterId);

                DateTime parsedFromDate;
                if (!string.IsNullOrWhiteSpace(fromDate) &&
                    DateTime.TryParse(fromDate, out parsedFromDate))
                {
                    query = query.Where(t => t.TRANDATE >= parsedFromDate);
                }

                DateTime parsedToDate;
                if (!string.IsNullOrWhiteSpace(toDate) &&
                    DateTime.TryParse(toDate, out parsedToDate))
                {
                    var exclusiveToDate = parsedToDate.Date.AddDays(1);
                    query = query.Where(t => t.TRANDATE < exclusiveToDate);
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
                t.TRANREFNO,
                PODate = t.PRCSDATE,
                SupplierName = t.TRANREFNAME,
                Amount = t.TRANNAMT,
                Status = t.DISPSTATUS == 0 ? "Enabled" : "Disabled"
            })
            .ToList();

                return Json(new { data }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [Authorize(Roles = "SalesOrderCreate,SalesOrderEdit,PurchaseOrderCreate,PurchaseOrderEdit,SalesOrderPO")]
        public ActionResult PoForm(int id)
        {
            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Invalid Sales Order.";
                return RedirectToAction("Index", "SalesOrder");
            }

            var source = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == SalesOrderRegisterId);
            if (source == null)
            {
                TempData["ErrorMessage"] = "Sales Order not found.";
                return RedirectToAction("Index", "SalesOrder");
            }

            bool hasPo = db.TransactionMasters.Any(t => t.REGSTRID == PurchaseRegisterId && t.TRANLMID == source.TRANMID);
            if (hasPo)
            {
                TempData["ErrorMessage"] = "PO already exists for this Sales Order.";
                return RedirectToAction("Index", "SalesOrder");
            }

            var compyObj = Session["CompyId"] ?? Session["compyid"];
            int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

            var maxTranNo = db.TransactionMasters
                .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseRegisterId)
                .Select(t => (int?)t.TRANNO)
                .Max();

            int nextTranNo = (maxTranNo ?? 0) + 1;

            var model = new TransactionMaster
            {
                TRANDATE = DateTime.Today,
                TRANTIME = DateTime.Now,
                TRANNO = nextTranNo,
                TRANDNO = nextTranNo.ToString("D4"),
                // For PO from Sales Order, the supplier will be chosen on the form; start with no reference selected.
                TRANREFID = 0,
                TRANREFNAME = null,
                TRANSTATETYPE = 0,
                TRANREFNO = source.TRANREFNO,
                TRANLMID = source.TRANMID,
                DISPSTATUS = 0
            };

            var detailRows = new List<PurchaseOrderDetailRow>();
            var details = db.TransactionDetails.Where(d => d.TRANMID == source.TRANMID).ToList();
            foreach (var d in details)
            {
                detailRows.Add(new PurchaseOrderDetailRow
                {
                    MaterialId = d.TRANDREFID,
                    Qty = d.TRANDQTY,
                    Rate = d.TRANDRATE,
                    Amount = d.TRANDGAMT,
                    ProfitPercent = d.TRANDMTRLPRFT,
                    ActualRate = d.TRANDARATE
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

            // For PO created from Sales Order, bind the dropdown to active suppliers so the user can pick a Supplier Name.
            var supplierList = db.SupplierMasters
                .Where(s => s.DISPSTATUS == 0 || s.DISPSTATUS == null)
                .OrderBy(s => s.CATENAME)
                .Select(s => new
                {
                    s.CATEID,
                    Name = s.CATENAME
                })
                .ToList();

            ViewBag.CustomerList = new SelectList(supplierList, "CATEID", "Name", model.TRANREFID);

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

            ViewBag.DetailRowsJson = detailRows.Any()
                ? JsonConvert.SerializeObject(detailRows)
                : "[]";

            ViewBag.FormAction = "SavePoFromSalesOrder";
            ViewBag.FormController = "PurchaseOrder";
            ViewBag.IsPoFromSalesOrder = true;

            return View("~/Views/SalesOrder/Form.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SalesOrderCreate,SalesOrderEdit,PurchaseOrderCreate,PurchaseOrderEdit,SalesOrderPO")]
        public ActionResult SavePoFromSalesOrder(TransactionMaster master, string detailRowsJson)
        {
            try
            {
                if (master.TRANLMID <= 0)
                {
                    TempData["ErrorMessage"] = "Invalid source Sales Order.";
                    return RedirectToAction("Index", "SalesOrder");
                }

                bool hasPo = db.TransactionMasters.Any(t => t.REGSTRID == PurchaseRegisterId && t.TRANLMID == master.TRANLMID);
                if (hasPo)
                {
                    TempData["ErrorMessage"] = "A PO already exists for this Sales Order.";
                    return RedirectToAction("Index", "SalesOrder");
                }

                var source = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANLMID && t.REGSTRID == SalesOrderRegisterId);
                if (source == null)
                {
                    TempData["ErrorMessage"] = "Source Sales Order not found.";
                    return RedirectToAction("Index", "SalesOrder");
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<PurchaseOrderDetailRow>()
                    : JsonConvert.DeserializeObject<List<PurchaseOrderDetailRow>>(detailRowsJson) ?? new List<PurchaseOrderDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0 && d.Rate >= 0)
                    .ToList();

                var sourceDetails = db.TransactionDetails
                    .Where(d => d.TRANMID == source.TRANMID)
                    .OrderBy(d => d.TRANDID)
                    .ToList();

                for (int i = 0; i < details.Count && i < sourceDetails.Count; i++)
                {
                    details[i].MaterialId = sourceDetails[i].TRANDREFID;
                }

                details = details.Take(sourceDetails.Count).ToList();

                if (!details.Any())
                {
                    TempData["ErrorMessage"] = "Please add at least one detail row.";
                    return RedirectToAction("PoForm", new { id = master.TRANLMID });
                }

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                short tranStateType = 0;

                // If a supplier has been selected on the PO form, prefer that as the reference.
                if (master.TRANREFID > 0)
                {
                    var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == master.TRANREFID);
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
                }
                else
                {
                    // Fallback to original behaviour using the Sales Order customer
                    if (source.TRANREFID <= 0)
                    {
                        TempData["ErrorMessage"] = "Source Sales Order has no customer.";
                        return RedirectToAction("Index", "SalesOrder");
                    }

                    var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == source.TRANREFID);
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
                }

                master.TRANSTATETYPE = tranStateType;
                master.COMPYID = compyId;
                master.SDPTID = 0;
                master.REGSTRID = PurchaseRegisterId;
                master.TRANBTYPE = 0;
                master.EXPRTSTATUS = 0;
                master.TRANTIME = DateTime.Now;

                if (string.IsNullOrWhiteSpace(master.TRANREFNO))
                {
                    master.TRANREFNO = "-";
                }

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseRegisterId)
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

                var poDetails = db.TransactionDetails.Where(d => d.TRANMID == master.TRANMID).ToList();
                foreach (var d in poDetails)
                {
                    d.TRANDAID = d.TRANDID;
                }
                db.SaveChanges();

                TempData["SuccessMessage"] = "PO created successfully.";
                return RedirectToAction("Index", "SalesOrder");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index", "SalesOrder");
            }
        }

        [HttpGet]
        public JsonResult GetSupplierDetailsForPo(int id)
        {
            try
            {
                var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == id && (s.DISPSTATUS == 0 || s.DISPSTATUS == null));
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "PurchaseOrderEdit")]
        public ActionResult SavePoEdit(TransactionMaster master, string detailRowsJson)
        {
            try
            {
                if (master.TRANMID <= 0)
                {
                    TempData["ErrorMessage"] = "Invalid Purchase Order.";
                    return RedirectToAction("Index");
                }

                var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANMID && t.REGSTRID == PurchaseRegisterId);
                if (existing == null)
                {
                    TempData["ErrorMessage"] = "Purchase Order not found.";
                    return RedirectToAction("Index");
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<PurchaseOrderDetailRow>()
                    : JsonConvert.DeserializeObject<List<PurchaseOrderDetailRow>>(detailRowsJson) ?? new List<PurchaseOrderDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0 && d.Rate >= 0)
                    .ToList();

                if (!details.Any())
                {
                    TempData["ErrorMessage"] = "Please add at least one detail row.";
                    return RedirectToAction("PoEdit", new { id = master.TRANMID });
                }

                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                existing.TRANDATE = master.TRANDATE;
                existing.TRANTIME = DateTime.Now;
                existing.DISPSTATUS = master.DISPSTATUS;
                existing.TRANRMKS = master.TRANRMKS;
                existing.LMUSRID = userName;
                existing.PRCSDATE = DateTime.Now;

                var existingDetails = db.TransactionDetails.Where(d => d.TRANMID == existing.TRANMID).ToList();
                if (existingDetails.Any())
                {
                    db.TransactionDetails.RemoveRange(existingDetails);
                    db.SaveChanges();
                }

                InsertDetails(existing, details);

                TempData["SuccessMessage"] = "Purchase Order updated successfully.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// Returns material and material group data for the Purchase Order detail grid.
        /// Used by the PurchaseOrder/Form.cshtml script to populate dropdowns and auto-fill
        /// material group when a material is selected.
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
                        groupId = m.MTRLGID
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
        /// Returns active cost factors from COSTFACTORMASTER for use in the Purchase Order TAX / Cost Factor popup.
        /// </summary>
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

        [Authorize(Roles = "PurchaseOrderPrint")]
        public ActionResult Print(int id)
        {
            try
            {
                var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == PurchaseRegisterId);
                if (master == null)
                {
                    TempData["ErrorMessage"] = "Purchase Order not found.";
                    return RedirectToAction("Index");
                }

                var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == master.TRANREFID);
                var customer = supplier == null
                    ? db.CustomerMasters.FirstOrDefault(c => c.CATEID == master.TRANREFID)
                    : null;

                LocationMaster location = null;
                StateMaster state = null;

                int? locId = supplier != null ? (int?)supplier.LOCTID : customer != null ? (int?)customer.LOCTID : null;
                int? stateId = supplier != null ? (int?)supplier.STATEID : customer != null ? (int?)customer.STATEID : null;

                if (locId.HasValue)
                {
                    location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == locId.Value);
                }

                if (stateId.HasValue)
                {
                    state = db.StateMasters.FirstOrDefault(s => s.STATEID == stateId.Value);
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

                        return new PurchaseOrderPrintItemViewModel
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

                var model = new PurchaseOrderPrintViewModel
                {
                    TRANMID = master.TRANMID,
                    TRANNO = master.TRANNO,
                    TRANDNO = master.TRANDNO,
                    TRANREFNO = master.TRANREFNO,
                    TRANDATE = master.TRANDATE,
                    SupplierName = supplier != null ? supplier.CATENAME : customer != null ? customer.CATENAME : master.TRANREFNAME,
                    SupplierCode = supplier != null ? supplier.CATECODE : customer != null ? customer.CATECODE : string.Empty,
                    Address1 = supplier != null ? supplier.CATEADDR1 : customer != null ? customer.CATEADDR1 : string.Empty,
                    Address2 = supplier != null ? supplier.CATEADDR2 : customer != null ? customer.CATEADDR2 : string.Empty,
                    Address3 = supplier != null ? supplier.CATEADDR3 : customer != null ? customer.CATEADDR3 : string.Empty,
                    Address4 = supplier != null ? supplier.CATEADDR4 : customer != null ? customer.CATEADDR4 : string.Empty,
                    City = location != null ? location.LOCTDESC : string.Empty,
                    Pincode = supplier != null ? supplier.CATEADDR5 : customer != null ? customer.CATEADDR5 : string.Empty,
                    State = state != null ? state.STATEDESC : string.Empty,
                    StateCode = state != null ? state.STATECODE : string.Empty,
                    GstNo = supplier != null ? supplier.CATE_GST_NO : customer != null ? customer.CATE_GST_NO : string.Empty,
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
                TempData["ErrorMessage"] = "Error loading Purchase Order: " + ex.Message;
                return RedirectToAction("Index");
            }
        }



        [HttpPost]
        public ActionResult Del(int id)
        {
            try
            {
                if (!User.IsInRole("PurchaseOrderDelete"))
                {
                    return Json("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }

                var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == PurchaseRegisterId);
                if (existing == null)
                {
                    return Json("Record not found");
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

        private void InsertDetails(TransactionMaster master, List<PurchaseOrderDetailRow> details)
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

        private class PurchaseOrderDetailRow
        {
            public int MaterialId { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public decimal ProfitPercent { get; set; }
            public decimal ActualRate { get; set; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }

            base.Dispose(disposing);
        }

        public class PurchaseOrderPrintItemViewModel
        {
            public string MaterialName { get; set; }
            public string MaterialGroupName { get; set; }
            public decimal ProfitPercent { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal ActualRate { get; set; }
            public decimal Amount { get; set; }
        }

        public class PurchaseOrderPrintViewModel
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

            public decimal GrossAmount { get; set; }
            public decimal NetAmount { get; set; }
            public string AmountInWords { get; set; }
            public string CompanyAddress { get; set; }

            public List<PurchaseOrderPrintItemViewModel> Items { get; set; }
        }
    }
}

