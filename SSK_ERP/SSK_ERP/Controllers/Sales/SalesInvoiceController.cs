
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesInvoiceController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesInvoiceRegisterId = 20;
        private const int PurchaseInvoiceRegisterId = 18;

        private class SalesInvoiceListRow
        {
            public int TRANMID { get; set; }
            public DateTime TRANDATE { get; set; }
            public int TRANNO { get; set; }
            public string TRANDNO { get; set; }
            public string TRANREFNO { get; set; }
            public string TRANREFNAME { get; set; }
            public decimal TRANNAMT { get; set; }
            public short DISPSTATUS { get; set; }
        }

        private class PoNumberResultLocal
        {
            public string POREFNO { get; set; }
        }

        private class PurchaseBatchInfoLocal
        {
            public int TRANDID { get; set; }
            public string TRANBDNO { get; set; }
            public DateTime? TRANBEXPDATE { get; set; }
            public int? PACKMID { get; set; }
            public int TRANBQTY { get; set; }
            public decimal TRANBPTRRATE { get; set; }
            public decimal TRANBMRP { get; set; }
        }

        private class SalesInvoiceTaxFactorInput
        {
            public int CostFactorId { get; set; }
            public string ExpressionType { get; set; }
            public string Mode { get; set; }
            public decimal ExpressionValue { get; set; }
            public decimal Amount { get; set; }
        }

        private class SalesInvoiceManualFactorRow
        {
            public int CFID { get; set; }
            public decimal DEDEXPRN { get; set; }
            public string DEDMODE { get; set; }
            public int DEDTYPE { get; set; }
            public decimal DEDVALUE { get; set; }
        }

        public class SalesInvoiceFromPurchaseItemViewModel
        {
            public int PurchaseTranDetailId { get; set; }
            public int MaterialId { get; set; }
            public string MaterialName { get; set; }
            public string HsnCode { get; set; }
            public string BatchNo { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public string PackingName { get; set; }
            public int? PackingId { get; set; }
            public decimal BoxQty { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Ptr { get; set; }
            public decimal Mrp { get; set; }
            public decimal Amount { get; set; }
            public decimal CgstRate { get; set; }
            public decimal SgstRate { get; set; }
            public decimal IgstRate { get; set; }
            public bool Selected { get; set; }
        }

        public class SalesInvoiceFromPurchaseViewModel
        {
            public int PurchaseTranMid { get; set; }
            public int SalesTranMid { get; set; }

            // Header information
            public string SalesInvoiceNo { get; set; }
            public string PurchaseInvoiceNo { get; set; }
            // Used as Sales Invoice Date on create/edit forms
            public DateTime PurchaseDate { get; set; }
            public string RefNo { get; set; }
            public string PoNumber { get; set; }

            public int CreditDays { get; set; }

            // JSON representation of manual cost factors from TAX popup on CreateFromPurchase/Form
            public string TaxFactorsJson { get; set; }

            public string CustomerName { get; set; }
            public string CustomerAddress1 { get; set; }
            public string CustomerAddress2 { get; set; }
            public string CustomerCity { get; set; }
            public string CustomerState { get; set; }
            public string CustomerPincode { get; set; }
            public string CustomerGstNo { get; set; }

            public short StateType { get; set; }

            public decimal TotalAmount { get; set; }

            public decimal GrossAmount { get; set; }
            public decimal CgstAmount { get; set; }
            public decimal SgstAmount { get; set; }
            public decimal IgstAmount { get; set; }
            public decimal NetAmount { get; set; }

            public short Status { get; set; }
            public string Remarks { get; set; }

            public IList<SalesInvoiceFromPurchaseItemViewModel> Items { get; set; }
        }

        [Authorize(Roles = "SalesInvoiceIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceEdit,SalesInvoiceIndex")]
        public ActionResult CreateFromPurchase(int id)
        {
            var purchase = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == PurchaseInvoiceRegisterId);
            if (purchase == null)
            {
                TempData["ErrorMessage"] = "Purchase Invoice not found.";
                return RedirectToAction("Index", "PurchaseInvoice", new { area = "" });
            }

            // Resolve customer details via TRANLMID chain: PI -> PO -> SO -> Customer.
            string customerName = string.Empty;
            string customerAddr1 = string.Empty;
            string customerAddr2 = string.Empty;
            string customerCity = string.Empty;
            string customerState = string.Empty;
            string customerPincode = string.Empty;
            string customerGstNo = string.Empty;
            short customerStateType = 0;
            int creditDays = 0;

            // Step 1: Purchase Invoice.TRANLMID should point to Purchase Order
            TransactionMaster purchaseOrder = null;
            TransactionMaster salesOrder = null;

            if (purchase.TRANLMID > 0)
            {
                purchaseOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchase.TRANLMID);
            }

            // Step 2: Purchase Order.TRANLMID should point to Sales Order
            if (purchaseOrder != null && purchaseOrder.TRANLMID > 0)
            {
                salesOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchaseOrder.TRANLMID);
            }

            // Step 3: Sales Order.TRANREFID should be the Customer
            if (salesOrder != null && salesOrder.TRANREFID > 0)
            {
                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == salesOrder.TRANREFID && c.DISPSTATUS == 0);
                if (customer != null)
                {
                    customerName = customer.CATENAME;
                    customerAddr1 = customer.CATEADDR1;
                    customerAddr2 = customer.CATEADDR2;
                    customerPincode = customer.CATEADDR5;
                    creditDays = customer.CATE_CRDTPRD;

                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
                    if (state != null)
                    {
                        customerState = state.STATEDESC;
                        customerStateType = state.STATETYPE;
                    }

                    var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == customer.LOCTID);
                    if (location != null)
                    {
                        customerCity = location.LOCTDESC;
                    }

                    customerGstNo = customer.CATE_GST_NO;
                }
            }

            // Fallback: if customer not resolved, use supplier from Purchase Invoice
            if (string.IsNullOrWhiteSpace(customerName))
            {
                var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == purchase.TRANREFID);
                if (supplier != null)
                {
                    customerName = supplier.CATENAME;
                    customerAddr1 = supplier.CATEADDR1;
                    customerAddr2 = supplier.CATEADDR2;
                    customerPincode = supplier.CATEADDR5;
                    customerGstNo = supplier.CATE_GST_NO;

                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);
                    if (state != null)
                    {
                        customerState = state.STATEDESC;
                        customerStateType = state.STATETYPE;
                    }

                    var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == supplier.LOCTID);
                    if (location != null)
                    {
                        customerCity = location.LOCTDESC;
                    }
                }
                else
                {
                    customerName = purchase.TRANREFNAME;
                }
            }

            // Final fallback: if credit days are still zero but we have a resolved customer name,
            // try to pick up credit period from CustomerMaster by name.
            if (creditDays <= 0 && !string.IsNullOrWhiteSpace(customerName))
            {
                var customerByName = db.CustomerMasters
                    .FirstOrDefault(c => c.DISPSTATUS == 0
                                         && c.CATE_CRDTPRD > 0
                                         && c.CATENAME == customerName);

                if (customerByName != null)
                {
                    creditDays = customerByName.CATE_CRDTPRD;
                }
            }

            // Try to get PO Number if TRANLMID is set via existing stored procedure.
            string poNumber = null;
            if (purchase.TRANLMID > 0)
            {
                try
                {
                    var poResult = db.Database.SqlQuery<PoNumberResultLocal>(
                        "EXEC PR_PONODETAILS_PUR_INV @Tranlmid",
                        new System.Data.SqlClient.SqlParameter("@Tranlmid", purchase.TRANLMID)
                    ).FirstOrDefault();

                    if (poResult != null && !string.IsNullOrWhiteSpace(poResult.POREFNO))
                    {
                        poNumber = poResult.POREFNO;
                    }
                }
                catch
                {
                    if (string.IsNullOrWhiteSpace(poNumber))
                    {
                        poNumber = purchase.TRANREFNO;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(poNumber) && purchase != null)
            {
                poNumber = purchase.TRANREFNO;
            }

            if (string.IsNullOrWhiteSpace(poNumber))
            {
                poNumber = purchase.TRANREFNO;
            }

            var purchaseDetails = db.TransactionDetails
                .Where(d => d.TRANMID == purchase.TRANMID)
                .OrderBy(d => d.TRANDID)
                .ToList();

            var detailIds = purchaseDetails.Select(d => d.TRANDID).ToList();

            var batchInfos = new List<PurchaseBatchInfoLocal>();
            if (detailIds.Any())
            {
                var idList = string.Join(",", detailIds);
                var sql = @"SELECT TRANDID, TRANBDNO, TRANBEXPDATE, PACKMID, TRANBQTY, TRANBPTRRATE, TRANBMRP 
                             FROM TRANSACTIONBATCHDETAIL WHERE TRANDID IN (" + idList + ")";
                batchInfos = db.Database.SqlQuery<PurchaseBatchInfoLocal>(sql).ToList();
            }

            var materialIds = purchaseDetails.Select(d => d.TRANDREFID).Distinct().ToList();
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

            var packIds = batchInfos
                .Where(b => b.PACKMID.HasValue)
                .Select(b => b.PACKMID.Value)
                .Distinct()
                .ToList();

            var packingMap = db.PackingMasters
                .Where(p => packIds.Contains(p.PACKMID))
                .ToDictionary(p => p.PACKMID, p => p);

            var items = purchaseDetails
                .Select(d =>
                {
                    var batch = batchInfos.FirstOrDefault(b => b.TRANDID == d.TRANDID);

                    materials.TryGetValue(d.TRANDREFID, out var material);
                    string hsnCode = null;
                    decimal cgstRate = 0m;
                    decimal sgstRate = 0m;
                    decimal igstRate = 0m;

                    if (material != null && material.HSNID > 0 && hsnMap.TryGetValue(material.HSNID, out var hsn))
                    {
                        hsnCode = hsn.HSNCODE;
                        cgstRate = hsn.CGSTEXPRN;
                        sgstRate = hsn.SGSTEXPRN;
                        igstRate = hsn.IGSTEXPRN;
                    }

                    string packingName = null;
                    int? packingId = null;
                    decimal boxQty = 0m;
                    decimal ptr = 0m;
                    decimal mrp = 0m;

                    if (batch != null)
                    {
                        if (batch.PACKMID.HasValue && packingMap.TryGetValue(batch.PACKMID.Value, out var pack))
                        {
                            packingName = pack.PACKMDESC;
                            packingId = pack.PACKMID;
                        }

                        boxQty = batch.TRANBQTY;
                        ptr = batch.TRANBPTRRATE;
                        mrp = batch.TRANBMRP;
                    }

                    // Apply 5% markup on purchase rate for Sales Invoice Price Unit
                    decimal qty = d.TRANDQTY;
                    decimal baseRate = d.TRANDRATE;
                    decimal rate = Math.Round(baseRate * 1.05m, 2);
                    decimal amount = Math.Round(qty * rate, 2);

                    return new SalesInvoiceFromPurchaseItemViewModel
                    {
                        PurchaseTranDetailId = d.TRANDID,
                        MaterialId = d.TRANDREFID,
                        MaterialName = d.TRANDREFNAME,
                        HsnCode = hsnCode,
                        BatchNo = batch != null ? batch.TRANBDNO : null,
                        ExpiryDate = batch != null ? batch.TRANBEXPDATE : null,
                        PackingName = packingName,
                        PackingId = packingId,
                        BoxQty = boxQty,
                        Qty = qty,
                        Rate = rate,
                        Ptr = ptr,
                        Mrp = mrp,
                        Amount = amount,
                        CgstRate = cgstRate,
                        SgstRate = sgstRate,
                        IgstRate = igstRate,
                        Selected = true
                    };
                })
                .ToList();

            var grossAmount = items.Sum(i => i.Amount);
            decimal cgstAmount = 0m;
            decimal sgstAmount = 0m;
            decimal igstAmount = 0m;

            foreach (var itm in items)
            {
                if (customerStateType == 0)
                {
                    if (itm.CgstRate > 0)
                    {
                        cgstAmount += Math.Round((itm.Amount * itm.CgstRate) / 100m, 2);
                    }

                    if (itm.SgstRate > 0)
                    {
                        sgstAmount += Math.Round((itm.Amount * itm.SgstRate) / 100m, 2);
                    }
                }
                else
                {
                    if (itm.IgstRate > 0)
                    {
                        igstAmount += Math.Round((itm.Amount * itm.IgstRate) / 100m, 2);
                    }
                }
            }

            var netAmount = grossAmount + cgstAmount + sgstAmount + igstAmount;

            var model = new SalesInvoiceFromPurchaseViewModel
            {
                PurchaseTranMid = purchase.TRANMID,
                SalesTranMid = 0,
                PurchaseInvoiceNo = purchase.TRANDNO,
                PurchaseDate = DateTime.Today,
                RefNo = purchase.TRANREFNO,
                PoNumber = poNumber,
                CreditDays = creditDays,
                CustomerName = customerName,
                CustomerAddress1 = customerAddr1,
                CustomerAddress2 = customerAddr2,
                CustomerCity = customerCity,
                CustomerState = customerState,
                CustomerPincode = customerPincode,
                CustomerGstNo = customerGstNo,
                StateType = customerStateType,
                TotalAmount = grossAmount,
                GrossAmount = grossAmount,
                CgstAmount = cgstAmount,
                SgstAmount = sgstAmount,
                IgstAmount = igstAmount,
                NetAmount = netAmount,
                Status = 0,
                Remarks = null,
                Items = items
            };

            return View("CreateFromPurchase", model);
        }

        [HttpGet]
        [Authorize(Roles = "SalesInvoiceIndex")]
        public JsonResult GetAjaxData(JQueryDataTableParamModel param, string fromDate = null, string toDate = null)
        {
            try
            {
                var query = db.TransactionMasters.Where(t => t.REGSTRID == SalesInvoiceRegisterId);

                if (!string.IsNullOrWhiteSpace(fromDate))
                {
                    DateTime fd;
                    if (DateTime.TryParse(fromDate, out fd))
                    {
                        query = query.Where(t => t.TRANDATE >= fd);
                    }
                }

                if (!string.IsNullOrWhiteSpace(toDate))
                {
                    DateTime td;
                    if (DateTime.TryParse(toDate, out td))
                    {
                        var exclusiveTo = td.Date.AddDays(1);
                        query = query.Where(t => t.TRANDATE < exclusiveTo);
                    }
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
                        TRANDNO = t.TRANDNO ?? "0000",
                        // Show edited Tax Bill No (TRANTAXBILLNO) if available,
                        // otherwise fall back to the legacy TRANREFNO value
                        TRANREFNO = !string.IsNullOrWhiteSpace(t.TRANTAXBILLNO)
                            ? t.TRANTAXBILLNO
                            : (t.TRANREFNO ?? "-"),
                        CustomerName = t.TRANREFNAME ?? string.Empty,
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

        [HttpPost]
        public JsonResult DeleteInvoice(int id)
        {
            try
            {
                if (!User.IsInRole("SalesInvoiceDelete"))
                {
                    return Json(new { success = false, message = "Access Denied: You do not have permission to delete records. Please contact your administrator." });
                }

                var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == SalesInvoiceRegisterId);
                if (master == null)
                {
                    return Json(new { success = false, message = "Sales Invoice not found." });
                }

                db.Database.ExecuteSqlCommand(
                    "DELETE FROM TRANSACTIONDETAIL WHERE TRANMID = @p0",
                    id);

                db.Database.ExecuteSqlCommand(
                    "DELETE FROM TRANSACTIONMASTER WHERE TRANMID = @p0 AND REGSTRID = @p1",
                    id,
                    SalesInvoiceRegisterId);

                return Json(new { success = true, message = "Sales Invoice deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error deleting invoice: " + ex.Message });
            }
        }

        [Authorize(Roles = "SalesInvoiceEdit")]
        public ActionResult Form(int id)
        {
            var sales = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == SalesInvoiceRegisterId);
            if (sales == null)
            {
                TempData["ErrorMessage"] = "Sales Invoice not found.";
                return RedirectToAction("Index");
            }

            TransactionMaster purchase = null;
            if (sales.TRANLMID > 0)
            {
                purchase = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == sales.TRANLMID && t.REGSTRID == PurchaseInvoiceRegisterId);
            }

            string customerName = sales.TRANREFNAME;
            string customerAddr1 = string.Empty;
            string customerAddr2 = string.Empty;
            string customerCity = string.Empty;
            string customerState = string.Empty;
            string customerPincode = string.Empty;
            string customerGstNo = string.Empty;

            if (sales.TRANREFID > 0)
            {
                // Primary: try to load customer directly from Sales Invoice reference (same as Print action)
                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == sales.TRANREFID);
                if (customer != null)
                {
                    customerName = customer.CATENAME;
                    customerAddr1 = customer.CATEADDR1;
                    customerAddr2 = customer.CATEADDR2;
                    customerPincode = customer.CATEADDR5;

                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
                    if (state != null)
                    {
                        customerState = state.STATEDESC;
                    }

                    var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == customer.LOCTID);
                    if (location != null)
                    {
                        customerCity = location.LOCTDESC;
                    }

                    customerGstNo = customer.CATE_GST_NO;
                }
            }

            // Fallback: if customer address is still empty but a Purchase is linked,
            // reuse the same resolution logic as CreateFromPurchase (PI -> PO -> SO -> Customer, then Supplier).
            if (string.IsNullOrWhiteSpace(customerAddr1) && purchase != null)
            {
                string fallbackName = string.Empty;
                string fallbackAddr1 = string.Empty;
                string fallbackAddr2 = string.Empty;
                string fallbackCity = string.Empty;
                string fallbackState = string.Empty;
                string fallbackPincode = string.Empty;
                string fallbackGstNo = string.Empty;

                TransactionMaster purchaseOrder = null;
                TransactionMaster salesOrder = null;

                if (purchase.TRANLMID > 0)
                {
                    purchaseOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchase.TRANLMID);
                }

                if (purchaseOrder != null && purchaseOrder.TRANLMID > 0)
                {
                    salesOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchaseOrder.TRANLMID);
                }

                if (salesOrder != null && salesOrder.TRANREFID > 0)
                {
                    var soCustomer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == salesOrder.TRANREFID && c.DISPSTATUS == 0);
                    if (soCustomer != null)
                    {
                        fallbackName = soCustomer.CATENAME;
                        fallbackAddr1 = soCustomer.CATEADDR1;
                        fallbackAddr2 = soCustomer.CATEADDR2;
                        fallbackPincode = soCustomer.CATEADDR5;

                        var state = db.StateMasters.FirstOrDefault(s => s.STATEID == soCustomer.STATEID);
                        if (state != null)
                        {
                            fallbackState = state.STATEDESC;
                        }

                        var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == soCustomer.LOCTID);
                        if (location != null)
                        {
                            fallbackCity = location.LOCTDESC;
                        }

                        fallbackGstNo = soCustomer.CATE_GST_NO;
                    }
                }

                // Fallback to supplier from Purchase Invoice if still not resolved
                if (string.IsNullOrWhiteSpace(fallbackAddr1))
                {
                    var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == purchase.TRANREFID);
                    if (supplier != null)
                    {
                        fallbackName = supplier.CATENAME;
                        fallbackAddr1 = supplier.CATEADDR1;
                        fallbackAddr2 = supplier.CATEADDR2;
                        fallbackPincode = supplier.CATEADDR5;
                        fallbackGstNo = supplier.CATE_GST_NO;

                        var state = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);
                        if (state != null)
                        {
                            fallbackState = state.STATEDESC;
                        }

                        var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == supplier.LOCTID);
                        if (location != null)
                        {
                            fallbackCity = location.LOCTDESC;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(fallbackName))
                {
                    customerName = fallbackName;
                }

                if (!string.IsNullOrWhiteSpace(fallbackAddr1))
                {
                    customerAddr1 = fallbackAddr1;
                    customerAddr2 = fallbackAddr2;
                    customerCity = fallbackCity;
                    customerState = fallbackState;
                    customerPincode = fallbackPincode;
                    customerGstNo = fallbackGstNo;
                }
            }

            // Final fallback: if address is still empty, try to match customer by name
            // using the sales.TRANREFNAME text (useful for legacy invoices without TRANREFID/links).
            if (string.IsNullOrWhiteSpace(customerAddr1) && !string.IsNullOrWhiteSpace(customerName))
            {
                var nameCustomer = db.CustomerMasters.FirstOrDefault(c => c.CATENAME == customerName);
                if (nameCustomer != null)
                {
                    customerAddr1 = nameCustomer.CATEADDR1;
                    customerAddr2 = nameCustomer.CATEADDR2;
                    customerPincode = nameCustomer.CATEADDR5;

                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == nameCustomer.STATEID);
                    if (state != null)
                    {
                        customerState = state.STATEDESC;
                    }

                    var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == nameCustomer.LOCTID);
                    if (location != null)
                    {
                        customerCity = location.LOCTDESC;
                    }

                    customerGstNo = nameCustomer.CATE_GST_NO;
                }
            }

            string purchaseInvoiceNo = purchase != null ? purchase.TRANDNO : string.Empty;
            // Prefer values saved on the Sales Invoice itself (new columns) and
            // only fall back to purchase-based logic for legacy data.
            string poNumber = string.IsNullOrWhiteSpace(sales.TRANPONUM) ? null : sales.TRANPONUM;
            string refNo = !string.IsNullOrWhiteSpace(sales.TRANTAXBILLNO) ? sales.TRANTAXBILLNO : sales.TRANREFNO;

            if (string.IsNullOrWhiteSpace(poNumber) && purchase != null && purchase.TRANLMID > 0)
            {
                try
                {
                    var poResult = db.Database.SqlQuery<PoNumberResultLocal>(
                        "EXEC PR_PONODETAILS_PUR_INV @Tranlmid",
                        new System.Data.SqlClient.SqlParameter("@Tranlmid", purchase.TRANLMID)
                    ).FirstOrDefault();

                    if (poResult != null && !string.IsNullOrWhiteSpace(poResult.POREFNO))
                    {
                        poNumber = poResult.POREFNO;
                    }
                }
                catch
                {
                    poNumber = purchase.TRANREFNO;
                }
            }

            var salesDetails = db.TransactionDetails
                .Where(d => d.TRANMID == sales.TRANMID)
                .OrderBy(d => d.TRANDID)
                .ToList();

            var items = new List<SalesInvoiceFromPurchaseItemViewModel>();

            if (purchase != null)
            {
                var purchaseDetails = db.TransactionDetails
                    .Where(d => d.TRANMID == purchase.TRANMID)
                    .OrderBy(d => d.TRANDID)
                    .ToList();

                var detailIds = purchaseDetails.Select(d => d.TRANDID).ToList();

                var batchInfos = new List<PurchaseBatchInfoLocal>();
                if (detailIds.Any())
                {
                    var idList = string.Join(",", detailIds);
                    var sql = @"SELECT TRANDID, TRANBDNO, TRANBEXPDATE, PACKMID, TRANBQTY, TRANBPTRRATE, TRANBMRP 
                             FROM TRANSACTIONBATCHDETAIL WHERE TRANDID IN (" + idList + ")";
                    batchInfos = db.Database.SqlQuery<PurchaseBatchInfoLocal>(sql).ToList();
                }

                var materialIds = purchaseDetails.Select(d => d.TRANDREFID).Distinct().ToList();
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

                var packIds = batchInfos
                    .Where(b => b.PACKMID.HasValue)
                    .Select(b => b.PACKMID.Value)
                    .Distinct()
                    .ToList();

                var packingMap = db.PackingMasters
                    .Where(p => packIds.Contains(p.PACKMID))
                    .ToDictionary(p => p.PACKMID, p => p);

                int count = Math.Min(salesDetails.Count, purchaseDetails.Count);
                for (int i = 0; i < count; i++)
                {
                    var s = salesDetails[i];
                    var p = purchaseDetails[i];
                    var batch = batchInfos.FirstOrDefault(b => b.TRANDID == p.TRANDID);

                    materials.TryGetValue(p.TRANDREFID, out var material);
                    string hsnCode = null;
                    decimal cgstRate = 0m;
                    decimal sgstRate = 0m;
                    decimal igstRate = 0m;
                    if (material != null && material.HSNID > 0 && hsnMap.TryGetValue(material.HSNID, out var hsn))
                    {
                        hsnCode = hsn.HSNCODE;
                        cgstRate = hsn.CGSTEXPRN;
                        sgstRate = hsn.SGSTEXPRN;
                        igstRate = hsn.IGSTEXPRN;
                    }

                    string packingName = null;
                    int? packingId = null;
                    decimal boxQty = 0m;
                    decimal ptr = 0m;
                    decimal mrp = 0m;

                    if (batch != null)
                    {
                        if (batch.PACKMID.HasValue && packingMap.TryGetValue(batch.PACKMID.Value, out var pack))
                        {
                            packingName = pack.PACKMDESC;
                            packingId = pack.PACKMID;
                        }

                        boxQty = batch.TRANBQTY;
                        ptr = batch.TRANBPTRRATE;
                        mrp = batch.TRANBMRP;
                    }

                    decimal gross = s.TRANDGAMT > 0
                        ? s.TRANDGAMT
                        : (s.TRANDNAMT > 0 ? s.TRANDNAMT : s.TRANDQTY * s.TRANDRATE);

                    items.Add(new SalesInvoiceFromPurchaseItemViewModel
                    {
                        PurchaseTranDetailId = p.TRANDID,
                        MaterialId = s.TRANDREFID,
                        MaterialName = s.TRANDREFNAME,
                        HsnCode = hsnCode,
                        BatchNo = batch != null ? batch.TRANBDNO : null,
                        ExpiryDate = batch != null ? batch.TRANBEXPDATE : null,
                        PackingName = packingName,
                        PackingId = packingId,
                        BoxQty = boxQty,
                        Qty = s.TRANDQTY,
                        Rate = s.TRANDRATE,
                        Ptr = ptr,
                        Mrp = mrp,
                        Amount = gross,
                        CgstRate = cgstRate,
                        SgstRate = sgstRate,
                        IgstRate = igstRate,
                        Selected = true
                    });
                }
            }
            else
            {
                var materialIds = salesDetails.Select(d => d.TRANDREFID).Distinct().ToList();
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

                foreach (var s in salesDetails)
                {
                    materials.TryGetValue(s.TRANDREFID, out var material);
                    string hsnCode = null;
                    decimal cgstRate = 0m;
                    decimal sgstRate = 0m;
                    decimal igstRate = 0m;
                    if (material != null && material.HSNID > 0 && hsnMap.TryGetValue(material.HSNID, out var hsn))
                    {
                        hsnCode = hsn.HSNCODE;
                        cgstRate = hsn.CGSTEXPRN;
                        sgstRate = hsn.SGSTEXPRN;
                        igstRate = hsn.IGSTEXPRN;
                    }

                    decimal gross = s.TRANDGAMT > 0
                        ? s.TRANDGAMT
                        : (s.TRANDNAMT > 0 ? s.TRANDNAMT : s.TRANDQTY * s.TRANDRATE);

                    items.Add(new SalesInvoiceFromPurchaseItemViewModel
                    {
                        PurchaseTranDetailId = 0,
                        MaterialId = s.TRANDREFID,
                        MaterialName = s.TRANDREFNAME,
                        HsnCode = hsnCode,
                        Qty = s.TRANDQTY,
                        Rate = s.TRANDRATE,
                        Amount = gross,
                        CgstRate = cgstRate,
                        SgstRate = sgstRate,
                        IgstRate = igstRate,
                        Selected = true
                    });
                }
            }

            short tranStateType = sales.TRANSTATETYPE;

            decimal grossAmount = 0m;
            decimal cgstAmount = 0m;
            decimal sgstAmount = 0m;
            decimal igstAmount = 0m;

            foreach (var itm in items)
            {
                var amt = itm.Amount;
                grossAmount += amt;

                if (tranStateType == 0)
                {
                    if (itm.CgstRate > 0)
                    {
                        cgstAmount += Math.Round((amt * itm.CgstRate) / 100m, 2);
                    }

                    if (itm.SgstRate > 0)
                    {
                        sgstAmount += Math.Round((amt * itm.SgstRate) / 100m, 2);
                    }
                }
                else
                {
                    if (itm.IgstRate > 0)
                    {
                        igstAmount += Math.Round((amt * itm.IgstRate) / 100m, 2);
                    }
                }
            }

            decimal netAmount = grossAmount + cgstAmount + sgstAmount + igstAmount;

            string taxFactorsJson = null;
            try
            {
                var factorRows = db.Database.SqlQuery<SalesInvoiceManualFactorRow>(@"
                        SELECT CFID, DEDEXPRN, DEDMODE, DEDTYPE, DEDVALUE
                        FROM TRANSACTIONMASTERFACTOR
                        WHERE TRANMID = @p0
                        ORDER BY DEDORDR, TRANMFID",
                        sales.TRANMID).ToList();

                if (factorRows != null && factorRows.Count > 0)
                {
                    var taxInputs = factorRows.Select(f => new SalesInvoiceTaxFactorInput
                    {
                        CostFactorId = f.CFID,
                        ExpressionType = f.DEDTYPE == 1 ? "Value" : "%",
                        Mode = (f.DEDMODE != null && f.DEDMODE.Trim() == "1") ? "-" : "+",
                        ExpressionValue = f.DEDEXPRN,
                        Amount = f.DEDVALUE
                    }).ToList();

                    taxFactorsJson = JsonConvert.SerializeObject(taxInputs);
                }
            }
            catch
            {
            }

            var model = new SalesInvoiceFromPurchaseViewModel
            {
                PurchaseTranMid = purchase != null ? purchase.TRANMID : 0,
                SalesTranMid = sales.TRANMID,
                SalesInvoiceNo = sales.TRANDNO ?? sales.TRANNO.ToString("D4"),
                PurchaseInvoiceNo = purchaseInvoiceNo,
                PurchaseDate = sales.TRANDATE,
                RefNo = refNo,
                PoNumber = poNumber,
                CreditDays = sales.TRAN_CRDPRDT,
                CustomerName = customerName,
                CustomerAddress1 = customerAddr1,
                CustomerAddress2 = customerAddr2,
                CustomerCity = customerCity,
                CustomerState = customerState,
                CustomerPincode = customerPincode,
                CustomerGstNo = customerGstNo,
                StateType = tranStateType,
                TotalAmount = grossAmount,
                GrossAmount = grossAmount,
                CgstAmount = cgstAmount,
                SgstAmount = sgstAmount,
                IgstAmount = igstAmount,
                NetAmount = netAmount,
                TaxFactorsJson = taxFactorsJson,
                Status = sales.DISPSTATUS,
                Remarks = sales.TRANRMKS,
                Items = items
            };

            ViewBag.StatusList = new SelectList(
                new[]
                {
                    new { Value = "0", Text = "Enabled" },
                    new { Value = "1", Text = "Disabled" }
                },
                "Value",
                "Text",
                model.Status.ToString());

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SalesInvoiceEdit")]
        public ActionResult Form(SalesInvoiceFromPurchaseViewModel model)
        {
            try
            {
                if (model == null || model.SalesTranMid <= 0)
                {
                    TempData["ErrorMessage"] = "Invalid Sales Invoice.";
                    return RedirectToAction("Index");
                }

                var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == model.SalesTranMid && t.REGSTRID == SalesInvoiceRegisterId);
                if (existing == null)
                {
                    TempData["ErrorMessage"] = "Sales Invoice not found.";
                    return RedirectToAction("Index");
                }

                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";

                // Ensure TRANREFID is populated with a valid customer id when possible.
                // If it was never set (legacy data) but we have a customer name, try to resolve it.
                int customerId = existing.TRANREFID;
                if (customerId <= 0 && !string.IsNullOrWhiteSpace(model.CustomerName))
                {
                    var customer = db.CustomerMasters
                        .FirstOrDefault(c => c.DISPSTATUS == 0 && c.CATENAME == model.CustomerName);
                    if (customer != null)
                    {
                        customerId = customer.CATEID;
                    }
                }

                existing.TRANDATE = model.PurchaseDate;
                existing.TRANTIME = DateTime.Now;
                existing.DISPSTATUS = model.Status;
                existing.TRANRMKS = model.Remarks;
                existing.TRAN_CRDPRDT = model.CreditDays;
                existing.TRANPONUM = model.PoNumber;
                existing.TRANTAXBILLNO = model.RefNo;
                if (customerId > 0)
                {
                    existing.TRANREFID = customerId;
                    existing.TRANREFNAME = model.CustomerName;
                }
                existing.LMUSRID = userName;
                existing.PRCSDATE = DateTime.Now;

                var details = db.TransactionDetails
                    .Where(d => d.TRANMID == existing.TRANMID)
                    .OrderBy(d => d.TRANDID)
                    .ToList();

                short tranStateType = existing.TRANSTATETYPE;

                var materialIds = details.Select(d => d.TRANDREFID).Distinct().ToList();
                var materialMap = db.MaterialMasters
                    .Where(m => materialIds.Contains(m.MTRLID))
                    .ToDictionary(m => m.MTRLID, m => m);

                var hsnIds = materialMap.Values
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

                for (int i = 0; i < details.Count && i < (model.Items != null ? model.Items.Count : 0); i++)
                {
                    var d = details[i];
                    var itm = model.Items[i];

                    decimal qty = itm.Qty;
                    decimal rate = itm.Rate;
                    decimal gross = Math.Round(qty * rate, 2);

                    materialMap.TryGetValue(d.TRANDREFID, out var material);
                    int hsnId = d.HSNID;
                    if (material != null && material.HSNID > 0)
                    {
                        hsnId = material.HSNID;
                    }

                    hsnMap.TryGetValue(hsnId, out var hsn);

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

                    d.TRANDQTY = qty;
                    d.TRANDRATE = rate;
                    d.HSNID = hsnId;
                    d.TRANDGAMT = gross;
                    d.TRANDCGSTAMT = cgstAmt;
                    d.TRANDSGSTAMT = sgstAmt;
                    d.TRANDIGSTAMT = igstAmt;
                    d.TRANDNAMT = net;

                    totalGross += gross;
                    totalCgst += cgstAmt;
                    totalSgst += sgstAmt;
                    totalIgst += igstAmt;
                    totalNet += net;
                }

                existing.TRANGAMT = totalGross;
                existing.TRANCGSTAMT = totalCgst;
                existing.TRANSGSTAMT = totalSgst;
                existing.TRANIGSTAMT = totalIgst;
                existing.TRANNAMT = totalNet;
                existing.TRANAMTWRDS = ConvertAmountToWords(totalNet);

                db.SaveChanges();

                try
                {
                    var taxInputs = new List<SalesInvoiceTaxFactorInput>();
                    if (!string.IsNullOrWhiteSpace(model.TaxFactorsJson))
                    {
                        try
                        {
                            taxInputs = JsonConvert.DeserializeObject<List<SalesInvoiceTaxFactorInput>>(model.TaxFactorsJson)
                                        ?? new List<SalesInvoiceTaxFactorInput>();
                        }
                        catch
                        {
                            taxInputs = new List<SalesInvoiceTaxFactorInput>();
                        }
                    }

                    if (taxInputs != null && taxInputs.Count > 0)
                    {
                        db.Database.ExecuteSqlCommand(
                            "DELETE FROM TRANSACTIONMASTERFACTOR WHERE TRANMID = @p0",
                            existing.TRANMID);

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
                                existing.TRANMID,
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
                            existing.TRANNAMT = totalNet + manualTotal;
                            existing.TRANAMTWRDS = ConvertAmountToWords(existing.TRANNAMT);
                            db.SaveChanges();
                        }
                    }
                    else
                    {
                        db.Database.ExecuteSqlCommand(
                            "DELETE FROM TRANSACTIONMASTERFACTOR WHERE TRANMID = @p0",
                            existing.TRANMID);
                    }
                }
                catch
                {
                }

                TempData["SuccessMessage"] = "Sales Invoice updated successfully.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        [Authorize(Roles = "SalesInvoicePrint")]
        public ActionResult Print(int id)
        {
            try
            {
                var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == SalesInvoiceRegisterId);
                if (master == null)
                {
                    TempData["ErrorMessage"] = "Sales Invoice not found.";
                    return RedirectToAction("Index");
                }

                // Resolve primary customer (direct link from Sales Invoice)
                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == master.TRANREFID);
                LocationMaster location = null;
                StateMaster state = null;

                string customerName = master.TRANREFNAME;
                string customerAddr1 = string.Empty;
                string customerAddr2 = string.Empty;
                string customerAddr3 = string.Empty;
                string customerAddr4 = string.Empty;
                string customerCity = string.Empty;
                string customerState = string.Empty;
                string customerPincode = string.Empty;
                string customerStateCode = string.Empty;
                string customerGstNo = string.Empty;
                string customerPhone = string.Empty;
                string customerDlNo = string.Empty;

                if (customer != null)
                {
                    customerName = customer.CATENAME;
                    customerAddr1 = customer.CATEADDR1;
                    customerAddr2 = customer.CATEADDR2;
                    customerAddr3 = customer.CATEADDR3;
                    customerAddr4 = customer.CATEADDR4;
                    customerPincode = customer.CATEADDR5;

                    location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == customer.LOCTID);
                    if (location != null)
                    {
                        customerCity = location.LOCTDESC;
                    }

                    state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
                    if (state != null)
                    {
                        customerState = state.STATEDESC;
                        customerStateCode = state.STATECODE;
                    }

                    customerGstNo = customer.CATE_GST_NO;

                    var phoneParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(customer.CATEPHN1)) phoneParts.Add(customer.CATEPHN1);
                    if (!string.IsNullOrWhiteSpace(customer.CATEPHN2)) phoneParts.Add(customer.CATEPHN2);
                    if (!string.IsNullOrWhiteSpace(customer.CATEPHN3)) phoneParts.Add(customer.CATEPHN3);
                    if (!string.IsNullOrWhiteSpace(customer.CATEPHN4)) phoneParts.Add(customer.CATEPHN4);
                    if (phoneParts.Count > 0)
                    {
                        customerPhone = string.Join(",", phoneParts);
                    }

                    var dlParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(customer.CATE_PEST_LIC_NO)) dlParts.Add(customer.CATE_PEST_LIC_NO);
                    if (!string.IsNullOrWhiteSpace(customer.CATE_SEED_LIC_NO)) dlParts.Add(customer.CATE_SEED_LIC_NO);
                    if (dlParts.Count > 0)
                    {
                        customerDlNo = string.Join(",", dlParts);
                    }
                }

                // Load Sales Invoice details
                var details = db.TransactionDetails
                    .Where(d => d.TRANMID == master.TRANMID)
                    .OrderBy(d => d.TRANDID)
                    .ToList();

                // Resolve linked Purchase Invoice and Sales Order using TRANLMID chain
                string salesOrderNo = string.Empty;
                DateTime? salesOrderDate = null;
                string purchaseInvoiceNo = string.Empty;
                DateTime? purchaseInvoiceDate = null;

                TransactionMaster purchase = null;
                TransactionMaster purchaseOrder = null;
                TransactionMaster salesOrder = null;

                if (master.TRANLMID > 0)
                {
                    purchase = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANLMID && t.REGSTRID == PurchaseInvoiceRegisterId);
                    if (purchase != null)
                    {
                        purchaseInvoiceNo = purchase.TRANDNO;
                        purchaseInvoiceDate = purchase.TRANDATE;

                        if (purchase.TRANLMID > 0)
                        {
                            purchaseOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchase.TRANLMID);
                        }

                        if (purchaseOrder != null && purchaseOrder.TRANLMID > 0)
                        {
                            salesOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchaseOrder.TRANLMID);
                        }

                        if (salesOrder != null)
                        {
                            // Use Sales Order TRANREFNO if available, otherwise fall back to TRANDNO
                            salesOrderNo = !string.IsNullOrWhiteSpace(salesOrder.TRANREFNO) && salesOrder.TRANREFNO != "-"
                                ? salesOrder.TRANREFNO
                                : salesOrder.TRANDNO;
                            salesOrderDate = salesOrder.TRANDATE;
                        }
                    }
                }

                // Fallback resolution for customer address/GST if direct link is missing (same logic as Form action)
                if (string.IsNullOrWhiteSpace(customerAddr1) && purchase != null)
                {
                    string fallbackName = string.Empty;
                    string fallbackAddr1 = string.Empty;
                    string fallbackAddr2 = string.Empty;
                    string fallbackCity = string.Empty;
                    string fallbackState = string.Empty;
                    string fallbackPincode = string.Empty;
                    string fallbackGstNo = string.Empty;
                    string fallbackStateCode = string.Empty;
                    string fallbackPhone = string.Empty;
                    string fallbackDlNo = string.Empty;

                    if (salesOrder == null && purchaseOrder != null && purchaseOrder.TRANLMID > 0)
                    {
                        salesOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchaseOrder.TRANLMID);
                    }

                    if (salesOrder != null && salesOrder.TRANREFID > 0)
                    {
                        var soCustomer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == salesOrder.TRANREFID && c.DISPSTATUS == 0);
                        if (soCustomer != null)
                        {
                            fallbackName = soCustomer.CATENAME;
                            fallbackAddr1 = soCustomer.CATEADDR1;
                            fallbackAddr2 = soCustomer.CATEADDR2;
                            fallbackPincode = soCustomer.CATEADDR5;

                            var stateLocal = db.StateMasters.FirstOrDefault(s => s.STATEID == soCustomer.STATEID);
                            if (stateLocal != null)
                            {
                                fallbackState = stateLocal.STATEDESC;
                                fallbackStateCode = stateLocal.STATECODE;
                            }

                            var locationLocal = db.LocationMasters.FirstOrDefault(l => l.LOCTID == soCustomer.LOCTID);
                            if (locationLocal != null)
                            {
                                fallbackCity = locationLocal.LOCTDESC;
                            }

                            fallbackGstNo = soCustomer.CATE_GST_NO;

                            var phoneParts = new List<string>();
                            if (!string.IsNullOrWhiteSpace(soCustomer.CATEPHN1)) phoneParts.Add(soCustomer.CATEPHN1);
                            if (!string.IsNullOrWhiteSpace(soCustomer.CATEPHN2)) phoneParts.Add(soCustomer.CATEPHN2);
                            if (!string.IsNullOrWhiteSpace(soCustomer.CATEPHN3)) phoneParts.Add(soCustomer.CATEPHN3);
                            if (!string.IsNullOrWhiteSpace(soCustomer.CATEPHN4)) phoneParts.Add(soCustomer.CATEPHN4);
                            if (phoneParts.Count > 0)
                            {
                                fallbackPhone = string.Join(",", phoneParts);
                            }

                            var dlParts = new List<string>();
                            if (!string.IsNullOrWhiteSpace(soCustomer.CATE_PEST_LIC_NO)) dlParts.Add(soCustomer.CATE_PEST_LIC_NO);
                            if (!string.IsNullOrWhiteSpace(soCustomer.CATE_SEED_LIC_NO)) dlParts.Add(soCustomer.CATE_SEED_LIC_NO);
                            if (dlParts.Count > 0)
                            {
                                fallbackDlNo = string.Join(",", dlParts);
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(fallbackAddr1))
                    {
                        var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == purchase.TRANREFID);
                        if (supplier != null)
                        {
                            fallbackName = supplier.CATENAME;
                            fallbackAddr1 = supplier.CATEADDR1;
                            fallbackAddr2 = supplier.CATEADDR2;
                            fallbackPincode = supplier.CATEADDR5;
                            fallbackGstNo = supplier.CATE_GST_NO;

                            var stateLocal = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);
                            if (stateLocal != null)
                            {
                                fallbackState = stateLocal.STATEDESC;
                                fallbackStateCode = stateLocal.STATECODE;
                            }

                            var locationLocal = db.LocationMasters.FirstOrDefault(l => l.LOCTID == supplier.LOCTID);
                            if (locationLocal != null)
                            {
                                fallbackCity = locationLocal.LOCTDESC;
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(fallbackName))
                    {
                        customerName = fallbackName;
                    }

                    if (!string.IsNullOrWhiteSpace(fallbackAddr1))
                    {
                        customerAddr1 = fallbackAddr1;
                        customerAddr2 = fallbackAddr2;
                        customerCity = fallbackCity;
                        customerState = fallbackState;
                        customerPincode = fallbackPincode;
                        customerGstNo = fallbackGstNo;
                        customerStateCode = fallbackStateCode;
                        if (!string.IsNullOrWhiteSpace(fallbackPhone))
                        {
                            customerPhone = fallbackPhone;
                        }
                        if (!string.IsNullOrWhiteSpace(fallbackDlNo))
                        {
                            customerDlNo = fallbackDlNo;
                        }
                    }
                }

                // Final fallback: try to resolve customer by name if address is still empty
                if (string.IsNullOrWhiteSpace(customerAddr1) && !string.IsNullOrWhiteSpace(customerName))
                {
                    var nameCustomer = db.CustomerMasters.FirstOrDefault(c => c.CATENAME == customerName);
                    if (nameCustomer != null)
                    {
                        customerAddr1 = nameCustomer.CATEADDR1;
                        customerAddr2 = nameCustomer.CATEADDR2;
                        customerAddr3 = nameCustomer.CATEADDR3;
                        customerAddr4 = nameCustomer.CATEADDR4;
                        customerPincode = nameCustomer.CATEADDR5;

                        var stateLocal = db.StateMasters.FirstOrDefault(s => s.STATEID == nameCustomer.STATEID);
                        if (stateLocal != null)
                        {
                            customerState = stateLocal.STATEDESC;
                            customerStateCode = stateLocal.STATECODE;
                        }

                        var locationLocal = db.LocationMasters.FirstOrDefault(l => l.LOCTID == nameCustomer.LOCTID);
                        if (locationLocal != null)
                        {
                            customerCity = locationLocal.LOCTDESC;
                        }

                        customerGstNo = nameCustomer.CATE_GST_NO;

                        var phoneParts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(nameCustomer.CATEPHN1)) phoneParts.Add(nameCustomer.CATEPHN1);
                        if (!string.IsNullOrWhiteSpace(nameCustomer.CATEPHN2)) phoneParts.Add(nameCustomer.CATEPHN2);
                        if (!string.IsNullOrWhiteSpace(nameCustomer.CATEPHN3)) phoneParts.Add(nameCustomer.CATEPHN3);
                        if (!string.IsNullOrWhiteSpace(nameCustomer.CATEPHN4)) phoneParts.Add(nameCustomer.CATEPHN4);
                        if (phoneParts.Count > 0)
                        {
                            customerPhone = string.Join(",", phoneParts);
                        }

                        var dlParts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(nameCustomer.CATE_PEST_LIC_NO)) dlParts.Add(nameCustomer.CATE_PEST_LIC_NO);
                        if (!string.IsNullOrWhiteSpace(nameCustomer.CATE_SEED_LIC_NO)) dlParts.Add(nameCustomer.CATE_SEED_LIC_NO);
                        if (dlParts.Count > 0)
                        {
                            customerDlNo = string.Join(",", dlParts);
                        }
                    }
                }

                // Build detailed item rows for print (including Div, Pack, Batch, Exp, HSN, PTR, MRP, SGST/CGST etc.)
                var printItems = new List<SalesInvoicePrintItemViewModel>();

                // Prepare maps for materials, groups, HSN, packing and batch when purchase is linked
                Dictionary<int, MaterialMaster> materialMap = null;
                Dictionary<int, MaterialGroupMaster> groupMap = null;
                Dictionary<int, HSNCodeMaster> hsnMap = null;
                Dictionary<int, PackingMaster> packMap = null;
                List<PurchaseBatchInfoLocal> batchInfos = null;
                List<TransactionDetail> purchaseDetails = null;

                if (purchase != null)
                {
                    purchaseDetails = db.TransactionDetails
                        .Where(d => d.TRANMID == purchase.TRANMID)
                        .OrderBy(d => d.TRANDID)
                        .ToList();

                    var purchaseDetailIds = purchaseDetails.Select(d => d.TRANDID).ToList();

                    batchInfos = new List<PurchaseBatchInfoLocal>();
                    if (purchaseDetailIds.Any())
                    {
                        var idList = string.Join(",", purchaseDetailIds);
                        var sql = @"SELECT TRANDID, TRANBDNO, TRANBEXPDATE, PACKMID, TRANBQTY, TRANBPTRRATE, TRANBMRP 
                             FROM TRANSACTIONBATCHDETAIL WHERE TRANDID IN (" + idList + ")";
                        batchInfos = db.Database.SqlQuery<PurchaseBatchInfoLocal>(sql).ToList();
                    }

                    var materialIds = purchaseDetails.Select(d => d.TRANDREFID).Distinct().ToList();
                    materialMap = db.MaterialMasters
                        .Where(m => materialIds.Contains(m.MTRLID))
                        .ToDictionary(m => m.MTRLID, m => m);

                    var groupIds = materialMap.Values.Select(m => m.MTRLGID).Distinct().ToList();
                    groupMap = db.MaterialGroupMasters
                        .Where(g => groupIds.Contains(g.MTRLGID))
                        .ToDictionary(g => g.MTRLGID, g => g);

                    var hsnIds = materialMap.Values
                        .Where(m => m.HSNID > 0)
                        .Select(m => m.HSNID)
                        .Distinct()
                        .ToList();

                    hsnMap = db.HSNCodeMasters
                        .Where(h => hsnIds.Contains(h.HSNID))
                        .ToDictionary(h => h.HSNID, h => h);

                    var packIds = batchInfos
                        .Where(b => b.PACKMID.HasValue)
                        .Select(b => b.PACKMID.Value)
                        .Distinct()
                        .ToList();

                    if (packIds.Any())
                    {
                        packMap = db.PackingMasters
                            .Where(p => packIds.Contains(p.PACKMID))
                            .ToDictionary(p => p.PACKMID, p => p);
                    }
                }

                for (int i = 0; i < details.Count; i++)
                {
                    var s = details[i];
                    TransactionDetail p = null;
                    PurchaseBatchInfoLocal batch = null;
                    MaterialMaster material = null;
                    HSNCodeMaster hsn = null;
                    MaterialGroupMaster group = null;
                    PackingMaster pack = null;

                    if (purchaseDetails != null && i < purchaseDetails.Count)
                    {
                        p = purchaseDetails[i];
                    }

                    if (p != null && batchInfos != null)
                    {
                        batch = batchInfos.FirstOrDefault(b => b.TRANDID == p.TRANDID);
                    }

                    int materialId = p != null ? p.TRANDREFID : s.TRANDREFID;

                    if (materialMap == null)
                    {
                        material = db.MaterialMasters.FirstOrDefault(m => m.MTRLID == materialId);
                    }
                    else
                    {
                        materialMap.TryGetValue(materialId, out material);
                    }

                    if (material != null)
                    {
                        if (hsnMap == null)
                        {
                            hsn = db.HSNCodeMasters.FirstOrDefault(h => h.HSNID == material.HSNID);
                        }
                        else if (material.HSNID > 0)
                        {
                            hsnMap.TryGetValue(material.HSNID, out hsn);
                        }

                        if (groupMap == null)
                        {
                            group = db.MaterialGroupMasters.FirstOrDefault(g => g.MTRLGID == material.MTRLGID);
                        }
                        else
                        {
                            groupMap.TryGetValue(material.MTRLGID, out group);
                        }
                    }

                    if (batch != null && packMap != null && batch.PACKMID.HasValue)
                    {
                        packMap.TryGetValue(batch.PACKMID.Value, out pack);
                    }
                    else if (s.PACKMID > 0)
                    {
                        pack = db.PackingMasters.FirstOrDefault(pck => pck.PACKMID == s.PACKMID);
                    }

                    decimal rate = s.TRANDRATE;
                    decimal qty = s.TRANDQTY;
                    decimal gross = s.TRANDGAMT > 0 ? s.TRANDGAMT : (qty * rate);
                    decimal net = s.TRANDNAMT > 0 ? s.TRANDNAMT : (gross + s.TRANDCGSTAMT + s.TRANDSGSTAMT + s.TRANDIGSTAMT);

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

                    printItems.Add(new SalesInvoicePrintItemViewModel
                    {
                        Division = group != null ? group.MTRLGDESC : string.Empty,
                        MaterialName = s.TRANDREFNAME,
                        Pack = pack != null ? pack.PACKMDESC : string.Empty,
                        Qty = qty,
                        BatchNo = batch != null ? batch.TRANBDNO : string.Empty,
                        ExpiryDate = batch != null ? batch.TRANBEXPDATE : (DateTime?)null,
                        HsnCode = hsn != null ? hsn.HSNCODE : string.Empty,
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

                // Build class-wise GST summary and overall item totals
                var classSummaryDict = new Dictionary<decimal, SalesInvoiceClassSummaryViewModel>();

                for (int i = 0; i < printItems.Count && i < details.Count; i++)
                {
                    var item = printItems[i];
                    var s = details[i];

                    decimal gstPercent = item.SgstRate + item.CgstRate;

                    if (!classSummaryDict.TryGetValue(gstPercent, out var summary))
                    {
                        summary = new SalesInvoiceClassSummaryViewModel
                        {
                            ClassName = $"GST {gstPercent.ToString("0.00")}%",
                            GstPercent = gstPercent,
                            Scheme = 0m,
                            Discount = 0m
                        };
                        classSummaryDict[gstPercent] = summary;
                    }

                    summary.Total += item.Amount;
                    summary.Sgst += s.TRANDSGSTAMT;
                    summary.Cgst += s.TRANDCGSTAMT;
                    summary.TotalGst = summary.Sgst + summary.Cgst;
                }

                var classSummaries = classSummaryDict.Values
                    .OrderBy(c => c.GstPercent)
                    .ToList();

                int totalItems = printItems.Count;
                decimal totalQty = printItems.Sum(x => x.Qty);

                decimal totalDisc = 0m;
                decimal courierCharges = 0m;
                try
                {
                    var factorRows = db.Database.SqlQuery<SalesInvoiceManualFactorRow>(@"
                            SELECT tmf.CFID, tmf.DEDEXPRN, tmf.DEDMODE, tmf.DEDTYPE, tmf.DEDVALUE
                            FROM TRANSACTIONMASTERFACTOR tmf
                            WHERE tmf.TRANMID = @p0
                            ORDER BY tmf.DEDORDR, tmf.TRANMFID",
                            master.TRANMID).ToList();

                    if (factorRows != null && factorRows.Count > 0)
                    {
                        var cfIds = factorRows.Select(f => f.CFID).Distinct().ToList();
                        var cfMap = db.CostFactorMasters
                            .Where(c => cfIds.Contains(c.CFID))
                            .ToDictionary(c => c.CFID, c => c);

                        foreach (var f in factorRows)
                        {
                            if (!cfMap.TryGetValue(f.CFID, out var cf))
                            {
                                continue;
                            }

                            var desc = (cf.CFDESC ?? string.Empty).Trim().ToUpper();

                            // Belongs to: 0 = Discount, 8 = Service Charge.
                            // Also fall back on description text so we can reliably split
                            // Discount and Courier Charges even if BelongsTo was configured differently.
                            bool isCourier = cf.DORDRID == 8 || desc.Contains("COURIER");
                            bool isDiscount = (cf.DORDRID == 0 || desc.Contains("DISCOUNT")) && !isCourier;

                            if (isDiscount)
                            {
                                totalDisc += f.DEDVALUE;
                            }

                            if (isCourier)
                            {
                                courierCharges += f.DEDVALUE;
                            }
                        }
                    }
                }
                catch
                {
                }

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

                var model = new SalesInvoicePrintViewModel
                {
                    TRANMID = master.TRANMID,
                    TRANNO = master.TRANNO,
                    TRANDNO = master.TRANDNO,
                    TRANREFNO = master.TRANREFNO,
                    TRANDATE = master.TRANDATE,
                    CreditDays = master.TRAN_CRDPRDT,
                    CustomerName = customerName,
                    CustomerCode = customer != null ? customer.CATECODE : string.Empty,
                    Address1 = customerAddr1,
                    Address2 = customerAddr2,
                    Address3 = customerAddr3,
                    Address4 = customerAddr4,
                    City = customerCity,
                    Pincode = customerPincode,
                    State = customerState,
                    StateCode = customerStateCode,
                    GstNo = customerGstNo,
                    CustomerPhone = customerPhone,
                    CustomerDlNo = customerDlNo,
                    GrossAmount = master.TRANGAMT,
                    NetAmount = master.TRANNAMT,
                    CgstAmount = master.TRANCGSTAMT,
                    SgstAmount = master.TRANSGSTAMT,
                    IgstAmount = master.TRANIGSTAMT,
                    SalesOrderNo = salesOrderNo,
                    SalesOrderDate = salesOrderDate,
                    PurchaseInvoiceNo = purchaseInvoiceNo,
                    PurchaseInvoiceDate = purchaseInvoiceDate,
                    // For print: Order No comes from Sales Invoice PO Number when set, else legacy SalesOrderNo
                    PoNumber = !string.IsNullOrWhiteSpace(master.TRANPONUM)
                        ? master.TRANPONUM
                        : salesOrderNo,
                    // For print: Ref No comes from Sales Invoice Tax Bill No when set,
                    // else the legacy TRANREFNO/TRANDNO logic
                    TaxBillNo = !string.IsNullOrWhiteSpace(master.TRANTAXBILLNO)
                        ? master.TRANTAXBILLNO
                        : ((string.IsNullOrWhiteSpace(master.TRANREFNO) || master.TRANREFNO == "-")
                            ? master.TRANDNO
                            : master.TRANREFNO),
                    AmountInWords = ConvertAmountToWords(master.TRANNAMT),
                    Items = printItems,
                    CompanyAddress = companyAddress,
                    CompanyName = companyName,
                    CompanyGstNo = companyGstNo,
                    ClassSummaries = classSummaries,
                    TotalItems = totalItems,
                    TotalQty = totalQty,
                    Remarks = master.TRANRMKS,
                    UserName = master.LMUSRID,
                    BillingTime = master.TRANTIME,
                    TotalDisc = totalDisc,
                    CourierCharges = courierCharges
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading Sales Invoice: " + ex.Message;
                return RedirectToAction("Index");
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
            // Use a five-digit running sequence (e.g. A00001) instead of four digits
            string runningPart = tranNo.ToString("D5");
            return fyPart + "/A" + runningPart;
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

                if (rupees > 0)
                {
                    words = NumberToWords(rupees, ones, teens, tens) + " Rupees";
                }

                if (paise > 0)
                {
                    if (!string.IsNullOrEmpty(words)) words += " and ";
                    words += NumberToWords(paise, ones, teens, tens) + " Paise";
                }

                words += " Only";
                return words;
            }
            catch
            {
                return amount.ToString("0.00");
            }
        }

        private string NumberToWords(int number, string[] ones, string[] teens, string[] tens)
        {
            if (number == 0) return "Zero";

            if (number < 0) return "Minus " + NumberToWords(Math.Abs(number), ones, teens, tens);

            string words = "";

            if ((number / 10000000) > 0)
            {
                words += NumberToWords(number / 10000000, ones, teens, tens) + " Crore ";
                number %= 10000000;
            }

            if ((number / 100000) > 0)
            {
                words += NumberToWords(number / 100000, ones, teens, tens) + " Lakh ";
                number %= 100000;
            }

            if ((number / 1000) > 0)
            {
                words += NumberToWords(number / 1000, ones, teens, tens) + " Thousand ";
                number %= 1000;
            }

            if ((number / 100) > 0)
            {
                words += NumberToWords(number / 100, ones, teens, tens) + " Hundred ";
                number %= 100;
            }

            if (number > 0)
            {
                if (number < 10)
                    words += ones[number];
                else if (number < 20)
                    words += teens[number - 10];
                else
                {
                    words += tens[number / 10];
                    if ((number % 10) > 0)
                        words += " " + ones[number % 10];
                }
            }

            return words.Trim();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SalesInvoiceCreate,SalesInvoiceEdit")]
        public ActionResult CreateFromPurchase(SalesInvoiceFromPurchaseViewModel model)
        {
            if (model == null || model.PurchaseTranMid <= 0)
            {
                TempData["ErrorMessage"] = "Invalid Sales Invoice data.";
                return RedirectToAction("Index", "PurchaseInvoice", new { area = "" });
            }

            var allItems = model.Items ?? new List<SalesInvoiceFromPurchaseItemViewModel>();

            // Keep original row index so we can reliably map back to purchase detail rows
            var selectedItems = allItems
                .Select((item, index) => new { Item = item, Index = index })
                .Where(x => x.Item.Selected && x.Item.MaterialId > 0 && x.Item.Qty > 0)
                .ToList();

            if (!selectedItems.Any())
            {
                TempData["ErrorMessage"] = "Please select at least one item for the Sales Invoice.";
                return RedirectToAction("CreateFromPurchase", new { id = model.PurchaseTranMid });
            }

            var purchase = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == model.PurchaseTranMid && t.REGSTRID == PurchaseInvoiceRegisterId);
            if (purchase == null)
            {
                TempData["ErrorMessage"] = "Purchase Invoice not found.";
                return RedirectToAction("Index", "PurchaseInvoice", new { area = "" });
            }

            // Load original purchase detail rows in the same order as the CreateFromPurchase GET action
            var purchaseDetailsForLink = db.TransactionDetails
                .Where(d => d.TRANMID == purchase.TRANMID)
                .OrderBy(d => d.TRANDID)
                .ToList();

            var compyObj = System.Web.HttpContext.Current.Session["CompyId"] ?? System.Web.HttpContext.Current.Session["compyid"];
            int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

            string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                ? User.Identity.Name
                : "System";

            var materialIds = selectedItems.Select(x => x.Item.MaterialId).Distinct().ToList();
            var materialMap = db.MaterialMasters
                .Where(m => materialIds.Contains(m.MTRLID))
                .ToDictionary(m => m.MTRLID, m => m);

            var hsnIds = materialMap.Values
                .Where(m => m.HSNID > 0)
                .Select(m => m.HSNID)
                .Distinct()
                .ToList();

            var hsnMap = db.HSNCodeMasters
                .Where(h => hsnIds.Contains(h.HSNID))
                .ToDictionary(h => h.HSNID, h => h);

            short tranStateType = purchase.TRANSTATETYPE;

            // Resolve customer id for TRANREFID: prefer Sales Order customer from PI -> PO -> SO chain,
            // and fall back to matching CustomerMaster by the posted CustomerName.
            int customerId = 0;

            try
            {
                TransactionMaster purchaseOrder = null;
                TransactionMaster salesOrder = null;

                if (purchase.TRANLMID > 0)
                {
                    purchaseOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchase.TRANLMID);
                }

                if (purchaseOrder != null && purchaseOrder.TRANLMID > 0)
                {
                    salesOrder = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == purchaseOrder.TRANLMID);
                }

                if (salesOrder != null && salesOrder.TRANREFID > 0)
                {
                    var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == salesOrder.TRANREFID && c.DISPSTATUS == 0);
                    if (customer != null)
                    {
                        customerId = customer.CATEID;
                    }
                }
            }
            catch
            {
                // Ignore resolution errors here; we will fall back to name-based resolution below.
            }

            if (customerId <= 0 && !string.IsNullOrWhiteSpace(model.CustomerName))
            {
                var customerByName = db.CustomerMasters
                    .FirstOrDefault(c => c.DISPSTATUS == 0 && c.CATENAME == model.CustomerName);
                if (customerByName != null)
                {
                    customerId = customerByName.CATEID;
                }
            }

            decimal totalGross = 0m;
            decimal totalCgst = 0m;
            decimal totalSgst = 0m;
            decimal totalIgst = 0m;
            decimal totalNet = 0m;

            var invoiceDate = model.PurchaseDate;
            if (invoiceDate == default(DateTime))
            {
                invoiceDate = DateTime.Today;
            }

            int fyStartYear;
            if (invoiceDate.Month >= 4)
            {
                fyStartYear = invoiceDate.Year;
            }
            else
            {
                fyStartYear = invoiceDate.Year - 1;
            }

            var fyStartDate = new DateTime(fyStartYear, 4, 1);
            var fyEndExclusive = fyStartDate.AddYears(1);

            var maxTranNo = db.TransactionMasters
                .Where(t => t.COMPYID == compyId
                            && t.REGSTRID == SalesInvoiceRegisterId
                            && t.TRANDATE >= fyStartDate
                            && t.TRANDATE < fyEndExclusive)
                .Select(t => (int?)t.TRANNO)
                .Max();

            int nextTranNo = (maxTranNo ?? 0) + 1;

            var master = new TransactionMaster
            {
                COMPYID = compyId,
                SDPTID = 0,
                REGSTRID = SalesInvoiceRegisterId,
                TRANDATE = invoiceDate,
                TRANTIME = DateTime.Now,
                TRANNO = nextTranNo,
                TRANDNO = GenerateSalesInvoiceNumber(invoiceDate, nextTranNo),
                // Keep TRANREFNO tied to the original purchase reference, not the editable Tax Bill No
                TRANREFNO = string.IsNullOrWhiteSpace(purchase.TRANREFNO) ? "-" : purchase.TRANREFNO,
                TRANREFID = customerId,
                TRANPONUM = string.IsNullOrWhiteSpace(model.PoNumber) ? null : model.PoNumber,
                TRANTAXBILLNO = string.IsNullOrWhiteSpace(model.RefNo) ? null : model.RefNo,
                TRANREFNAME = model.CustomerName,
                TRANSTATETYPE = tranStateType,
                TRAN_CRDPRDT = model.CreditDays,
                TRANGAMT = 0m,
                TRANNAMT = 0m,
                TRANCGSTAMT = 0m,
                TRANSGSTAMT = 0m,
                TRANIGSTAMT = 0m,
                TRANAMTWRDS = string.Empty,
                TRANBTYPE = 0,
                EXPRTSTATUS = 0,
                TRANPCOUNT = 0,
                TRANLMID = purchase.TRANMID,
                CUSRID = userName,
                LMUSRID = userName,
                PRCSDATE = DateTime.Now,
                DISPSTATUS = 0,
                TRANRMKS = model.Remarks
            };

            db.TransactionMasters.Add(master);
            db.SaveChanges();

            var batchInsertPairs = new List<Tuple<SalesInvoiceFromPurchaseItemViewModel, TransactionDetail>>();

            foreach (var selected in selectedItems)
            {
                var item = selected.Item;
                int rowIndex = selected.Index;

                decimal rate = item.Rate;
                decimal qty = item.Qty;
                decimal amount = item.Amount > 0 ? item.Amount : qty * rate;

                materialMap.TryGetValue(item.MaterialId, out var material);

                int hsnId = 0;
                string refNo = string.Empty;
                string refName = item.MaterialName;

                if (material != null)
                {
                    hsnId = material.HSNID;
                    refNo = material.MTRLCODE;
                    refName = material.MTRLDESC;
                }

                hsnMap.TryGetValue(hsnId, out var hsn);

                int packMid = item.PackingId ?? 0;

                // Prefer the posted PurchaseTranDetailId, but if it's missing/zero,
                // fall back to the purchase detail row at the same index used in GET
                int purchaseDetailId = item.PurchaseTranDetailId;
                if (purchaseDetailId <= 0 && purchaseDetailsForLink != null && purchaseDetailsForLink.Count > rowIndex)
                {
                    purchaseDetailId = purchaseDetailsForLink[rowIndex].TRANDID;
                }

                decimal gross = amount;
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

                totalGross += gross;
                totalCgst += cgstAmt;
                totalSgst += sgstAmt;
                totalIgst += igstAmt;
                totalNet += net;

                var detail = new TransactionDetail
                {
                    TRANMID = master.TRANMID,
                    TRANDREFID = item.MaterialId,
                    TRANDREFNO = refNo ?? string.Empty,
                    TRANDREFNAME = refName ?? string.Empty,
                    TRANDMTRLPRFT = 0,
                    HSNID = hsnId,
                    PACKMID = packMid,
                    TRANDQTY = qty,
                    TRANDRATE = rate,
                    TRANDARATE = rate,
                    TRANDGAMT = gross,
                    TRANDCGSTAMT = cgstAmt,
                    TRANDSGSTAMT = sgstAmt,
                    TRANDIGSTAMT = igstAmt,
                    TRANDNAMT = net,
                    TRANDAID = purchaseDetailId,
                    TRANDNARTN = null,
                    TRANDRMKS = null
                };

                db.TransactionDetails.Add(detail);

                batchInsertPairs.Add(Tuple.Create(item, detail));
            }

            master.TRANGAMT = totalGross;
            master.TRANCGSTAMT = totalCgst;
            master.TRANSGSTAMT = totalSgst;
            master.TRANIGSTAMT = totalIgst;
            master.TRANNAMT = totalNet;
            master.TRANAMTWRDS = ConvertAmountToWords(totalNet);

            db.SaveChanges();

            // Save manual TAX / cost factors from popup into TRANSACTIONMASTERFACTOR (if any)
            // and update TRANNAMT based on NET amount (totalNet) instead of gross.
            try
            {
                var taxInputs = new List<SalesInvoiceTaxFactorInput>();
                if (!string.IsNullOrWhiteSpace(model.TaxFactorsJson))
                {
                    try
                    {
                        taxInputs = JsonConvert.DeserializeObject<List<SalesInvoiceTaxFactorInput>>(model.TaxFactorsJson)
                                    ?? new List<SalesInvoiceTaxFactorInput>();
                    }
                    catch
                    {
                        taxInputs = new List<SalesInvoiceTaxFactorInput>();
                    }
                }

                if (taxInputs != null && taxInputs.Count > 0)
                {
                    // Remove any existing factors for this invoice (should be none on create, but safe for reuse)
                    db.Database.ExecuteSqlCommand(
                        "DELETE FROM TRANSACTIONMASTERFACTOR WHERE TRANMID = @p0",
                        master.TRANMID);

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

                        // DEDTYPE: 0 = %, 1 = Value (same convention as CostFactorMaster.CFTYPE)
                        int dedType = (tax.ExpressionType != null &&
                                       tax.ExpressionType.Equals("Value", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

                        // DEDMODE: 0 = Add (+), 1 = Deduct (-)
                        int dedMode = (tax.Mode != null && tax.Mode.Trim() == "-") ? 1 : 0;

                        // Base for calculation is NET amount (totalNet = sum of line net amounts including GST)
                        decimal baseNet = totalNet;
                        decimal amount = 0m;

                        if (dedType == 1)
                        {
                            // Fixed value
                            amount = expr;
                        }
                        else
                        {
                            // Percentage of NET amount
                            amount = Math.Round((baseNet * expr) / 100m, 2);
                        }

                        // Apply sign based on mode
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
                            master.TRANMID,              // @p0 - TRANMID
                            tax.CostFactorId,            // @p1 - CFID
                            expr,                        // @p2 - DEDEXPRN (entered value)
                            dedMode,                     // @p3 - DEDMODE (0=+,1=-)
                            dedType,                     // @p4 - DEDTYPE (0=%,1=Value)
                            dedOrder++,                  // @p5 - DEDORDR
                            cfOptn,                      // @p6 - CFOPTN (Amount/Tax/Excise)
                            dordrId,                     // @p7 - DORDRID (Belongs to)
                            amount,                      // @p8 - DEDVALUE (calculated value, signed)
                            desc,                        // @p9 - TRANCFDESC
                            0,                           // @p10 - CFHSNID (0 for manual factors)
                            0.00m,                       // @p11 - TRANCFCGSTEXPRN (percentage - not used for manual)
                            0.00m,                       // @p12 - TRANCFSGSTEXPRN
                            0.00m,                       // @p13 - TRANCFIGSTEXPRN
                            0.00m,                       // @p14 - TRANCFCGSTAMT
                            0.00m,                       // @p15 - TRANCFSGSTAMT
                            0.00m                        // @p16 - TRANCFIGSTAMT
                        );
                    }

                    if (manualTotal != 0m)
                    {
                        // Update master TRANNAMT to include manual cost factors (NET + manualTotal)
                        master.TRANNAMT = totalNet + manualTotal;
                        master.TRANAMTWRDS = ConvertAmountToWords(master.TRANNAMT);
                        db.SaveChanges();
                    }
                }
                else
                {
                    // No manual tax factors - ensure table is clean for this TRANMID
                    db.Database.ExecuteSqlCommand(
                        "DELETE FROM TRANSACTIONMASTERFACTOR WHERE TRANMID = @p0",
                        master.TRANMID);
                }
            }
            catch
            {
                // Swallow exceptions from manual tax factor handling to avoid breaking core invoice save
            }

            if (batchInsertPairs.Any())
            {
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

                foreach (var pair in batchInsertPairs)
                {
                    var item = pair.Item1;
                    var detail = pair.Item2;

                    if (detail.TRANDID <= 0)
                    {
                        continue;
                    }

                    decimal cgstExpr = 0m;
                    decimal sgstExpr = 0m;
                    decimal igstExpr = 0m;

                    if (detail.HSNID > 0 && hsnMap.TryGetValue(detail.HSNID, out var hsnForBatch))
                    {
                        cgstExpr = hsnForBatch.CGSTEXPRN;
                        sgstExpr = hsnForBatch.SGSTEXPRN;
                        igstExpr = hsnForBatch.IGSTEXPRN;
                    }

                    db.Database.ExecuteSqlCommand(
                        queryInsertBatch,
                        detail.TRANDID,
                        detail.TRANDREFID,
                        detail.HSNID,
                        0,
                        item.BatchNo ?? string.Empty,
                        item.ExpiryDate ?? DateTime.Today,
                        detail.PACKMID,
                        0,
                        item.BoxQty,
                        detail.TRANDRATE,
                        item.Ptr,
                        item.Mrp,
                        detail.TRANDGAMT,
                        cgstExpr,
                        sgstExpr,
                        igstExpr,
                        detail.TRANDCGSTAMT,
                        detail.TRANDSGSTAMT,
                        detail.TRANDIGSTAMT,
                        detail.TRANDNAMT,
                        0,
                        detail.TRANDAID,
                        detail.TRANDQTY
                    );
                }

                // After inserting Sales Invoice batch rows, update their TRANDPID to
                // point to the corresponding Purchase Invoice batch primary key (TRANBID)
                db.Database.ExecuteSqlCommand(
                    @"UPDATE sbd
                      SET sbd.TRANDPID = pbd.TRANBID
                      FROM TRANSACTIONBATCHDETAIL AS sbd
                      INNER JOIN TRANSACTIONDETAIL AS sd ON sd.TRANDID = sbd.TRANDID
                      INNER JOIN TRANSACTIONMASTER AS sm ON sm.TRANMID = sd.TRANMID
                      INNER JOIN TRANSACTIONMASTER AS pim ON pim.TRANMID = sm.TRANLMID
                      INNER JOIN TRANSACTIONDETAIL AS pd 
                          ON pd.TRANMID = pim.TRANMID 
                         AND pd.TRANDID = sd.TRANDAID
                      INNER JOIN TRANSACTIONBATCHDETAIL AS pbd 
                          ON pbd.TRANDID = pd.TRANDID
                         AND pbd.AMTRLID = sd.TRANDREFID
                         AND pbd.PACKMID = sd.PACKMID
                         AND pbd.TRANBQTY = sbd.TRANBQTY
                         AND pbd.TRANBDNO = sbd.TRANBDNO
                      WHERE sm.TRANMID = @p0
                        AND sbd.TRANDPID = sd.TRANDAID",
                    master.TRANMID);
            }

            TempData["SuccessMessage"] = "Sales Invoice created successfully.";
            return RedirectToAction("Index");
        }

        /// <summary>
        /// Returns active cost factors for use in the Sales Invoice CreateFromPurchase TAX popup.
        /// This is UI-only and does not change any existing save logic.
        /// </summary>
        [HttpGet]
        public JsonResult GetCostFactorsForSalesInvoice()
        {
            try
            {
                var items = db.CostFactorMasters
                    .Where(c => c.DISPSTATUS == 0)
                    .OrderBy(c => c.CFDESC)
                    .Select(c => new
                    {
                        id = c.CFID,
                        name = c.CFDESC,
                        belongsTo = c.DORDRID
                    })
                    .ToList();

                return Json(new { success = true, items }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
    }

    public class SalesInvoicePrintViewModel
    {
        public int TRANMID { get; set; }
        public int TRANNO { get; set; }
        public string TRANDNO { get; set; }
        public string TRANREFNO { get; set; }
        public DateTime TRANDATE { get; set; }
        public int CreditDays { get; set; }
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
        public string CustomerPhone { get; set; }
        public string CustomerDlNo { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal NetAmount { get; set; }
        public decimal CgstAmount { get; set; }
        public decimal SgstAmount { get; set; }
        public decimal IgstAmount { get; set; }
        public string SalesOrderNo { get; set; }
        public DateTime? SalesOrderDate { get; set; }
        public string PurchaseInvoiceNo { get; set; }
        public DateTime? PurchaseInvoiceDate { get; set; }
        public string PoNumber { get; set; }
        public string TaxBillNo { get; set; }
        public string AmountInWords { get; set; }
        public IList<SalesInvoicePrintItemViewModel> Items { get; set; }
        public string CompanyAddress { get; set; }
        public string CompanyName { get; set; }
        public string CompanyGstNo { get; set; }
        public IList<SalesInvoiceClassSummaryViewModel> ClassSummaries { get; set; }
        public int TotalItems { get; set; }
        public decimal TotalQty { get; set; }
        public string Remarks { get; set; }
        public string UserName { get; set; }
        public DateTime BillingTime { get; set; }
        public decimal TotalDisc { get; set; }
        public decimal CourierCharges { get; set; }
    }

    public class SalesInvoicePrintItemViewModel
    {
        public string Division { get; set; }
        public string MaterialName { get; set; }
        public string Pack { get; set; }
        public decimal Qty { get; set; }
        public string BatchNo { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string HsnCode { get; set; }
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

    public class SalesInvoiceClassSummaryViewModel
    {
        public string ClassName { get; set; }
        public decimal GstPercent { get; set; }
        public decimal Total { get; set; }
        public decimal Scheme { get; set; }
        public decimal Discount { get; set; }
        public decimal Sgst { get; set; }
        public decimal Cgst { get; set; }
        public decimal TotalGst { get; set; }
    }
}
