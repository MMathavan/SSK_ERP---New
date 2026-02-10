using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
using ClosedXML.Excel;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Purchase
{
    [SessionExpire]
    public class PurchaseInvoiceUploadController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int PurchaseRegisterId = 2;
        private const int PurchaseInvoiceRegisterId = 18;

        private string ConvertNumberToWords(int number, string[] ones, string[] teens, string[] tens)
        {
            if (number < 10) return ones[number];
            if (number < 20) return teens[number - 10];
            if (number < 100) return tens[number / 10] + (number % 10 > 0 ? " " + ones[number % 10] : string.Empty);
            return string.Empty;
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

        public class TransactionPInvoiceMasterRow
        {
            public int Id { get; set; }
            public string PoNumber { get; set; }
            public DateTime? PoDate { get; set; }
            public string CustomerInfo { get; set; }
            public string FullExtractedText { get; set; }
            public string TaxInvoiceNo { get; set; }
        }

        public class TransactionPInvoiceDetailRow
        {
            public int TransactionPInvoiceMasterId { get; set; }
            public int LineNo { get; set; }
            public string MfgCode { get; set; }
            public string Cat { get; set; }
            public string ProductDescription { get; set; }
            public string HsnCode { get; set; }
            public string UnitUom { get; set; }
            public string BatchNo { get; set; }
            public string ExpiryText { get; set; }
            public string Boxes { get; set; }
            public decimal TotalQty { get; set; }
            public decimal PricePerUnit { get; set; }
            public decimal Ptr { get; set; }
            public decimal Mrp { get; set; }
            public decimal TotalValue { get; set; }
            public decimal DiscPercent { get; set; }
            public decimal DiscountValue { get; set; }
            public decimal TaxableValue { get; set; }
            public decimal CgstRate { get; set; }
            public decimal CgstAmount { get; set; }
            public decimal SgstRate { get; set; }
            public decimal SgstAmount { get; set; }
            public decimal TotalAmount { get; set; }
        }

        public class TransactionPInvoiceDetailMaterialRow
        {
            public int TransactionPInvoiceMasterId { get; set; }
            public int LineNo { get; set; }
            public string ProductDescription { get; set; }
            public int? MTRLID { get; set; }
            public string MTRLDESC { get; set; }
        }

        public class TransactionPInvoiceMasterHeaderRow
        {
            public int Id { get; set; }
            public int? SupplierId { get; set; }
            public int? PurchaseOrderId { get; set; }
            public string PoNumber { get; set; }
            public DateTime? PoDate { get; set; }
            public string TaxInvoiceNo { get; set; }
        }

        private class PurchaseInvoiceDetailSelection
        {
            public int LineNo { get; set; }
            public int MaterialId { get; set; }
            public int PackingId { get; set; }
        }

        private class PurchaseInvoiceDetailCalcRow
        {
            public int LineNo { get; set; }
            public int MaterialId { get; set; }
            public int PackingId { get; set; }
            public int HsnId { get; set; }
            public string MaterialCode { get; set; }
            public string MaterialName { get; set; }
            public decimal ProfitPercent { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Taxable { get; set; }
            public decimal Cgst { get; set; }
            public decimal Sgst { get; set; }
            public decimal Igst { get; set; }
            public decimal Net { get; set; }
        }

        public class PoDropdownResult
        {
            public int TRANMID { get; set; }
            public string POREFNO { get; set; }
        }

        [HttpGet]
        public JsonResult GetPurchaseOrdersBySupplier(int supplierId)
        {
            try
            {
                var poList = db.Database.SqlQuery<PoDropdownResult>("EXEC PR_PONODETAILS @Tranrefid",
                    new SqlParameter("@Tranrefid", supplierId)).ToList();

                var results = poList.Select(p => new
                {
                    id = p.TRANMID,
                    text = p.POREFNO
                }).ToList();

                return Json(results, JsonRequestBehavior.AllowGet);
            }
            catch
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        private void PopulateDropdowns(int? selectedSupplierId = null, int? selectedPurchaseOrderId = null)
        {
            if (!selectedSupplierId.HasValue && Session["PurchaseInvoiceUploadSupplierId"] != null)
            {
                int sId;
                if (int.TryParse(Session["PurchaseInvoiceUploadSupplierId"].ToString(), out sId))
                {
                    selectedSupplierId = sId;
                }
            }

            if (!selectedPurchaseOrderId.HasValue && Session["PurchaseInvoiceUploadPoId"] != null)
            {
                int pId;
                if (int.TryParse(Session["PurchaseInvoiceUploadPoId"].ToString(), out pId))
                {
                    selectedPurchaseOrderId = pId;
                }
            }

            var supplierList = db.SupplierMasters
                .Where(s => s.DISPSTATUS == 0 || s.DISPSTATUS == null)
                .OrderBy(s => s.CATENAME)
                .Select(s => new
                {
                    s.CATEID,
                    Name = s.CATENAME
                })
                .ToList();

            ViewBag.SupplierList = new SelectList(supplierList, "CATEID", "Name", selectedSupplierId);

            var purchaseOrders = new List<object>();

            if (selectedSupplierId.HasValue && selectedSupplierId.Value > 0)
            {
                try
                {
                    var poList = db.Database.SqlQuery<PoDropdownResult>("EXEC PR_PONODETAILS @Tranrefid",
                        new SqlParameter("@Tranrefid", selectedSupplierId.Value)).ToList();

                    purchaseOrders = poList.Select(t => new
                    {
                        TRANMID = t.TRANMID,
                        Display = t.POREFNO
                    }).ToList<object>();
                }
                catch
                {
                    // Fallback to empty if SP fails or other error
                }
            }

            ViewBag.PurchaseOrderList = new SelectList(purchaseOrders, "TRANMID", "Display", selectedPurchaseOrderId);

            try
            {
                var packings = db.PackingMasters
                    .Where(p => p.DISPSTATUS == 0)
                    .OrderBy(p => p.PACKMDESC)
                    .Select(p => new SelectListItem
                    {
                        Text = p.PACKMDESC,
                        Value = p.PACKMID.ToString()
                    })
                    .ToList();

                ViewBag.PackingMasters = packings;
            }
            catch
            {
                ViewBag.PackingMasters = new List<SelectListItem>();
            }
        }

        [HttpGet]
        public JsonResult SearchActualProducts(string term)
        {
            term = (term ?? string.Empty).Trim();

            var query = db.MaterialMasters.Where(m => m.DISPSTATUS == 0);
            if (!string.IsNullOrEmpty(term))
            {
                query = query.Where(m => m.MTRLDESC.Contains(term));
            }

            var results = query
                .OrderBy(m => m.MTRLDESC)
                .Take(50)
                .Select(m => new
                {
                    id = m.MTRLID,
                    text = m.MTRLDESC
                })
                .ToList();

            return Json(results, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetPackingUnits(int packingId)
        {
            try
            {
                var packing = db.PackingMasters
                    .Where(p => p.PACKMID == packingId && p.DISPSTATUS == 0)
                    .Select(p => new { p.PACKMNOU })
                    .FirstOrDefault();

                if (packing != null)
                {
                    return Json(new { success = true, units = packing.PACKMNOU }, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    return Json(new { success = false }, JsonRequestBehavior.AllowGet);
                }
            }
            catch
            {
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);
            }
        }

        private class PoDetailResult
        {
            public string MTRLDESC { get; set; }
            public decimal TRANQTY { get; set; }
            public decimal TRANRATE { get; set; }
            public decimal TRANAMT { get; set; }
            public string UOMCode { get; set; }
        }

        [HttpGet]
        public ActionResult GetPoDetails(int id)
        {
            try
            {
                // Fetch PO Master
                var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id);
                if (master == null)
                {
                    return Content("<div class='alert alert-danger'>Purchase Order not found.</div>");
                }

                // Fetch PO Details (Materials)
                // Note: Using TRANSACTIONDETAIL table and mapping columns correctly based on model
                var materials = db.Database.SqlQuery<PoDetailResult>(@"
                    SELECT 
                        d.TRANDID, 
                        d.TRANMID, 
                        d.TRANDREFNAME AS MTRLDESC, -- Use stored name or join
                        d.TRANDQTY AS TRANQTY, 
                        d.TRANDRATE AS TRANRATE, 
                        d.TRANDNAMT AS TRANAMT,
                        p.PACKMDESC AS UOMCode -- Using Packing Description as UOM/Unit logic often uses Packing
                    FROM TRANSACTIONDETAIL d
                    LEFT JOIN PACKINGMASTER p ON d.PACKMID = p.PACKMID
                    WHERE d.TRANMID = @p0
                    ORDER BY d.TRANDID
                ", id).ToList();

                var sb = new StringBuilder();

                // Header Info
                sb.Append("<div class='row mb-3'>");
                sb.Append($"<div class='col-md-4'><strong>PO No:</strong> {master.TRANDNO}</div>");
                sb.Append($"<div class='col-md-4'><strong>PO Date:</strong> {master.TRANDATE:dd/MM/yyyy}</div>");
                sb.Append($"<div class='col-md-4'><strong>Ref No:</strong> {master.TRANREFNAME}</div>");
                sb.Append("</div>"); 

                // Table
                sb.Append("<div class='table-responsive'>");
                sb.Append("<table class='table table-bordered table-striped'>");
                sb.Append("<thead class='table-dark'><tr>");
                sb.Append("<th>S.No</th>");
                sb.Append("<th>Material</th>");
                sb.Append("<th class='text-end'>Qty</th>");
                sb.Append("</tr></thead>");
                sb.Append("<tbody>");

                if (materials != null && materials.Count > 0)
                {
                    int sno = 1;
                    foreach (var item in materials)
                    {
                        sb.Append("<tr>");
                        sb.Append($"<td>{sno++}</td>");
                        sb.Append($"<td>{item.MTRLDESC}</td>");
                        sb.Append($"<td class='text-end'>{item.TRANQTY:0.00}</td>");
                        sb.Append("</tr>");
                    }
                }
                else
                {
                    sb.Append("<tr><td colspan='3' class='text-center'>No items found.</td></tr>");
                }

                sb.Append("</tbody>");
                sb.Append("</table>");
                sb.Append("</div>");

                return Content(sb.ToString());
            }
            catch (Exception ex)
            {
                return Content($"<div class='alert alert-danger'>Error loading details: {ex.Message}</div>");
            }
        }

        [HttpGet]
        public JsonResult GetAllPackingMasters()
        {
            try
            {
                var packings = db.PackingMasters
                    .Where(p => p.DISPSTATUS == 0)
                    .Select(p => new
                    {
                        id = p.PACKMID,
                        units = p.PACKMNOU,
                        description = p.PACKMDESC
                    })
                    .ToList();

                return Json(packings, JsonRequestBehavior.AllowGet);
            }
            catch
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        // No role-based Authorize here so Upload works without adding new roles
        [HttpGet]
        public ActionResult Index()
        {
            if (TempData["PInvoiceMasterTempId"] == null)
            {
                Session["PurchaseInvoiceUploadSupplierId"] = null;
                Session["PurchaseInvoiceUploadPoId"] = null;
            }

            PopulateDropdowns();

            var masterTempIdObj = TempData["PInvoiceMasterTempId"];
            if (masterTempIdObj != null)
            {
                int masterTempId;
                if (int.TryParse(masterTempIdObj.ToString(), out masterTempId) && masterTempId > 0)
                {
                    var masterRow = db.Database.SqlQuery<TransactionPInvoiceMasterRow>(
                        "SELECT Id, PoNumber, PoDate, CustomerInfo, FullExtractedText, TaxInvoiceNo FROM transactionpinvoicemaster WHERE Id = @p0;",
                        new SqlParameter("@p0", masterTempId)
                    ).FirstOrDefault();

                    if (masterRow != null)
                    {
                        ViewBag.PInvoiceMaster = masterRow;
                    }

                    var details = db.Database.SqlQuery<TransactionPInvoiceDetailRow>(
                        "SELECT TransactionPInvoiceMasterId, [LineNo], MfgCode, Cat, ProductDescription, HsnCode, UnitUom, BatchNo, ExpiryText, Boxes, TotalQty, PricePerUnit, Ptr, Mrp, TotalValue, DiscPercent, DiscountValue, TaxableValue, CgstRate, CgstAmount, SgstRate, SgstAmount, TotalAmount " +
                        "FROM transactionpinvoicedetail WHERE TransactionPInvoiceMasterId = @p0 ORDER BY Id;",
                        new SqlParameter("@p0", masterTempId)
                    ).ToList();

                    if (details.Any())
                    {
                        string currentUserId;
                        if (Session["CUSRID"] != null)
                        {
                            currentUserId = Session["CUSRID"].ToString();
                        }
                        else if (Session["USERNAME"] != null)
                        {
                            currentUserId = Session["USERNAME"].ToString();
                        }
                        else if (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                        {
                            currentUserId = User.Identity.Name;
                        }
                        else
                        {
                            currentUserId = "admin";
                        }

                        var userParam = new SqlParameter("@kusrid", (object)currentUserId ?? DBNull.Value);

                        var allMatches = db.Database.SqlQuery<TransactionPInvoiceDetailMaterialRow>(
                            "EXEC PR_transactionpinvoicedetail_Up @kusrid",
                            userParam
                        ).ToList();

                        var matchesForCurrentMaster = allMatches
                            .Where(m => m.TransactionPInvoiceMasterId == masterTempId)
                            .ToList();

                        ViewBag.PInvoiceDetails = details;

                        if (matchesForCurrentMaster.Any())
                        {
                            ViewBag.PInvoiceDetailMatches = matchesForCurrentMaster;
                        }
                    }
                }
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Index(HttpPostedFileBase file, int? SupplierId, int? PurchaseOrderId)
        {
            PopulateDropdowns(SupplierId, PurchaseOrderId);

            if (!SupplierId.HasValue || SupplierId.Value <= 0)
            {
                TempData["ErrorMessage"] = "Please select a supplier.";
                return View();
            }

            if (!PurchaseOrderId.HasValue || PurchaseOrderId.Value <= 0)
            {
                TempData["ErrorMessage"] = "Please select a Purchase Order Number.";
                return View();
            }

            if (file == null || file.ContentLength == 0)
            {
                TempData["ErrorMessage"] = "Please select a file to upload.";
                return View();
            }

            var extension = (Path.GetExtension(file.FileName) ?? string.Empty).ToLowerInvariant();
            var allowedExtensions = new[] { ".xls", ".xlsx", ".csv" };
            if (!allowedExtensions.Contains(extension))
            {
                TempData["ErrorMessage"] = "Invalid file type. Only Excel (.xls, .xlsx) and CSV (.csv) files are allowed.";
                return View();
            }

            var uploadsDir = Server.MapPath("~/Uploads/PurchaseInvoicePdfs");
            if (!Directory.Exists(uploadsDir))
            {
                Directory.CreateDirectory(uploadsDir);
            }

            var safeName = Path.GetFileNameWithoutExtension(file.FileName);
            var uniqueName = string.Format("{0}_{1:yyyyMMddHHmmssfff}{2}", safeName, DateTime.Now, extension);
            var fullPath = Path.Combine(uploadsDir, uniqueName);

            try
            {
                file.SaveAs(fullPath);

                if (extension == ".xls" || extension == ".xlsx")
                {
                    return ProcessExcelInvoice(fullPath, file.FileName, SupplierId, PurchaseOrderId);
                }
                else if (extension == ".csv")
                {
                    // Basic CSV support: treat the whole file as plain text and
                    // reuse the existing text-based parser in SaveTemp.
                    var csvText = System.IO.File.ReadAllText(fullPath);
                    return SaveTemp(csvText, file.FileName, SupplierId, PurchaseOrderId);
                }
                else
                {
                    TempData["ErrorMessage"] = "Unsupported file type.";
                    return View();
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "There was a problem processing the uploaded file: " + ex.Message;
                return View();
            }
        }

        private ActionResult ProcessExcelInvoice(string excelFilePath, string originalFileName, int? supplierId, int? purchaseOrderId)
        {
            try
            {
                var uploadedBy = (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                    ? User.Identity.Name
                    : "PurchaseInvoiceUpload";

                if (supplierId.HasValue && supplierId.Value > 0)
                {
                    Session["PurchaseInvoiceUploadSupplierId"] = supplierId.Value;
                }
                if (purchaseOrderId.HasValue && purchaseOrderId.Value > 0)
                {
                    Session["PurchaseInvoiceUploadPoId"] = purchaseOrderId.Value;
                }

                try
                {
                    var lastBatchStr = Session["LastPInvUploadBatchId"] as string;
                    var lastMasterTempObj = Session["LastPInvMasterTempId"];

                    Guid lastBatchGuid;
                    int lastMasterTempId;
                    if (!string.IsNullOrWhiteSpace(lastBatchStr)
                        && Guid.TryParse(lastBatchStr, out lastBatchGuid)
                        && lastMasterTempObj != null
                        && int.TryParse(lastMasterTempObj.ToString(), out lastMasterTempId)
                        && lastMasterTempId > 0)
                    {
                        db.Database.ExecuteSqlCommand(
                            "DELETE FROM transactionpinvoicedetail WHERE TransactionPInvoiceMasterId = @p0; DELETE FROM transactionpinvoicemaster WHERE UploadBatchId = @p1;",
                            lastMasterTempId,
                            lastBatchGuid);
                    }
                }
                catch
                {
                }

                var uploadBatchId = Guid.NewGuid();

                using (var workbook = new XLWorkbook(excelFilePath))
                {
                    var worksheet = workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        TempData["ErrorMessage"] = "The uploaded Excel file does not contain any worksheets.";
                        return RedirectToAction("Index");
                    }

                    var usedRange = worksheet.RangeUsed();
                    if (usedRange == null)
                    {
                        TempData["ErrorMessage"] = "The uploaded Excel file does not contain any data.";
                        return RedirectToAction("Index");
                    }

                    string poNumber = null;
                    DateTime? poDate = null;
                    string customerInfo = null;
                    var fullTextBuilder = new StringBuilder();

                    foreach (var row in usedRange.Rows())
                    {
                        var rowTexts = new List<string>();

                        foreach (var cell in row.Cells())
                        {
                            var cellText = cell.GetFormattedString().Trim();
                            if (string.IsNullOrEmpty(cellText))
                            {
                                continue;
                            }

                            rowTexts.Add(cellText);

                            if ((poNumber == null || !poDate.HasValue) &&
                                cellText.IndexOf("Buyer PO No", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var match = Regex.Match(cellText,
                                    @"Buyer\s*PO\s*No\.?\s*:?\s*(?<po>.+?)\s+PO\s*Date\s*:?\s*(?<date>\d{1,2}[./-]\d{1,2}[./-]\d{4})",
                                    RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    poNumber = match.Groups["po"].Value.Trim();
                                    var dateStr = match.Groups["date"].Value.Trim();
                                    DateTime dtPo;
                                    if (DateTime.TryParseExact(dateStr,
                                        new[] { "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy" },
                                        CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out dtPo))
                                    {
                                        poDate = dtPo;
                                    }
                                }
                            }

                            if (customerInfo == null &&
                                cellText.StartsWith("Customer name", StringComparison.OrdinalIgnoreCase))
                            {
                                var sbCust = new StringBuilder();
                                sbCust.Append(cellText);

                                var baseRow = cell.WorksheetRow().RowNumber();
                                var baseCol = cell.WorksheetColumn().ColumnNumber();

                                for (int c = baseCol + 1; c <= baseCol + 3; c++)
                                {
                                    var extraCell = worksheet.Cell(baseRow, c);
                                    var extraText = extraCell.GetFormattedString().Trim();
                                    if (!string.IsNullOrEmpty(extraText))
                                    {
                                        sbCust.Append(" " + extraText);
                                    }
                                }

                                for (int r = baseRow + 1; r <= baseRow + 3; r++)
                                {
                                    var extraCell = worksheet.Cell(r, baseCol);
                                    var extraText = extraCell.GetFormattedString().Trim();
                                    if (!string.IsNullOrEmpty(extraText))
                                    {
                                        if (sbCust.Length > 0)
                                        {
                                            sbCust.AppendLine();
                                        }
                                        sbCust.Append(extraText);
                                    }
                                }

                                customerInfo = sbCust.ToString();
                            }
                        }

                        if (rowTexts.Count > 0)
                        {
                            fullTextBuilder.AppendLine(string.Join(" ", rowTexts));
                        }
                    }

                    string fullText = fullTextBuilder.ToString();

                    string taxInvoiceNo = null;
                    var taxInvMatch = Regex.Match(fullText, @"Tax\s*Invoice\s*No[:.]?\s*(?<inv>[A-Za-z0-9]+)", RegexOptions.IgnoreCase);
                    if (taxInvMatch.Success)
                    {
                        taxInvoiceNo = taxInvMatch.Groups["inv"].Value.Trim();
                    }

                    object DbValue(object value) => value ?? (object)DBNull.Value;

                    int masterId = db.Database.SqlQuery<int>(
                        "INSERT INTO transactionpinvoicemaster (UploadBatchId, OriginalPdfFileName, UploadedOn, UploadedBy, SupplierId, PurchaseOrderId, PoNumber, PoDate, CustomerInfo, TaxInvoiceNo, FullExtractedText) " +
                        "VALUES (@p0, @p1, GETDATE(), @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        uploadBatchId,
                        originalFileName ?? string.Empty,
                        uploadedBy,
                        DbValue(supplierId),
                        DbValue(purchaseOrderId),
                        DbValue(poNumber),
                        DbValue(poDate),
                        DbValue(customerInfo),
                        DbValue(taxInvoiceNo),
                        fullText
                    ).Single();

                    int headerRowNumber = -1;
                    int firstDataRow = -1;
                    int lastDataRow = usedRange.LastRow().RowNumber();

                    int srNoCol = -1;
                    int mfgCodeCol = -1;
                    int catCol = -1;
                    int productDescriptionCol = -1;
                    int hsnCol = -1;
                    int unitUomCol = -1;
                    int batchNoCol = -1;
                    int expiryCol = -1;
                    int boxesCol = -1;

                    int firstCol = usedRange.FirstColumn().ColumnNumber();
                    int lastCol = usedRange.LastColumn().ColumnNumber();

                    for (int r = usedRange.FirstRow().RowNumber(); r <= usedRange.LastRow().RowNumber(); r++)
                    {
                        bool hasProductDesc = false;
                        bool hasHsn = false;

                        for (int c = firstCol; c <= lastCol; c++)
                        {
                            var headerText = worksheet.Cell(r, c).GetFormattedString().Trim();
                            if (string.IsNullOrEmpty(headerText))
                            {
                                continue;
                            }

                            var norm = Regex.Replace(headerText, @"\s+", " ").Trim().ToLowerInvariant();

                            if (norm.Contains("product description"))
                            {
                                hasProductDesc = true;
                            }
                            if (norm.Contains("hsn") && norm.Contains("code"))
                            {
                                hasHsn = true;
                            }
                        }

                        if (hasProductDesc && hasHsn)
                        {
                            headerRowNumber = r;
                            break;
                        }
                    }

                    if (headerRowNumber <= 0)
                    {
                        TempData["ErrorMessage"] = "Could not locate the item header row in the Excel invoice.";
                        return RedirectToAction("Index");
                    }

                    for (int c = firstCol; c <= lastCol; c++)
                    {
                        var headerText = worksheet.Cell(headerRowNumber, c).GetFormattedString().Trim();
                        if (string.IsNullOrEmpty(headerText))
                        {
                            continue;
                        }

                        var norm = Regex.Replace(headerText, @"\s+", " ").Trim().ToLowerInvariant();

                        if (srNoCol < 0 && (norm.StartsWith("st.") || norm.StartsWith("sr.") || norm.StartsWith("s.no") || norm.StartsWith("st no")))
                        {
                            srNoCol = c;
                        }
                        else if (mfgCodeCol < 0 && norm.Contains("mfg") && norm.Contains("code"))
                        {
                            mfgCodeCol = c;
                        }
                        else if (catCol < 0 && norm == "cat")
                        {
                            catCol = c;
                        }
                        else if (productDescriptionCol < 0 && norm.Contains("product description"))
                        {
                            productDescriptionCol = c;
                        }
                        else if (hsnCol < 0 && norm.Contains("hsn") && norm.Contains("code"))
                        {
                            hsnCol = c;
                        }
                        else if (unitUomCol < 0 && (norm.Contains("unit uom") || norm == "uom"))
                        {
                            unitUomCol = c;
                        }
                        else if (batchNoCol < 0 && norm.Contains("batch") && norm.Contains("no"))
                        {
                            batchNoCol = c;
                        }
                        else if (expiryCol < 0 && (norm.Contains("expiry") || norm.Contains("exp date")))
                        {
                            expiryCol = c;
                        }
                        else if (boxesCol < 0 && (norm.Contains("no. of boxes") || norm.Contains("no of boxes") || norm.Contains("boxes/shipper")))
                        {
                            boxesCol = c;
                        }
                    }

                    if (productDescriptionCol < 0 || hsnCol < 0 || boxesCol < 0)
                    {
                        TempData["ErrorMessage"] = "The Excel invoice columns could not be mapped. Please ensure the header row matches the expected layout.";
                        return RedirectToAction("Index");
                    }

                    if (srNoCol < 0)
                    {
                        srNoCol = productDescriptionCol - 3;
                    }
                    if (mfgCodeCol < 0)
                    {
                        mfgCodeCol = productDescriptionCol - 2;
                    }
                    if (catCol < 0)
                    {
                        catCol = productDescriptionCol - 1;
                    }
                    if (unitUomCol < 0)
                    {
                        unitUomCol = hsnCol + 1;
                    }
                    if (batchNoCol < 0)
                    {
                        batchNoCol = unitUomCol + 1;
                    }
                    if (expiryCol < 0)
                    {
                        expiryCol = batchNoCol + 1;
                    }

                    firstDataRow = headerRowNumber + 1;

                    decimal ParseDecimalOrZero(string s)
                    {
                        if (string.IsNullOrWhiteSpace(s)) return 0m;

                        s = s.Replace(",", string.Empty);
                        s = Regex.Replace(s, @"[^0-9.\-]", string.Empty);

                        if (string.IsNullOrWhiteSpace(s) || s == "." || s == "-" || s == "-.")
                        {
                            return 0m;
                        }

                        decimal val;
                        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                        {
                            return val;
                        }
                        return 0m;
                    }

                    // Numeric columns are positioned consistently to the right of "No. of Boxes/Shipper".
                    // Layout (SUN Pharma): TotalQty, PricePerUnit, Disc%, PTR, MRP, TotalValue, then discount/tax columns.
                    //
                    // Start with relative offsets from the Boxes column, then refine them using the
                    // actual header text (including multi-row CGST/SGST headers) so that columns like
                    // Taxable Value, Rate of Tax %, Tax Amount and Total Amount are mapped correctly.
                    int totalQtyCol = boxesCol + 1;
                    int pricePerUnitCol = boxesCol + 2;
                    int discPercentCol = boxesCol + 3;
                    int ptrCol = boxesCol + 4;
                    int mrpCol = boxesCol + 5;
                    int totalValueCol = boxesCol + 6;
                    int discountValueCol = boxesCol + 7;
                    int taxableValueCol = boxesCol + 8;
                    int cgstRateCol = boxesCol + 9;
                    int cgstAmountCol = boxesCol + 10;
                    int sgstRateCol = boxesCol + 11;
                    int sgstAmountCol = boxesCol + 12;
                    int totalAmountCol = boxesCol + 13;

                    bool ptrHeaderDetected = false;

                    // Refine numeric column mapping using header text from the main header row
                    // and nearby header rows (used for CGST/SGST Rate/Amount columns).
                    int headerRowAbove = headerRowNumber - 1;
                    int headerRowBelow = headerRowNumber + 1;
                    for (int c = boxesCol + 1; c <= lastCol; c++)
                    {
                        var hMain = worksheet.Cell(headerRowNumber, c).GetFormattedString().Trim();
                        var hAbove = headerRowAbove >= usedRange.FirstRow().RowNumber()
                            ? worksheet.Cell(headerRowAbove, c).GetFormattedString().Trim()
                            : string.Empty;
                        var hBelow = headerRowBelow <= lastDataRow
                            ? worksheet.Cell(headerRowBelow, c).GetFormattedString().Trim()
                            : string.Empty;

                        var combined = (hAbove + " " + hMain + " " + hBelow).Trim();
                        if (string.IsNullOrWhiteSpace(combined))
                        {
                            continue;
                        }

                        var normHeader = Regex.Replace(combined, @"\s+", " ").Trim().ToLowerInvariant();
                        var headerNoPunct = Regex.Replace(normHeader, @"[^a-z0-9]+", string.Empty);

                        if (normHeader.Contains("total qty") || normHeader.Contains("total quantity"))
                        {
                            totalQtyCol = c;
                        }
                        else if (normHeader.Contains("price per unit") || normHeader.Contains("price per") || normHeader.Contains("unit price"))
                        {
                            pricePerUnitCol = c;
                        }
                        else if (normHeader.StartsWith("disc") && normHeader.Contains("%"))
                        {
                            discPercentCol = c;
                        }
                        else if (normHeader.Contains("ptr") || headerNoPunct.Contains("ptr"))
                        {
                            ptrCol = c;
                            ptrHeaderDetected = true;
                        }
                        else if (normHeader.Contains("m.r.p") || normHeader.Contains("mrp"))
                        {
                            mrpCol = c;
                        }
                        else if (normHeader.Contains("total value") && !normHeader.Contains("total amount"))
                        {
                            totalValueCol = c;
                        }
                        else if (normHeader.Contains("discount value"))
                        {
                            discountValueCol = c;
                        }
                        else if (normHeader.Contains("taxable value"))
                        {
                            taxableValueCol = c;
                        }
                        else if (normHeader.Contains("cgst") && normHeader.Contains("rate"))
                        {
                            cgstRateCol = c;
                        }
                        else if (normHeader.Contains("cgst") && normHeader.Contains("amount"))
                        {
                            cgstAmountCol = c;
                        }
                        else if (normHeader.Contains("sgst") && normHeader.Contains("rate"))
                        {
                            sgstRateCol = c;
                        }
                        else if (normHeader.Contains("sgst") && normHeader.Contains("amount"))
                        {
                            sgstAmountCol = c;
                        }
                        else if (normHeader.Contains("total amount"))
                        {
                            totalAmountCol = c;
                        }
                    }

                    // For SUN-style invoices, the GST columns appear immediately after
                    // the "Taxable Value" column in the order:
                    //   CGST Rate %, CGST Tax Amount, SGST Rate %, SGST Tax Amount, Total Amount.
                    // When this header pattern is detected, prefer these relative
                    // positions so that CGST/SGST rate and amount values are mapped
                    // accurately even if columns have been moved or additional
                    // columns were inserted earlier in the row.
                    if (taxableValueCol > 0 && taxableValueCol + 5 <= lastCol)
                    {
                        int c1 = taxableValueCol + 1;
                        int c2 = taxableValueCol + 2;
                        int c3 = taxableValueCol + 3;
                        int c4 = taxableValueCol + 4;
                        int c5 = taxableValueCol + 5;

                        string h1 = worksheet.Cell(headerRowNumber, c1).GetFormattedString().Trim();
                        string h2 = worksheet.Cell(headerRowNumber, c2).GetFormattedString().Trim();
                        string h3 = worksheet.Cell(headerRowNumber, c3).GetFormattedString().Trim();
                        string h4 = worksheet.Cell(headerRowNumber, c4).GetFormattedString().Trim();
                        string h5 = worksheet.Cell(headerRowNumber, c5).GetFormattedString().Trim();

                        string n1 = Regex.Replace((h1 ?? string.Empty), "\\s+", " ").Trim().ToLowerInvariant();
                        string n2 = Regex.Replace((h2 ?? string.Empty), "\\s+", " ").Trim().ToLowerInvariant();
                        string n3 = Regex.Replace((h3 ?? string.Empty), "\\s+", " ").Trim().ToLowerInvariant();
                        string n4 = Regex.Replace((h4 ?? string.Empty), "\\s+", " ").Trim().ToLowerInvariant();
                        string n5 = Regex.Replace((h5 ?? string.Empty), "\\s+", " ").Trim().ToLowerInvariant();

                        bool looksLikeRate(string s) => !string.IsNullOrEmpty(s) && (s.Contains("rate") || s.Contains("tax %"));
                        bool looksLikeAmount(string s) => !string.IsNullOrEmpty(s) && s.Contains("amount");

                        if (looksLikeRate(n1) && looksLikeAmount(n2) && looksLikeRate(n3) && looksLikeAmount(n4) && n5.Contains("total amount"))
                        {
                            cgstRateCol = c1;
                            cgstAmountCol = c2;
                            sgstRateCol = c3;
                            sgstAmountCol = c4;
                            totalAmountCol = c5;
                        }
                    }

                    // Ensure GST amount columns sit immediately to the right of the
                    // corresponding rate columns, which matches the SUN layout and
                    // prevents mapping the CGST/SGST amount from the Taxable Value
                    // column.
                    if (cgstRateCol > 0 && cgstRateCol + 1 <= lastCol)
                    {
                        cgstAmountCol = cgstRateCol + 1;
                    }
                    if (sgstRateCol > 0 && sgstRateCol + 1 <= lastCol)
                    {
                        sgstAmountCol = sgstRateCol + 1;
                    }

                    int lineNo = 1;

                    // Carry forward group headers such as "B9 - ALTAN" so they can be
                    // prefixed to subsequent product descriptions (e.g. AZTOGOLD 20...).
                    string lastGroupHeader = null;

                    string CombineDescription(string groupHeader, string desc)
                    {
                        if (string.IsNullOrWhiteSpace(groupHeader))
                        {
                            return desc;
                        }
                        if (string.IsNullOrWhiteSpace(desc))
                        {
                            return groupHeader;
                        }

                        // Avoid duplicating the group header if it's already present.
                        if (desc.StartsWith(groupHeader, StringComparison.OrdinalIgnoreCase))
                        {
                            return desc;
                        }

                        return groupHeader + " " + desc;
                    }

                    string ExtractGroupHeader(string description)
                    {
                        if (string.IsNullOrWhiteSpace(description))
                        {
                            return null;
                        }

                        // Look for a trailing pattern like "B9 - ALTAN" at the end of the
                        // description text (e.g. "CLOPILET A 75 15S B9 - ALTAN").
                        var match = Regex.Match(description, @"([A-Z0-9]{1,3}\s*-\s*[A-Za-z][A-Za-z0-9 ]*)$",
                            RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var value = match.Groups[1].Value.Trim();
                            return string.IsNullOrWhiteSpace(value) ? null : value;
                        }

                        return null;
                    }

                    bool hasCurrent = false;
                    bool currentHasNumeric = false;
                    int currentLineNoValue = 0;
                    string currentSrNoText = null;
                    string currentMfgCode = null;
                    string currentCat = null;
                    string currentProductDescription = null;
                    string currentHsnCode = null;
                    string currentUnitUom = null;
                    string currentBatchNo = null;
                    string currentExpiryText = null;
                    string currentBoxes = null;
                    decimal currentTotalQty = 0m;
                    decimal currentPricePerUnit = 0m;
                    decimal currentPtr = 0m;
                    decimal currentMrp = 0m;
                    decimal currentTotalValue = 0m;
                    decimal currentDiscPercent = 0m;
                    decimal currentDiscountValue = 0m;
                    decimal currentTaxableValue = 0m;
                    decimal currentCgstRate = 0m;
                    decimal currentCgstAmount = 0m;
                    decimal currentSgstRate = 0m;
                    decimal currentSgstAmount = 0m;
                    decimal currentTotalAmount = 0m;

                    string StripDivisionPrefix(string description)
                    {
                        if (string.IsNullOrWhiteSpace(description))
                        {
                            return description;
                        }

                        // Pattern for leading division-style prefix, e.g.
                        // "B9 - SYMENTA XAFINACT 100" -> keep "XAFINACT 100"
                        // "Z4- SEPHEUS PARKITIDIN ER TABLETS 129 MG" -> keep
                        // "PARKITIDIN ER TABLETS 129 MG".
                        // The division name part is limited to a single word so we
                        // don't eat into the main product name.
                        var m = Regex.Match(description,
                            @"^\s*[A-Z0-9]{1,3}\s*-\s*[A-Za-z]+\s+(?<rest>.+)$",
                            RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            var rest = m.Groups["rest"].Value;
                            if (!string.IsNullOrWhiteSpace(rest))
                            {
                                return rest.Trim();
                            }
                        }

                        return description;
                    }

                    Action flushCurrent = () =>
                    {
                        if (!hasCurrent)
                        {
                            return;
                        }

                        // Only persist rows that have a description and some numeric data.
                        if (string.IsNullOrWhiteSpace(currentProductDescription) || !currentHasNumeric)
                        {
                            hasCurrent = false;
                            currentHasNumeric = false;
                            return;
                        }

                        var finalDescription = StripDivisionPrefix(currentProductDescription);

                        var rawParts = new List<string>
                        {
                            currentSrNoText,
                            currentMfgCode,
                            currentCat,
                            finalDescription,
                            currentHsnCode,
                            currentUnitUom,
                            currentBatchNo,
                            currentExpiryText,
                            currentBoxes,
                            currentTotalQty.ToString(CultureInfo.InvariantCulture),
                            currentPricePerUnit.ToString(CultureInfo.InvariantCulture),
                            currentPtr.ToString(CultureInfo.InvariantCulture),
                            currentMrp.ToString(CultureInfo.InvariantCulture),
                            currentTotalValue.ToString(CultureInfo.InvariantCulture),
                            currentDiscPercent.ToString(CultureInfo.InvariantCulture),
                            currentDiscountValue.ToString(CultureInfo.InvariantCulture),
                            currentTaxableValue.ToString(CultureInfo.InvariantCulture),
                            currentCgstRate.ToString(CultureInfo.InvariantCulture),
                            currentCgstAmount.ToString(CultureInfo.InvariantCulture),
                            currentSgstRate.ToString(CultureInfo.InvariantCulture),
                            currentSgstAmount.ToString(CultureInfo.InvariantCulture),
                            currentTotalAmount.ToString(CultureInfo.InvariantCulture)
                        };

                        var rawLineText = string.Join(" ", rawParts.Where(p => !string.IsNullOrWhiteSpace(p)));
                        if (rawLineText.Length > 500)
                        {
                            rawLineText = rawLineText.Substring(0, 500);
                        }

                        db.Database.ExecuteSqlCommand(
                            "INSERT INTO transactionpinvoicedetail (TransactionPInvoiceMasterId, [LineNo], MfgCode, Cat, ProductDescription, HsnCode, UnitUom, BatchNo, ExpiryText, Boxes, TotalQty, PricePerUnit, Ptr, Mrp, TotalValue, DiscPercent, DiscountValue, TaxableValue, CgstRate, CgstAmount, SgstRate, SgstAmount, TotalAmount, RawLineText) " +
                            "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23);",
                            masterId,
                            currentLineNoValue,
                            currentMfgCode,
                            currentCat,
                            finalDescription,
                            currentHsnCode,
                            currentUnitUom,
                            currentBatchNo,
                            currentExpiryText,
                            currentBoxes,
                            currentTotalQty,
                            currentPricePerUnit,
                            currentPtr,
                            currentMrp,
                            currentTotalValue,
                            currentDiscPercent,
                            currentDiscountValue,
                            currentTaxableValue,
                            currentCgstRate,
                            currentCgstAmount,
                            currentSgstRate,
                            currentSgstAmount,
                            currentTotalAmount,
                            rawLineText
                        );

                        // Derive a group header tag (e.g. "B9 - ALTAN") from the
                        // saved description so that it can be reused for the next row
                        // if needed.
                        var headerFromDesc = ExtractGroupHeader(currentProductDescription);
                        if (!string.IsNullOrWhiteSpace(headerFromDesc))
                        {
                            lastGroupHeader = headerFromDesc;
                        }

                        lineNo++;
                        hasCurrent = false;
                        currentHasNumeric = false;
                    };

                    Func<string, string[]> splitLines = text =>
                    {
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            return new string[0];
                        }

                        return text
                            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim())
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .ToArray();
                    };

                    Func<string[], int, string> getLine = (lines, index) =>
                    {
                        if (lines == null || lines.Length == 0)
                        {
                            return string.Empty;
                        }

                        if (index >= 0 && index < lines.Length)
                        {
                            return lines[index];
                        }

                        return string.Empty;
                    };

                    Func<string[], int, string[]> normalizeUnitUomParts = (parts, targetCount) =>
                    {
                        if (targetCount <= 1)
                        {
                            return parts ?? new string[0];
                        }

                        if (parts == null || parts.Length == 0)
                        {
                            return new string[0];
                        }

                        var joined = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
                        if (string.IsNullOrWhiteSpace(joined))
                        {
                            return parts;
                        }

                        var tokenMatches = Regex.Matches(joined, "[A-Za-z0-9]+");
                        var tokens = tokenMatches.Cast<Match>().Select(m => m.Value).ToArray();

                        // If the flattened tokens exactly match the number of logical
                        // lines (Sr.No entries), distribute one token per row. This
                        // handles cases where a single cell has mixed content like
                        // "M78" and "S15 S10" but we still need three UOM codes.
                        if (tokens.Length == targetCount)
                        {
                            return tokens;
                        }

                        return parts;
                    };

                    Func<string[], int, string[]> normalizeBatchParts = (parts, targetCount) =>
                    {
                        if (targetCount <= 1)
                        {
                            return parts ?? new string[0];
                        }

                        if (parts == null || parts.Length == 0)
                        {
                            return new string[0];
                        }

                        var joined = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
                        if (string.IsNullOrWhiteSpace(joined))
                        {
                            return parts;
                        }

                        var tokens = Regex.Matches(joined, @"\S+")
                            .Cast<Match>()
                            .Select(m => m.Value)
                            .ToArray();

                        if (tokens.Length == targetCount)
                        {
                            return tokens;
                        }

                        return parts;
                    };

                    for (int r = firstDataRow; r <= lastDataRow; r++)
                    {
                        var srNoRaw = srNoCol > 0 && srNoCol <= lastCol ? worksheet.Cell(r, srNoCol).GetFormattedString() : string.Empty;

                        string descRaw = string.Empty;
                        if (productDescriptionCol > 0 && productDescriptionCol <= lastCol)
                        {
                            var descCell = worksheet.Cell(r, productDescriptionCol);
                            // Prefer the underlying string/value to ensure we capture
                            // any embedded newlines created with Alt+Enter in Excel.
                            var rawVal = descCell.Value;
                            descRaw = rawVal != null ? rawVal.ToString() : string.Empty;
                            if (string.IsNullOrWhiteSpace(descRaw))
                            {
                                descRaw = descCell.GetFormattedString();
                            }
                        }
                        var hsnRaw = hsnCol > 0 && hsnCol <= lastCol ? worksheet.Cell(r, hsnCol).GetFormattedString() : string.Empty;

                        var mfgRaw = mfgCodeCol > 0 && mfgCodeCol <= lastCol ? worksheet.Cell(r, mfgCodeCol).GetFormattedString() : string.Empty;
                        var catRaw = catCol > 0 && catCol <= lastCol ? worksheet.Cell(r, catCol).GetFormattedString() : string.Empty;
                        var unitUomRaw = unitUomCol > 0 && unitUomCol <= lastCol ? worksheet.Cell(r, unitUomCol).GetFormattedString() : string.Empty;
                        var batchRaw = batchNoCol > 0 && batchNoCol <= lastCol ? worksheet.Cell(r, batchNoCol).GetFormattedString() : string.Empty;
                        var expiryRaw = expiryCol > 0 && expiryCol <= lastCol ? worksheet.Cell(r, expiryCol).GetFormattedString() : string.Empty;
                        var boxesRaw = boxesCol > 0 && boxesCol <= lastCol ? worksheet.Cell(r, boxesCol).GetFormattedString() : string.Empty;

                        var totalQtyRaw = totalQtyCol <= lastCol ? worksheet.Cell(r, totalQtyCol).GetFormattedString() : string.Empty;
                        var pricePerUnitRaw = pricePerUnitCol <= lastCol ? worksheet.Cell(r, pricePerUnitCol).GetFormattedString() : string.Empty;
                        var ptrRaw = ptrCol <= lastCol ? worksheet.Cell(r, ptrCol).GetFormattedString() : string.Empty;
                        var mrpRaw = mrpCol <= lastCol ? worksheet.Cell(r, mrpCol).GetFormattedString() : string.Empty;
                        var totalValueRaw = totalValueCol <= lastCol ? worksheet.Cell(r, totalValueCol).GetFormattedString() : string.Empty;
                        var discPercentRaw = discPercentCol <= lastCol ? worksheet.Cell(r, discPercentCol).GetFormattedString() : string.Empty;
                        var discountValueRaw = discountValueCol <= lastCol ? worksheet.Cell(r, discountValueCol).GetFormattedString() : string.Empty;
                        var taxableValueRaw = taxableValueCol <= lastCol ? worksheet.Cell(r, taxableValueCol).GetFormattedString() : string.Empty;
                        var cgstRateRaw = cgstRateCol <= lastCol ? worksheet.Cell(r, cgstRateCol).GetFormattedString() : string.Empty;
                        var cgstAmountRaw = cgstAmountCol <= lastCol ? worksheet.Cell(r, cgstAmountCol).GetFormattedString() : string.Empty;
                        var sgstRateRaw = sgstRateCol <= lastCol ? worksheet.Cell(r, sgstRateCol).GetFormattedString() : string.Empty;
                        var sgstAmountRaw = sgstAmountCol <= lastCol ? worksheet.Cell(r, sgstAmountCol).GetFormattedString() : string.Empty;
                        var totalAmountRaw = totalAmountCol <= lastCol ? worksheet.Cell(r, totalAmountCol).GetFormattedString() : string.Empty;

                        var srParts = splitLines(srNoRaw);
                        var descParts = splitLines(descRaw);
                        var hsnParts = splitLines(hsnRaw);
                        var mfgParts = splitLines(mfgRaw);
                        var catParts = splitLines(catRaw);
                        var unitUomParts = splitLines(unitUomRaw);
                        var batchParts = splitLines(batchRaw);
                        var expiryParts = splitLines(expiryRaw);
                        var boxesParts = splitLines(boxesRaw);

                        var totalQtyParts = splitLines(totalQtyRaw);
                        var pricePerUnitParts = splitLines(pricePerUnitRaw);
                        var ptrParts = splitLines(ptrRaw);
                        var mrpParts = splitLines(mrpRaw);
                        var totalValueParts = splitLines(totalValueRaw);
                        var discPercentParts = splitLines(discPercentRaw);
                        var discountValueParts = splitLines(discountValueRaw);
                        var taxableValueParts = splitLines(taxableValueRaw);
                        var cgstRateParts = splitLines(cgstRateRaw);
                        var cgstAmountParts = splitLines(cgstAmountRaw);
                        var sgstRateParts = splitLines(sgstRateRaw);
                        var sgstAmountParts = splitLines(sgstAmountRaw);
                        var totalAmountParts = splitLines(totalAmountRaw);

                        var srCount = srParts.Length;
                        if (srCount > 1)
                        {
                            unitUomParts = normalizeUnitUomParts(unitUomParts, srCount);
                            batchParts = normalizeBatchParts(batchParts, srCount);
                        }

                        // Special handling for SUN-style packed rows where a single
                        // physical row contains multiple products and the Product
                        // Description cell holds pairs of lines in the pattern:
                        //   ["B9 - SYMENTA", "XAFINACT 100", "B9 - SYNERGY", "LONAZEP-1", "Z4- SEPHEUS", "PARKITIDIN ER TABLETS 129 MG"]
                        // while S.No / HSN / other key columns have exactly N lines
                        // (1, 2, 3 ...). In this case we want to map each S.No to the
                        // combination of its division prefix and product line, e.g.
                        //   1 -> "B9 - SYMENTA XAFINACT 100"
                        //   2 -> "B9 - SYNERGY LONAZEP-1"
                        //   3 -> "Z4- SEPHEUS PARKITIDIN ER TABLETS 129 MG".
                        if (srParts.Length > 1 && descParts.Length == srParts.Length * 2)
                        {
                            bool looksLikePairedDivisionPattern = true;
                            for (int i = 0; i < srParts.Length; i++)
                            {
                                var prefixCandidate = descParts[i * 2];
                                if (string.IsNullOrWhiteSpace(prefixCandidate) ||
                                    !Regex.IsMatch(prefixCandidate, @"^[A-Z0-9]{1,3}\s*-\s*[A-Za-z]"))
                                {
                                    looksLikePairedDivisionPattern = false;
                                    break;
                                }
                            }

                            if (looksLikePairedDivisionPattern)
                            {
                                var mergedDesc = new string[srParts.Length];
                                for (int i = 0; i < srParts.Length; i++)
                                {
                                    var prefix = descParts[i * 2];
                                    var product = descParts[i * 2 + 1];

                                    if (string.IsNullOrWhiteSpace(prefix))
                                    {
                                        mergedDesc[i] = product;
                                    }
                                    else if (string.IsNullOrWhiteSpace(product))
                                    {
                                        mergedDesc[i] = prefix;
                                    }
                                    else
                                    {
                                        mergedDesc[i] = prefix + " " + product;
                                    }
                                }

                                descParts = mergedDesc;
                            }
                        }

                        // Determine how many logical lines exist in this physical row.
                        // We intentionally base this only on the identifying/textual
                        // columns (Sr No, Product Description, HSN, etc.) so that
                        // extra line breaks in numeric/tax columns (e.g. "Division Total")
                        // do not create additional pseudo-rows.
                        int logicalLines = new[]
                        {
                            srParts.Length,
                            // Do NOT use descParts here – a single product can have
                            // multiple description lines inside the same cell
                            // (e.g. "Z4- SEPHEUS" + "PARKITDIN ER TABLETS 129 MG").
                            // Splitting based on description alone would create
                            // extra pseudo-rows. Instead, drive the number of
                            // logical items from the key identifying columns only.
                            hsnParts.Length,
                            mfgParts.Length,
                            catParts.Length,
                            unitUomParts.Length,
                            batchParts.Length,
                            expiryParts.Length
                            // Do NOT include boxesParts here. Packing/boxes cells can
                            // legitimately contain extra formatting or text lines for
                            // a single product and should not create additional
                            // logical product rows.
                        }.Max();

                        // If only the description column has content (e.g. a second
                        // physical row that continues the product name such as
                        // "PARKITIDIN ER TABLETS 129 MG"), treat it as a single
                        // logical line so that it can be merged as a continuation
                        // of the previous product instead of being skipped.
                        if (logicalLines == 0 && descParts.Length > 0)
                        {
                            logicalLines = 1;
                        }

                        if (logicalLines == 0)
                        {
                            continue;
                        }

                        for (int i = 0; i < logicalLines; i++)
                        {
                            var srNoText = getLine(srParts, i);

                            // For rows that represent a single logical product (based on
                            // Sr No / HSN / Qty etc.), keep the entire multi-line
                            // Product Description together as one text so names like
                            // "Z4 - SEPHEUS" + "PARKITDIN ER TABLETS 129 MG" are not
                            // split into separate products.
                            string descText;
                            if (logicalLines == 1 && descParts.Length > 1)
                            {
                                descText = string.Join(" ", descParts);
                            }
                            else
                            {
                                descText = getLine(descParts, i);
                            }
                            var hsnText = getLine(hsnParts, i);

                            if (string.IsNullOrWhiteSpace(srNoText) && string.IsNullOrWhiteSpace(descText) && string.IsNullOrWhiteSpace(hsnText))
                            {
                                continue;
                            }

                            // Standalone group header row, e.g. "B9 - ALTAN" with no HSN
                            // and no numeric values. Remember it and skip creating a row.
                            if (!string.IsNullOrWhiteSpace(descText)
                                && string.IsNullOrWhiteSpace(hsnText)
                                && Regex.IsMatch(descText, @"^[A-Z0-9]{1,3}\s*-\s*[A-Za-z]"))
                            {
                                lastGroupHeader = descText.Trim();
                                continue;
                            }

                            if (descText.StartsWith("Product Description", StringComparison.OrdinalIgnoreCase) ||
                                hsnText.StartsWith("HSN Code", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (descText.StartsWith("Sub Total", StringComparison.OrdinalIgnoreCase) ||
                                descText.StartsWith("Division Total", StringComparison.OrdinalIgnoreCase) ||
                                descText.StartsWith("B/F", StringComparison.OrdinalIgnoreCase) ||
                                descText.StartsWith("Invoice Total", StringComparison.OrdinalIgnoreCase) ||
                                descText.StartsWith("Grand Total", StringComparison.OrdinalIgnoreCase))
                            {
                                // We've reached summary rows; flush the last pending item and stop.
                                flushCurrent();
                                break;
                            }

                            string mfgCode = getLine(mfgParts, i);
                            string cat = getLine(catParts, i);
                            string unitUom = getLine(unitUomParts, i);
                            string batchNo = getLine(batchParts, i);
                            string expiryText = getLine(expiryParts, i);
                            string boxes = getLine(boxesParts, i);

                            decimal totalQty = ParseDecimalOrZero(getLine(totalQtyParts, i));
                            decimal pricePerUnit = ParseDecimalOrZero(getLine(pricePerUnitParts, i));
                            decimal ptr = ParseDecimalOrZero(getLine(ptrParts, i));
                            decimal mrp = ParseDecimalOrZero(getLine(mrpParts, i));
                            decimal totalValue = ParseDecimalOrZero(getLine(totalValueParts, i));
                            decimal discPercent = ParseDecimalOrZero(getLine(discPercentParts, i));
                            decimal discountValue = ParseDecimalOrZero(getLine(discountValueParts, i));
                            decimal taxableValue = ParseDecimalOrZero(getLine(taxableValueParts, i));

                            if (ptr == 0m && !ptrHeaderDetected)
                            {
                                // No dedicated PTR column was detected from the header. In this
                                // case, derive PTR from TotalValue/TotalQty or fall back to
                                // PricePerUnit. When a PTR column exists we always trust the Excel
                                // value and never recompute it.
                                if (totalQty != 0m && totalValue != 0m)
                                {
                                    ptr = Math.Round(totalValue / totalQty, 2);
                                }
                                else if (pricePerUnit != 0m)
                                {
                                    ptr = pricePerUnit;
                                }
                            }
                            decimal cgstRate = ParseDecimalOrZero(getLine(cgstRateParts, i));
                            decimal cgstAmount = ParseDecimalOrZero(getLine(cgstAmountParts, i));
                            decimal sgstRate = ParseDecimalOrZero(getLine(sgstRateParts, i));
                            decimal sgstAmount = ParseDecimalOrZero(getLine(sgstAmountParts, i));
                            decimal totalAmount = ParseDecimalOrZero(getLine(totalAmountParts, i));

                            // If GST amounts appear to be mis-mapped (e.g. equal to the
                            // taxable value or left as zero while a non-zero rate and
                            // taxable base are present), recompute them from the
                            // taxable value and the corresponding rate.
                            if (taxableValue != 0m && cgstRate != 0m && (cgstAmount == 0m || cgstAmount == taxableValue))
                            {
                                cgstAmount = Math.Round(taxableValue * cgstRate / 100m, 2);
                            }
                            if (taxableValue != 0m && sgstRate != 0m && (sgstAmount == 0m || sgstAmount == taxableValue))
                            {
                                sgstAmount = Math.Round(taxableValue * sgstRate / 100m, 2);
                            }

                            bool hasNumeric =
                                totalQty != 0m ||
                                pricePerUnit != 0m ||
                                ptr != 0m ||
                                mrp != 0m ||
                                totalValue != 0m ||
                                discPercent != 0m ||
                                discountValue != 0m ||
                                taxableValue != 0m ||
                                cgstRate != 0m ||
                                cgstAmount != 0m ||
                                sgstRate != 0m ||
                                sgstAmount != 0m ||
                                totalAmount != 0m;

                            int lineNoValue = lineNo;
                            int parsedSrNo;
                            if (int.TryParse(srNoText, out parsedSrNo) && parsedSrNo > 0)
                            {
                                lineNoValue = parsedSrNo;
                            }

                            if (!hasCurrent)
                            {
                                // Start a new logical item (may initially be description-only).
                                hasCurrent = true;
                                currentHasNumeric = hasNumeric;
                                currentSrNoText = srNoText;
                                currentLineNoValue = lineNoValue;
                                currentMfgCode = mfgCode;
                                currentCat = cat;
                                currentProductDescription = CombineDescription(lastGroupHeader, descText);
                                currentHsnCode = hsnText;
                                currentUnitUom = unitUom;
                                currentBatchNo = batchNo;
                                currentExpiryText = expiryText;
                                currentBoxes = boxes;
                                currentTotalQty = totalQty;
                                currentPricePerUnit = pricePerUnit;
                                currentPtr = ptr;
                                currentMrp = mrp;
                                currentTotalValue = totalValue;
                                currentDiscPercent = discPercent;
                                currentDiscountValue = discountValue;
                                currentTaxableValue = taxableValue;
                                currentCgstRate = cgstRate;
                                currentCgstAmount = cgstAmount;
                                currentSgstRate = sgstRate;
                                currentSgstAmount = sgstAmount;
                                currentTotalAmount = totalAmount;
                            }
                            else
                            {
                                if (hasNumeric)
                                {
                                    // Only merge a numeric row into an existing description-only
                                    // row when the Sr No is empty OR clearly the same. This avoids
                                    // combining multiple distinct products (e.g. three S10 batches
                                    // with SrNo 1,2,3) into a single grid row.
                                    bool sameSrNo =
                                        !string.IsNullOrWhiteSpace(srNoText) &&
                                        !string.IsNullOrWhiteSpace(currentSrNoText) &&
                                        string.Equals(srNoText.Trim(), currentSrNoText.Trim(), StringComparison.OrdinalIgnoreCase);

                                    if (!currentHasNumeric && (string.IsNullOrWhiteSpace(srNoText) || sameSrNo))
                                    {
                                        // We already have description-only lines (like "B9 - AKUNA").
                                        // This numeric row belongs to the same product: merge numeric
                                        // values and extend the description, but keep the original Sr. No.
                                        if (!string.IsNullOrWhiteSpace(descText))
                                        {
                                            if (!string.IsNullOrWhiteSpace(currentProductDescription))
                                            {
                                                currentProductDescription += " " + descText;
                                            }
                                            else
                                            {
                                                currentProductDescription = CombineDescription(lastGroupHeader, descText);
                                            }
                                        }

                                        if (string.IsNullOrWhiteSpace(currentMfgCode)) currentMfgCode = mfgCode;
                                        if (string.IsNullOrWhiteSpace(currentCat)) currentCat = cat;
                                        if (string.IsNullOrWhiteSpace(currentHsnCode)) currentHsnCode = hsnText;
                                        if (string.IsNullOrWhiteSpace(currentUnitUom)) currentUnitUom = unitUom;
                                        if (string.IsNullOrWhiteSpace(currentBatchNo)) currentBatchNo = batchNo;
                                        if (string.IsNullOrWhiteSpace(currentExpiryText)) currentExpiryText = expiryText;
                                        if (string.IsNullOrWhiteSpace(currentBoxes)) currentBoxes = boxes;

                                        currentTotalQty = totalQty;
                                        currentPricePerUnit = pricePerUnit;
                                        currentPtr = ptr;
                                        currentMrp = mrp;
                                        currentTotalValue = totalValue;
                                        currentDiscPercent = discPercent;
                                        currentDiscountValue = discountValue;
                                        currentTaxableValue = taxableValue;
                                        currentCgstRate = cgstRate;
                                        currentCgstAmount = cgstAmount;
                                        currentSgstRate = sgstRate;
                                        currentSgstAmount = sgstAmount;
                                        currentTotalAmount = totalAmount;
                                        currentHasNumeric = true;
                                    }
                                    else
                                    {
                                        // Different Sr No or we already had numeric values: this is a
                                        // new logical item.
                                        flushCurrent();

                                        hasCurrent = true;
                                        currentHasNumeric = hasNumeric;
                                        currentSrNoText = srNoText;
                                        currentLineNoValue = lineNoValue;
                                        currentMfgCode = mfgCode;
                                        currentCat = cat;
                                        currentProductDescription = descText;
                                        currentHsnCode = hsnText;
                                        currentUnitUom = unitUom;
                                        currentBatchNo = batchNo;
                                        currentExpiryText = expiryText;
                                        currentBoxes = boxes;
                                        currentTotalQty = totalQty;
                                        currentPricePerUnit = pricePerUnit;
                                        currentPtr = ptr;
                                        currentMrp = mrp;
                                        currentTotalValue = totalValue;
                                        currentDiscPercent = discPercent;
                                        currentDiscountValue = discountValue;
                                        currentTaxableValue = taxableValue;
                                        currentCgstRate = cgstRate;
                                        currentCgstAmount = cgstAmount;
                                        currentSgstRate = sgstRate;
                                        currentSgstAmount = sgstAmount;
                                        currentTotalAmount = totalAmount;
                                    }
                                }
                                else
                                {
                                    // Continuation row without numeric values (e.g. extra description).
                                    if (!string.IsNullOrWhiteSpace(descText))
                                    {
                                        if (!string.IsNullOrWhiteSpace(currentProductDescription))
                                        {
                                            currentProductDescription += " " + descText;
                                        }
                                        else
                                        {
                                            currentProductDescription = CombineDescription(lastGroupHeader, descText);
                                        }
                                    }

                                    if (string.IsNullOrWhiteSpace(currentMfgCode)) currentMfgCode = mfgCode;
                                    if (string.IsNullOrWhiteSpace(currentCat)) currentCat = cat;
                                    if (string.IsNullOrWhiteSpace(currentHsnCode)) currentHsnCode = hsnText;
                                    if (string.IsNullOrWhiteSpace(currentUnitUom)) currentUnitUom = unitUom;
                                    if (string.IsNullOrWhiteSpace(currentBatchNo)) currentBatchNo = batchNo;
                                    if (string.IsNullOrWhiteSpace(currentExpiryText)) currentExpiryText = expiryText;
                                    if (string.IsNullOrWhiteSpace(currentBoxes)) currentBoxes = boxes;
                                }
                            }
                        }
                    }

                    // Flush the last pending item, if any.
                    flushCurrent();

                    Session["LastPInvUploadBatchId"] = uploadBatchId.ToString();
                    Session["LastPInvMasterTempId"] = masterId;

                    TempData["PInvoiceMasterTempId"] = masterId;

                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error processing purchase invoice Excel: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SaveTemp(string extractedText, string originalFileName, int? supplierId, int? purchaseOrderId)
        {
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                TempData["ErrorMessage"] = "No extracted data to save.";
                return RedirectToAction("Index");
            }

            try
            {
                var uploadedBy = (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                    ? User.Identity.Name
                    : "PurchaseInvoiceUpload";

                if (supplierId.HasValue && supplierId.Value > 0)
                {
                    Session["PurchaseInvoiceUploadSupplierId"] = supplierId.Value;
                }
                if (purchaseOrderId.HasValue && purchaseOrderId.Value > 0)
                {
                    Session["PurchaseInvoiceUploadPoId"] = purchaseOrderId.Value;
                }

                try
                {
                    var lastBatchStr = Session["LastPInvUploadBatchId"] as string;
                    var lastMasterTempObj = Session["LastPInvMasterTempId"];

                    Guid lastBatchGuid;
                    int lastMasterTempId;
                    if (!string.IsNullOrWhiteSpace(lastBatchStr)
                        && Guid.TryParse(lastBatchStr, out lastBatchGuid)
                        && lastMasterTempObj != null
                        && int.TryParse(lastMasterTempObj.ToString(), out lastMasterTempId)
                        && lastMasterTempId > 0)
                    {
                        db.Database.ExecuteSqlCommand(
                            "DELETE FROM transactionpinvoicedetail WHERE TransactionPInvoiceMasterId = @p0; DELETE FROM transactionpinvoicemaster WHERE UploadBatchId = @p1;",
                            lastMasterTempId,
                            lastBatchGuid);
                    }
                }
                catch
                {
                }

                var uploadBatchId = Guid.NewGuid();

                string fullText = extractedText ?? string.Empty;
                var allLines = fullText
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

                string poNumber = null;
                DateTime? poDate = null;
                string customerInfo = null;

                var buyerPoMatch = Regex.Match(fullText,
                    @"Buyer\s*PO\s*No\.?.*?:\s*(?<po>.+?)\s+PO\s*Date\s*:?\s*(?<date>\d{1,2}[./-]\d{1,2}[./-]\d{4})",
                    RegexOptions.IgnoreCase);
                if (buyerPoMatch.Success)
                {
                    poNumber = buyerPoMatch.Groups["po"].Value.Trim();
                    var dateStr = buyerPoMatch.Groups["date"].Value.Trim();
                    DateTime dtPo;
                    if (DateTime.TryParseExact(dateStr,
                        new[] { "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy" },
                        CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out dtPo))
                    {
                        poDate = dtPo;
                    }
                }

                if (string.IsNullOrEmpty(poNumber) || !poDate.HasValue)
                {
                    var orderMatch = Regex.Match(fullText,
                        @"Order number\s*&\s*Date[:]*\s*(?<po>\S+)\s*&\s*(?<date>\d{1,2}[./]\d{1,2}[./]\d{4})",
                        RegexOptions.IgnoreCase);
                    if (orderMatch.Success)
                    {
                        poNumber = orderMatch.Groups["po"].Value.Trim();
                        var dateStr = orderMatch.Groups["date"].Value.Trim();
                        DateTime dtPo;
                        if (DateTime.TryParseExact(dateStr,
                            new[] { "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy" },
                            CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out dtPo))
                        {
                            poDate = dtPo;
                        }
                    }
                }

                if (!poDate.HasValue)
                {
                    var poDateMatch = Regex.Match(fullText,
                        @"PO Date\s*[:]*\s*(?<date>\d{1,2}[./-]\d{1,2}[./-]\d{4})",
                        RegexOptions.IgnoreCase);
                    if (poDateMatch.Success)
                    {
                        var dateStr = poDateMatch.Groups["date"].Value.Trim();
                        DateTime dtPo;
                        if (DateTime.TryParseExact(dateStr,
                            new[] { "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy" },
                            CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out dtPo))
                        {
                            poDate = dtPo;
                        }
                    }
                }

                int custIdx = allLines.FindIndex(l => l.StartsWith("Customer name", StringComparison.OrdinalIgnoreCase));
                if (custIdx >= 0)
                {
                    var sbCust = new StringBuilder();
                    for (int i = custIdx; i < allLines.Count; i++)
                    {
                        var line = allLines[i];
                        if (line.StartsWith("Place", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("Order number", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                        if (sbCust.Length > 0)
                        {
                            sbCust.AppendLine();
                        }
                        sbCust.Append(line);
                    }
                    customerInfo = sbCust.ToString();
                }

                string taxInvoiceNo = null;
                var taxInvMatch = Regex.Match(fullText, @"Tax\s*Invoice\s*No[:.]?\s*(?<inv>[A-Za-z0-9]+)", RegexOptions.IgnoreCase);
                if (taxInvMatch.Success)
                {
                    taxInvoiceNo = taxInvMatch.Groups["inv"].Value.Trim();
                }

                object DbValue(object value) => value ?? (object)DBNull.Value;

                int masterId = db.Database.SqlQuery<int>(
                    "INSERT INTO transactionpinvoicemaster (UploadBatchId, OriginalPdfFileName, UploadedOn, UploadedBy, SupplierId, PurchaseOrderId, PoNumber, PoDate, CustomerInfo, TaxInvoiceNo, FullExtractedText) " +
                    "VALUES (@p0, @p1, GETDATE(), @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    uploadBatchId,
                    originalFileName ?? string.Empty,
                    uploadedBy,
                    DbValue(supplierId),
                    DbValue(purchaseOrderId),
                    DbValue(poNumber),
                    DbValue(poDate),
                    DbValue(customerInfo),
                    DbValue(taxInvoiceNo),
                    fullText
                ).Single();

                // Find the header row for the item grid. In this invoice format the header
                // typically contains both "Product Description" and "HSN Code".
                int itemsHeaderIndex = allLines.FindIndex(l =>
                    l.IndexOf("Product Description", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    l.IndexOf("HSN Code", StringComparison.OrdinalIgnoreCase) >= 0);

                // If we couldn't find the header, fall back to the first line that looks like
                // an item row: starts with a serial number followed by another number.
                int itemsStartIndex;
                if (itemsHeaderIndex >= 0)
                {
                    itemsStartIndex = itemsHeaderIndex + 1;
                }
                else
                {
                    itemsStartIndex = allLines.FindIndex(l => Regex.IsMatch(l, @"^\d+\s+\d+\s+"));
                    if (itemsStartIndex < 0)
                    {
                        itemsStartIndex = 0;
                    }
                }

                var itemBlocks = new List<string>();
                StringBuilder currentItem = null;
                bool seenFirstItem = false;

                for (int i = itemsStartIndex; i < allLines.Count; i++)
                {
                    var line = allLines[i];

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    // Treat division totals, carried-forward totals and brought-forward lines
                    // as separators between detail rows. They should not be appended to any
                    // item block because they contain aggregate values, not row-level data.
                    if (Regex.IsMatch(line, @"^(Division Total|Sub Total|Sub Total C/F|Sub Total CIF|B/F)\b", RegexOptions.IgnoreCase))
                    {
                        if (currentItem != null)
                        {
                            itemBlocks.Add(currentItem.ToString().Trim());
                            currentItem = null;
                        }
                        continue;
                    }

                    // Treat group headers such as "B9 - AKUNA", "Z4 - AMAZE" as separators
                    // as well so that they are not merged into any item row.
                    if (Regex.IsMatch(line, @"^(B9|Z4)\s*-", RegexOptions.IgnoreCase))
                    {
                        if (currentItem != null)
                        {
                            itemBlocks.Add(currentItem.ToString().Trim());
                            currentItem = null;
                        }
                        continue;
                    }

                    // Only start looking for final total lines after we have started parsing
                    // at least one item row, so that summary panels at the top of the page
                    // don't stop the parsing prematurely.
                    if (seenFirstItem && (
                        line.StartsWith("Grand Total", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Invoice Total", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Total Amount", StringComparison.OrdinalIgnoreCase)))
                    {
                        break;
                    }

                    // Start of a new item row: line begins with a serial number (e.g. "1 ")
                    bool startsWithNumber = Regex.IsMatch(line, @"^\d+\s+");

                    if (startsWithNumber)
                    {
                        seenFirstItem = true;

                        if (currentItem != null)
                        {
                            itemBlocks.Add(currentItem.ToString().Trim());
                        }
                        currentItem = new StringBuilder();
                        currentItem.Append(line);
                    }
                    else if (currentItem != null)
                    {
                        // Continuation of previous row (description wraps to next lines)
                        currentItem.Append(" " + line);
                    }
                }

                if (currentItem != null)
                {
                    itemBlocks.Add(currentItem.ToString().Trim());
                }

                int lineNo = 1;

                foreach (var block in itemBlocks)
                {
                    var normalized = Regex.Replace(block, @"\s+", " ").Trim();
                    var tokens = normalized.Split(' ');
                    if (tokens.Length < 8)
                    {
                        continue;
                    }

                    // Map Sr. No., MfgCode and CAT from the first tokens
                    int lineNoValue = lineNo;
                    int parsedSrNo;
                    if (int.TryParse(tokens[0], out parsedSrNo) && parsedSrNo > 0)
                    {
                        lineNoValue = parsedSrNo;
                    }

                    string mfgCode = tokens.Length > 1 ? tokens[1] : string.Empty;
                    string cat = tokens.Length > 2 ? tokens[2] : string.Empty;

                    // In this invoice layout each detail row looks roughly like:
                    // SrNo  MfgCode  CAT  Product Description ...  HSNCode  UnitUOM  BatchNo  Expiry  Boxes  TotalQty  PricePerUnit  PTR  MRP  TotalValue ...
                    // We skip the first 3 columns and treat the first 6-8 digit code
                    // (or a token containing such digits) after the description as the HSN Code.

                    int hsnIndex = -1;
                    string hsnCode = null;
                    for (int idx = 3; idx < tokens.Length; idx++)
                    {
                        var m = Regex.Match(tokens[idx], @"\d{6,8}");
                        if (m.Success)
                        {
                            hsnIndex = idx;
                            hsnCode = m.Value;
                            break;
                        }
                    }

                    if (hsnIndex <= 3 || string.IsNullOrEmpty(hsnCode))
                    {
                        // Could not find a plausible HSN code; skip this row
                        continue;
                    }

                    // Product description: everything from index 3 (after SrNo, MfgCode, CAT)
                    // up to just before the HSN code.
                    string productDescription = string.Join(" ", tokens.Skip(3).Take(hsnIndex - 3));

                    // In the SUN Pharma layout the columns immediately after HSN are:
                    // Unit UOM, Batch No., Expiry, No. of Boxes/Shipper, Total Qty,
                    // Price per Unit, P.T.R., M.R.P., Total Value, then discount/tax columns.
                    string unitUom = hsnIndex + 1 < tokens.Length ? tokens[hsnIndex + 1] : string.Empty;
                    string batchNo = hsnIndex + 2 < tokens.Length ? tokens[hsnIndex + 2] : string.Empty;
                    string expiryText = hsnIndex + 3 < tokens.Length ? tokens[hsnIndex + 3] : string.Empty;
                    string boxes = hsnIndex + 4 < tokens.Length ? tokens[hsnIndex + 4] : string.Empty;
                    if (!string.IsNullOrEmpty(boxes) && boxes.Contains("/"))
                    {
                        var parts = boxes.Split('/');
                        if (parts.Length > 0)
                        {
                            boxes = parts[0];
                        }
                    }

                    decimal ParseDecimalOrZero(string s)
                    {
                        if (string.IsNullOrWhiteSpace(s)) return 0m;

                        // Remove thousands separators and any non-numeric/non-decimal characters
                        s = s.Replace(",", string.Empty);
                        s = Regex.Replace(s, @"[^0-9.\-]", string.Empty);

                        if (string.IsNullOrWhiteSpace(s) || s == "." || s == "-" || s == "-.")
                        {
                            return 0m;
                        }

                        decimal val;
                        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                        {
                            return val;
                        }
                        return 0m;
                    }

                    decimal totalQty = 0m;
                    decimal pricePerUnit = 0m;
                    decimal ptr = 0m;
                    decimal mrp = 0m;
                    decimal totalValue = 0m;
                    decimal discPercent = 0m;
                    decimal discountValue = 0m;
                    decimal taxableValue = 0m;
                    decimal cgstRate = 0m;
                    decimal cgstAmount = 0m;
                    decimal sgstRate = 0m;
                    decimal sgstAmount = 0m;
                    decimal totalAmount = 0m;

                    // Collect all numeric-looking tokens after Boxes. This is more robust
                    // than relying strictly on fixed positions because some PDFs may wrap
                    // or inject extra spacing, but the order of the numeric columns remains
                    // consistent: TotalQty, PricePerUnit, PTR, MRP, TotalValue, Disc%,
                    // DiscountValue, TaxableValue, CGST Rate, CGST Amount, SGST Rate,
                    // SGST Amount, Total Amount.
                    var numericTokens = new List<string>();
                    for (int idx = hsnIndex + 5; idx < tokens.Length && numericTokens.Count < 13; idx++)
                    {
                        if (Regex.IsMatch(tokens[idx], @"^-?[0-9,]+(\.[0-9]+)?$"))
                        {
                            numericTokens.Add(tokens[idx]);
                        }
                    }

                    if (numericTokens.Count > 0) totalQty = ParseDecimalOrZero(numericTokens[0]);
                    if (numericTokens.Count > 1) pricePerUnit = ParseDecimalOrZero(numericTokens[1]);
                    if (numericTokens.Count > 2) ptr = ParseDecimalOrZero(numericTokens[2]);
                    if (numericTokens.Count > 3) mrp = ParseDecimalOrZero(numericTokens[3]);
                    if (numericTokens.Count > 4) totalValue = ParseDecimalOrZero(numericTokens[4]);
                    if (numericTokens.Count > 5) discPercent = ParseDecimalOrZero(numericTokens[5]);
                    if (numericTokens.Count > 6) discountValue = ParseDecimalOrZero(numericTokens[6]);
                    if (numericTokens.Count > 7) taxableValue = ParseDecimalOrZero(numericTokens[7]);

                    if (ptr == 0m)
                    {
                        if (totalQty != 0m && totalValue != 0m)
                        {
                            ptr = Math.Round(totalValue / totalQty, 2);
                        }
                        else if (pricePerUnit != 0m)
                        {
                            ptr = pricePerUnit;
                        }
                    }
                    if (numericTokens.Count > 8) cgstRate = ParseDecimalOrZero(numericTokens[8]);
                    if (numericTokens.Count > 9) cgstAmount = ParseDecimalOrZero(numericTokens[9]);
                    if (numericTokens.Count > 10) sgstRate = ParseDecimalOrZero(numericTokens[10]);
                    if (numericTokens.Count > 11) sgstAmount = ParseDecimalOrZero(numericTokens[11]);
                    if (numericTokens.Count > 12) totalAmount = ParseDecimalOrZero(numericTokens[12]);

                    var rawLineText = block.Length > 500 ? block.Substring(0, 500) : block;

                    db.Database.ExecuteSqlCommand(
                        "INSERT INTO transactionpinvoicedetail (TransactionPInvoiceMasterId, [LineNo], MfgCode, Cat, ProductDescription, HsnCode, UnitUom, BatchNo, ExpiryText, Boxes, TotalQty, PricePerUnit, Ptr, Mrp, TotalValue, DiscPercent, DiscountValue, TaxableValue, CgstRate, CgstAmount, SgstRate, SgstAmount, TotalAmount, RawLineText) " +
                        "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23);",
                        masterId,
                        lineNoValue,
                        mfgCode,
                        cat,
                        productDescription,
                        hsnCode,
                        unitUom,
                        batchNo,
                        expiryText,
                        boxes,
                        totalQty,
                        pricePerUnit,
                        ptr,
                        mrp,
                        totalValue,
                        discPercent,
                        discountValue,
                        taxableValue,
                        cgstRate,
                        cgstAmount,
                        sgstRate,
                        sgstAmount,
                        totalAmount,
                        rawLineText
                    );

                    lineNo++;
                }

                Session["LastPInvUploadBatchId"] = uploadBatchId.ToString();
                Session["LastPInvMasterTempId"] = masterId;

                TempData["PInvoiceMasterTempId"] = masterId;

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error processing purchase invoice PDF: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ConfirmUploadedInvoice(int masterTempId, int[] lineNos, int[] actualMaterialIds, int[] packingIds)
        {
            if (masterTempId <= 0)
            {
                TempData["ErrorMessage"] = "Invoice information is missing. Please upload the file again.";
                return RedirectToAction("Index");
            }

            if (lineNos == null || actualMaterialIds == null || packingIds == null ||
                lineNos.Length == 0 || lineNos.Length != actualMaterialIds.Length || lineNos.Length != packingIds.Length)
            {
                TempData["ErrorMessage"] = "Unable to confirm invoice details. Please try again.";
                return RedirectToAction("Index");
            }

            var selections = new List<PurchaseInvoiceDetailSelection>();
            for (int i = 0; i < lineNos.Length; i++)
            {
                int matId = actualMaterialIds[i];
                int packId = packingIds[i];
                if (matId > 0)
                {
                    selections.Add(new PurchaseInvoiceDetailSelection
                    {
                        LineNo = lineNos[i],
                        MaterialId = matId,
                        PackingId = packId
                    });
                }
            }

            if (!selections.Any())
            {
                TempData["ErrorMessage"] = "No rows with selected Actual Product to save.";
                return RedirectToAction("Index");
            }

            try
            {
                var masterHeader = db.Database.SqlQuery<TransactionPInvoiceMasterHeaderRow>(
                    "SELECT Id, SupplierId, PurchaseOrderId, PoNumber, PoDate, TaxInvoiceNo FROM transactionpinvoicemaster WHERE Id = @p0;",
                    new SqlParameter("@p0", masterTempId)
                ).FirstOrDefault();

                if (masterHeader == null)
                {
                    TempData["ErrorMessage"] = "Purchase invoice header not found. Please upload again.";
                    return RedirectToAction("Index");
                }

                if (!string.IsNullOrEmpty(masterHeader.TaxInvoiceNo))
                {
                    var isDuplicate = db.TransactionMasters.Any(t => t.REGSTRID == PurchaseInvoiceRegisterId && t.TRANREFNO == masterHeader.TaxInvoiceNo);
                    if (isDuplicate)
                    {
                        TempData["ErrorMessage"] = "Duplicate Tax Invoice No found: " + masterHeader.TaxInvoiceNo + ". This invoice has already been entered.";
                        return RedirectToAction("Index");
                    }
                }

                if (!masterHeader.SupplierId.HasValue || masterHeader.SupplierId.Value <= 0)
                {
                    TempData["ErrorMessage"] = "Supplier information for this invoice is missing. Please upload again.";
                    return RedirectToAction("Index");
                }

                int supplierId = masterHeader.SupplierId.Value;
                var supplier = db.SupplierMasters.FirstOrDefault(s => s.CATEID == supplierId);
                if (supplier == null)
                {
                    TempData["ErrorMessage"] = "Selected supplier no longer exists. Please upload again.";
                    return RedirectToAction("Index");
                }

                short tranStateType = 0;
                var state = db.StateMasters.FirstOrDefault(s => s.STATEID == supplier.STATEID);
                if (state != null)
                {
                    tranStateType = state.STATETYPE;
                }

                var tempDetails = db.Database.SqlQuery<TransactionPInvoiceDetailRow>(
                    "SELECT TransactionPInvoiceMasterId, [LineNo], MfgCode, Cat, ProductDescription, HsnCode, UnitUom, BatchNo, ExpiryText, Boxes, TotalQty, PricePerUnit, Ptr, Mrp, TotalValue, DiscPercent, DiscountValue, TaxableValue, CgstRate, CgstAmount, SgstRate, SgstAmount, TotalAmount " +
                    "FROM transactionpinvoicedetail WHERE TransactionPInvoiceMasterId = @p0;",
                    new SqlParameter("@p0", masterTempId)
                ).ToList();

                if (!tempDetails.Any())
                {
                    TempData["ErrorMessage"] = "No invoice detail rows were found for this upload. Please upload again.";
                    return RedirectToAction("Index");
                }

                var lineSet = new HashSet<int>(selections.Select(s => s.LineNo));

                var detailMap = tempDetails
                    .Where(d => lineSet.Contains(d.LineNo))
                    .ToDictionary(d => d.LineNo, d => d);

                var materialIds = selections.Select(s => s.MaterialId).Distinct().ToList();
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

                var calcRows = new List<PurchaseInvoiceDetailCalcRow>();

                foreach (var sel in selections)
                {
                    TransactionPInvoiceDetailRow tempRow;
                    if (!detailMap.TryGetValue(sel.LineNo, out tempRow))
                    {
                        continue;
                    }

                    MaterialMaster material;
                    materialMap.TryGetValue(sel.MaterialId, out material);

                    int hsnId = material != null ? material.HSNID : 0;
                    HSNCodeMaster hsn = null;
                    if (hsnId > 0)
                    {
                        hsnMap.TryGetValue(hsnId, out hsn);
                    }

                    decimal qty = tempRow.TotalQty;
                    decimal rate = tempRow.PricePerUnit;
                    if (rate <= 0 && material != null && material.RATE > 0)
                    {
                        rate = material.RATE;
                    }

                    decimal profitPercent = material != null ? material.MTRLPRFT : 0m;

                    decimal taxable = tempRow.TaxableValue;
                    if (taxable <= 0)
                    {
                        taxable = qty * rate;
                    }

                    decimal cgstAmt = 0m;
                    decimal sgstAmt = 0m;
                    decimal igstAmt = 0m;

                    if (hsn != null)
                    {
                        if (tranStateType == 0)
                        {
                            if (hsn.CGSTEXPRN > 0)
                            {
                                cgstAmt = Math.Round((taxable * hsn.CGSTEXPRN) / 100m, 2);
                            }

                            if (hsn.SGSTEXPRN > 0)
                            {
                                sgstAmt = Math.Round((taxable * hsn.SGSTEXPRN) / 100m, 2);
                            }
                        }
                        else
                        {
                            if (hsn.IGSTEXPRN > 0)
                            {
                                igstAmt = Math.Round((taxable * hsn.IGSTEXPRN) / 100m, 2);
                            }
                        }
                    }

                    decimal net = taxable + cgstAmt + sgstAmt + igstAmt;

                    var row = new PurchaseInvoiceDetailCalcRow
                    {
                        LineNo = sel.LineNo,
                        MaterialId = material != null ? material.MTRLID : sel.MaterialId,
                        PackingId = sel.PackingId,
                        HsnId = hsnId,
                        MaterialCode = material != null ? material.MTRLCODE : string.Empty,
                        MaterialName = material != null ? material.MTRLDESC : tempRow.ProductDescription,
                        ProfitPercent = profitPercent,
                        Qty = qty,
                        Rate = rate,
                        Taxable = taxable,
                        Cgst = cgstAmt,
                        Sgst = sgstAmt,
                        Igst = igstAmt,
                        Net = net
                    };

                    calcRows.Add(row);
                }

                if (!calcRows.Any())
                {
                    TempData["ErrorMessage"] = "No valid invoice rows to save. Please ensure Actual Product is selected.";
                    return RedirectToAction("Index");
                }

                decimal totalTaxable = calcRows.Sum(r => r.Taxable);
                decimal totalCgst = calcRows.Sum(r => r.Cgst);
                decimal totalSgst = calcRows.Sum(r => r.Sgst);
                decimal totalIgst = calcRows.Sum(r => r.Igst);
                decimal totalNet = calcRows.Sum(r => r.Net);

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == PurchaseInvoiceRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;
                string trandNo = nextTranNo.ToString("D4");

                int cusrId = 0;
                var sessUsr = Session["CUSRID"];
                if (sessUsr != null)
                {
                    int.TryParse(sessUsr.ToString(), out cusrId);
                }

                int lmusId = cusrId;

                string userNameForTran;
                if (Session["CUSRID"] != null)
                {
                    userNameForTran = Session["CUSRID"].ToString();
                }
                else if (Session["USERNAME"] != null)
                {
                    userNameForTran = Session["USERNAME"].ToString();
                }
                else if (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                {
                    userNameForTran = User.Identity.Name;
                }
                else
                {
                    userNameForTran = "System";
                }

                string poNumberRef = masterHeader.PoNumber;
                if (string.IsNullOrWhiteSpace(poNumberRef))
                {
                    poNumberRef = "-";
                }

                // Use TaxInvoiceNo for TRANREFNO if available, otherwise fallback to PO Number
                string tranRefNo = !string.IsNullOrEmpty(masterHeader.TaxInvoiceNo) ? masterHeader.TaxInvoiceNo : poNumberRef;

                DateTime tranDate = masterHeader.PoDate ?? DateTime.Today;
                DateTime tranTime = DateTime.Now;
                DateTime prcsDate = DateTime.Now;

                var pCompyId = new SqlParameter("@COMPYID", SqlDbType.Int) { Value = compyId };
                var pSdptId = new SqlParameter("@SDPTID", SqlDbType.Int) { Value = 0 };
                var pRegstrId = new SqlParameter("@REGSTRID", SqlDbType.Int) { Value = PurchaseInvoiceRegisterId };
                var pTranBType = new SqlParameter("@TRANBTYPE", SqlDbType.Int) { Value = 0 };
                var pTranDate = new SqlParameter("@TRANDATE", SqlDbType.DateTime) { Value = tranDate };
                var pTranTime = new SqlParameter("@TRANTIME", SqlDbType.DateTime) { Value = tranTime };
                var pTranNo = new SqlParameter("@TRANNO", SqlDbType.Int) { Value = nextTranNo };
                var pTrandNo = new SqlParameter("@TRANDNO", SqlDbType.VarChar, 25) { Value = trandNo };
                var pTranRefId = new SqlParameter("@TRANREFID", SqlDbType.Int) { Value = supplierId };
                var pTranRefName = new SqlParameter("@TRANREFNAME", SqlDbType.VarChar, 100) { Value = (object)supplier.CATENAME ?? DBNull.Value };
                var pTranStateType = new SqlParameter("@TRANSTATETYPE", SqlDbType.Int) { Value = (int)tranStateType };
                var pTranRefNo = new SqlParameter("@TRANREFNO", SqlDbType.VarChar, 25) { Value = tranRefNo };
                var pTranGAmt = new SqlParameter("@TRANGAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalTaxable };
                var pTranCgstAmt = new SqlParameter("@TRANCGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalCgst };
                var pTranSgstAmt = new SqlParameter("@TRANSGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalSgst };
                var pTranIgstAmt = new SqlParameter("@TRANIGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalIgst };
                var pTranNAmt = new SqlParameter("@TRANNAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalNet };
                var pTranAmtWrds = new SqlParameter("@TRANAMTWRDS", SqlDbType.VarChar, 250) { Value = (object)ConvertAmountToWords(totalNet) ?? DBNull.Value };
                var pTranLMId = new SqlParameter("@TRANLMID", SqlDbType.Int) { Value = masterHeader.PurchaseOrderId ?? 0 };
                var pTranPCount = new SqlParameter("@TRANPCOUNT", SqlDbType.Int) { Value = 0 };
                var pTranNartn = new SqlParameter("@TRANNARTN", SqlDbType.VarChar) { Value = DBNull.Value };
                var pTranRmks = new SqlParameter("@TRANRMKS", SqlDbType.VarChar) { Value = DBNull.Value };
                var pExprtStatus = new SqlParameter("@EXPRTSTATUS", SqlDbType.Int) { Value = 0 };
                var pCusrId = new SqlParameter("@CUSRID", SqlDbType.Int) { Value = cusrId };
                var pLmusId = new SqlParameter("@LMUSRID", SqlDbType.Int) { Value = lmusId };
                var pDispStatus = new SqlParameter("@DISPSTATUS", SqlDbType.Int) { Value = 0 };
                var pPrcsDate = new SqlParameter("@PRCSDATE", SqlDbType.DateTime) { Value = prcsDate };
                var pOutId = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };

                db.Database.ExecuteSqlCommand(
                    "EXEC PR_TRANSACTIONMASTER_INSRT @COMPYID, @SDPTID, @REGSTRID, @TRANBTYPE, @TRANDATE, @TRANTIME, @TRANNO, @TRANDNO, @TRANREFID, @TRANREFNAME, @TRANSTATETYPE, @TRANREFNO, @TRANGAMT, @TRANCGSTAMT, @TRANSGSTAMT, @TRANIGSTAMT, @TRANNAMT, @TRANAMTWRDS, @TRANLMID, @TRANPCOUNT, @TRANNARTN, @TRANRMKS, @EXPRTSTATUS, @CUSRID, @LMUSRID, @DISPSTATUS, @PRCSDATE, @ID OUT",
                    pCompyId,
                    pSdptId,
                    pRegstrId,
                    pTranBType,
                    pTranDate,
                    pTranTime,
                    pTranNo,
                    pTrandNo,
                    pTranRefId,
                    pTranRefName,
                    pTranStateType,
                    pTranRefNo,
                    pTranGAmt,
                    pTranCgstAmt,
                    pTranSgstAmt,
                    pTranIgstAmt,
                    pTranNAmt,
                    pTranAmtWrds,
                    pTranLMId,
                    pTranPCount,
                    pTranNartn,
                    pTranRmks,
                    pExprtStatus,
                    pCusrId,
                    pLmusId,
                    pDispStatus,
                    pPrcsDate,
                    pOutId);

                int tranmid;
                if (pOutId.Value == null || !int.TryParse(pOutId.Value.ToString(), out tranmid) || tranmid <= 0)
                {
                    throw new Exception("Failed to create TransactionMaster record for purchase invoice.");
                }

                try
                {
                    var createdMaster = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == tranmid);
                    if (createdMaster != null)
                    {
                        createdMaster.CUSRID = userNameForTran;
                        createdMaster.LMUSRID = userNameForTran;
                        db.SaveChanges();
                    }
                }
                catch
                {
                }

                foreach (var r in calcRows)
                {
                    var pPtranMid = new SqlParameter("@PTRANMID", SqlDbType.Int) { Value = tranmid };
                    var pPtrandRefId = new SqlParameter("@PTRANDREFID", SqlDbType.Int) { Value = r.MaterialId };
                    var pPtrandRefNo = new SqlParameter("@PTRANDREFNO", SqlDbType.VarChar, 25) { Value = (object)r.MaterialCode ?? string.Empty };
                    var pPtrandRefName = new SqlParameter("@PTRANDREFNAME", SqlDbType.VarChar, 100) { Value = (object)r.MaterialName ?? string.Empty };
                    var pPtrandMtrlPrft = new SqlParameter("@PTRANDMTRLPRFT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.ProfitPercent };
                    var pPhsnId = new SqlParameter("@PHSNID", SqlDbType.Int) { Value = r.HsnId };
                    var pPpackmId = new SqlParameter("@PPACKMID", SqlDbType.Int) { Value = r.PackingId };
                    var pPtrandQty = new SqlParameter("@PTRANDQTY", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Qty };
                    var pPtrandRate = new SqlParameter("@PTRANDRATE", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Rate };
                    var pPtrandGAmt = new SqlParameter("@PTRANDGAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Taxable };
                    var pPtrandCgstAmt = new SqlParameter("@PTRANDCGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Cgst };
                    var pPtrandSgstAmt = new SqlParameter("@PTRANDSGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Sgst };
                    var pPtrandIgstAmt = new SqlParameter("@PTRANDIGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Igst };
                    var pPtrandNAmt = new SqlParameter("@PTRANDNAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Net };
                    var pPtrandAid = new SqlParameter("@PTRANDAID", SqlDbType.Int) { Value = 0 };
                    var pPtrandNartn = new SqlParameter("@PTRANDNARTN", SqlDbType.VarChar) { Value = DBNull.Value };
                    var pPtrandRmks = new SqlParameter("@PTRANDRMKS", SqlDbType.VarChar) { Value = DBNull.Value };
                    var pPtrandARate = new SqlParameter("@PTRANDARATE", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Rate };
                    var pDetOutId = new SqlParameter("@id", SqlDbType.Int) { Direction = ParameterDirection.Output };

                    db.Database.ExecuteSqlCommand(
                        "EXEC PR_TRANSACTIONDETAIL_INSRT @PTRANMID, @PTRANDREFID, @PTRANDREFNO, @PTRANDREFNAME, @PTRANDMTRLPRFT, @PHSNID, @PPACKMID, @PTRANDQTY, @PTRANDRATE, @PTRANDGAMT, @PTRANDCGSTAMT, @PTRANDSGSTAMT, @PTRANDIGSTAMT, @PTRANDNAMT, @PTRANDAID, @PTRANDNARTN, @PTRANDRMKS, @PTRANDARATE, @id OUT",
                        pPtranMid,
                        pPtrandRefId,
                        pPtrandRefNo,
                        pPtrandRefName,
                        pPtrandMtrlPrft,
                        pPhsnId,
                        pPpackmId,
                        pPtrandQty,
                        pPtrandRate,
                        pPtrandGAmt,
                        pPtrandCgstAmt,
                        pPtrandSgstAmt,
                        pPtrandIgstAmt,
                        pPtrandNAmt,
                        pPtrandAid,
                        pPtrandNartn,
                        pPtrandRmks,
                        pPtrandARate,
                        pDetOutId);

                    int trandId;
                    if (pDetOutId.Value != null && int.TryParse(pDetOutId.Value.ToString(), out trandId) && trandId > 0)
                    {
                        TransactionPInvoiceDetailRow tempRowForBatch;
                        if (detailMap.TryGetValue(r.LineNo, out tempRowForBatch))
                        {
                            int boxesInt = 0;
                            if (!string.IsNullOrWhiteSpace(tempRowForBatch.Boxes))
                            {
                                string rawBoxes = tempRowForBatch.Boxes;
                                if (rawBoxes.Contains("/"))
                                {
                                    rawBoxes = rawBoxes.Split('/')[0];
                                }
                                var digitsOnly = Regex.Replace(rawBoxes, @"[^0-9]", string.Empty);
                                if (!string.IsNullOrWhiteSpace(digitsOnly))
                                {
                                    int.TryParse(digitsOnly, out boxesInt);
                                }
                            }

                            int totalQtyInt = (int)Math.Round(tempRowForBatch.TotalQty);
                            int tranPQty = totalQtyInt;
                            if (boxesInt > 0 && totalQtyInt > 0)
                            {
                                tranPQty = (int)Math.Round((decimal)totalQtyInt / boxesInt);
                            }

                            DateTime expiryDate = DateTime.Today;
                            var expiryText = tempRowForBatch.ExpiryText;
                            if (!string.IsNullOrWhiteSpace(expiryText))
                            {
                                DateTime parsed;
                                bool successfullyParsed = false;

                                var monthYearFormats = new[] { "MM/yy", "M/yy", "MM/yyyy", "M/yyyy", "MM-yy", "M-yy", "MM-yyyy", "M-yyyy" };
                                foreach (var fmt in monthYearFormats)
                                {
                                    if (DateTime.TryParseExact(expiryText, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                                    {
                                        expiryDate = new DateTime(parsed.Year, parsed.Month, 1);
                                        successfullyParsed = true;
                                        break;
                                    }
                                }

                                if (!successfullyParsed)
                                {
                                    if (DateTime.TryParse(expiryText, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) ||
                                        DateTime.TryParse(expiryText, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out parsed))
                                    {
                                        expiryDate = parsed;
                                    }
                                    else
                                    {
                                        var fullDateFormats = new[] { "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy" };
                                        foreach (var fmt in fullDateFormats)
                                        {
                                            if (DateTime.TryParseExact(expiryText, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                                            {
                                                expiryDate = parsed;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            db.Database.ExecuteSqlCommand(
                                "INSERT INTO TRANSACTIONBATCHDETAIL (TRANDID, AMTRLID, HSNID, STKBID, TRANBDNO, TRANBEXPDATE, PACKMID, TRANPQTY, TRANBQTY, TRANBRATE, TRANBPTRRATE, TRANBMRP, TRANBGAMT, TRANBCGSTEXPRN, TRANBSGSTEXPRN, TRANBIGSTEXPRN, TRANBCGSTAMT, TRANBSGSTAMT, TRANBIGSTAMT, TRANBNAMT, TRANBPID, TRANDPID, TRANPTQTY) " +
                                "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22);",
                                trandId,
                                r.MaterialId,
                                r.HsnId,
                                0,
                                (object)(tempRowForBatch.BatchNo ?? string.Empty),
                                expiryDate,
                                r.PackingId,
                                tranPQty,
                                boxesInt,
                                tempRowForBatch.PricePerUnit,
                                tempRowForBatch.Ptr,
                                tempRowForBatch.Mrp,
                                tempRowForBatch.TotalValue,
                                tempRowForBatch.CgstRate,
                                tempRowForBatch.SgstRate,
                                0m,
                                tempRowForBatch.CgstAmount,
                                tempRowForBatch.SgstAmount,
                                0m,
                                tempRowForBatch.TotalAmount,
                                0,
                                0,
                                totalQtyInt);
                        }
                    }
                }

                try
                {
                    var lastBatchStr = Session["LastPInvUploadBatchId"] as string;
                    Guid batchGuid;
                    if (!string.IsNullOrWhiteSpace(lastBatchStr) && Guid.TryParse(lastBatchStr, out batchGuid))
                    {
                        db.Database.ExecuteSqlCommand(
                            "DELETE FROM transactionpinvoicedetail WHERE TransactionPInvoiceMasterId = @p0; DELETE FROM transactionpinvoicemaster WHERE UploadBatchId = @p1;",
                            masterTempId,
                            batchGuid);

                        Session["LastPInvUploadBatchId"] = null;
                        Session["LastPInvMasterTempId"] = null;
                    }

                    Session["PurchaseInvoiceUploadSupplierId"] = null;
                    Session["PurchaseInvoiceUploadPoId"] = null;
                }
                catch
                {
                }

                TempData["SuccessMessage"] = "Purchase invoice uploaded successfully.";
                return RedirectToAction("Index", "PurchaseInvoice", new { area = "" });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error confirming purchase invoice details: " + ex.Message;
                return RedirectToAction("Index");
            }
        }
    }
}
