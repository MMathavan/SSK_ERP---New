using System;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Text;
using System.Linq;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    [Authorize(Roles = "SalesOrderCreate")]
    public class SalesOrderUploadController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesOrderRegisterId = 1;

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

        private void PopulateCustomerList(int? selectedCustomerId = null)
        {
            var customerList = db.CustomerMasters
                .Where(c => c.DISPSTATUS == 0)
                .OrderBy(c => c.CATENAME)
                .Select(c => new
                {
                    c.CATEID,
                    c.CATENAME
                })
                .ToList();

            ViewBag.CustomerList = new SelectList(customerList, "CATEID", "CATENAME", selectedCustomerId);
        }

        [HttpGet]
        public ActionResult Index()
        {
            int? selectedCustomerId = null;

            var uploadCustomerIdObj = TempData["UploadCustomerId"] ?? Session["SalesOrderUploadCustomerId"];
            if (uploadCustomerIdObj != null)
            {
                int parsedCustomerId;
                if (int.TryParse(uploadCustomerIdObj.ToString(), out parsedCustomerId) && parsedCustomerId > 0)
                {
                    selectedCustomerId = parsedCustomerId;
                }
            }

            PopulateCustomerList(selectedCustomerId);

            var uploadBatchIdObj = TempData["UploadBatchId"];
            var masterTempIdObj = TempData["TransactionMasterTempId"];

            if (masterTempIdObj != null)
            {
                int masterTempId;
                if (int.TryParse(masterTempIdObj.ToString(), out masterTempId) && masterTempId > 0)
                {
                    // Determine current user id / name from session for the detail-material mapping SP
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

                    // Use stored procedure PR_TRANSACTIONDETAILMATERIAL_DETAILS to get temp rows with material info
                    var allDetails = db.Database.SqlQuery<TransactionDetailTempRow>(
                        "EXEC PR_TRANSACTIONDETAILMATERIAL_DETAILS @kusrid",
                        userParam
                    ).ToList();

                    // Filter to the current TransactionMasterTempId and ensure stable ordering by LineNo
                    var tempDetails = allDetails
                        .Where(d => d.TransactionMasterTempId == masterTempId)
                        .OrderBy(d => d.LineNo)
                        .ToList();

                    if (tempDetails.Any())
                    {
                        var matchedMaterialIds = tempDetails
                            .Where(d => d.MTRLID.HasValue && d.MTRLID.Value > 0)
                            .Select(d => d.MTRLID.Value)
                            .Distinct()
                            .ToList();

                        var materialMap = new Dictionary<int, MaterialMaster>();
                        var groupNameMap = new Dictionary<int, string>();

                        if (matchedMaterialIds.Any())
                        {
                            materialMap = db.MaterialMasters
                                .Where(m => matchedMaterialIds.Contains(m.MTRLID))
                                .ToDictionary(m => m.MTRLID, m => m);

                            var groupIds = materialMap.Values
                                .Where(m => m.MTRLGID > 0)
                                .Select(m => m.MTRLGID)
                                .Distinct()
                                .ToList();

                            if (groupIds.Any())
                            {
                                groupNameMap = db.MaterialGroupMasters
                                    .Where(g => groupIds.Contains(g.MTRLGID))
                                    .ToDictionary(g => g.MTRLGID, g => g.MTRLGDESC);
                            }
                        }

                        var items = tempDetails
                            .Select(d =>
                            {
                                MaterialMaster material = null;
                                string groupName = string.Empty;

                                if (d.MTRLID.HasValue && d.MTRLID.Value > 0)
                                {
                                    materialMap.TryGetValue(d.MTRLID.Value, out material);

                                    if (material != null && material.MTRLGID > 0)
                                    {
                                        string gName;
                                        if (groupNameMap.TryGetValue(material.MTRLGID, out gName))
                                        {
                                            groupName = gName;
                                        }
                                    }
                                }

                                decimal profitPercent = material != null ? material.MTRLPRFT : 0m;

                                return new SalesOrderUploadItemViewModel
                                {
                                    DetailId = d.LineNo,
                                    ExtractedItemName = d.ItemDrugName,
                                    MaterialName = material != null ? material.MTRLDESC : null,
                                    MaterialGroupName = groupName,
                                    ProfitPercent = profitPercent,
                                    Qty = d.Qty,
                                    Rate = d.RatePerUnit,
                                    ActualRate = 0m,
                                    Amount = d.GrossAmount,
                                    ActualMaterialId = d.MTRLID ?? 0
                                };
                            })
                            .ToList();

                        ViewBag.UploadedSalesOrderId = 0;
                        ViewBag.UploadBatchId = uploadBatchIdObj != null ? uploadBatchIdObj.ToString() : null;
                        ViewBag.TransactionMasterTempId = masterTempId;
                        ViewBag.UploadedDetails = items;

                        var allMaterials = db.MaterialMasters
                            .OrderBy(m => m.MTRLDESC)
                            .Select(m => new { m.MTRLID, m.MTRLDESC })
                            .ToList();
                        ViewBag.MaterialList = new SelectList(allMaterials, "MTRLID", "MTRLDESC");
                    }
                }
            }

            return View();
        }

        public class SalesOrderUploadItemViewModel
        {
            public int DetailId { get; set; }
            public string ExtractedItemName { get; set; }
            public string MaterialName { get; set; }
            public string MaterialGroupName { get; set; }
            public decimal ProfitPercent { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal ActualRate { get; set; }
            public decimal Amount { get; set; }
            public int ActualMaterialId { get; set; }
        }

        private class TransactionDetailTempRow
        {
            public int TransactionMasterTempId { get; set; }
            public int LineNo { get; set; }
            public string ItemDrugName { get; set; }
            public string HsnCode { get; set; }
            public decimal Qty { get; set; }
            public decimal RatePerUnit { get; set; }
            public decimal GrossAmount { get; set; }
            // MTRLID comes from MATERIALMASTER when using PR_TRANSACTIONDETAILMATERIAL_DETAILS
            public int? MTRLID { get; set; }
        }

        private class UploadDetailCalcRow
        {
            public int MaterialId { get; set; }
            public string MaterialCode { get; set; }
            public string MaterialName { get; set; }
            public decimal ProfitPercent { get; set; }
            public int HsnId { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal ActualRate { get; set; }
            public decimal Gross { get; set; }
            public decimal Cgst { get; set; }
            public decimal Sgst { get; set; }
            public decimal Igst { get; set; }
            public decimal Net { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Index(HttpPostedFileBase file, int? customerId)
        {
            PopulateCustomerList(customerId);

            if (!customerId.HasValue || customerId.Value <= 0)
            {
                TempData["ErrorMessage"] = "Please select a customer.";
                return View();
            }

            if (file == null || file.ContentLength == 0)
            {
                TempData["ErrorMessage"] = "Please select a file to upload.";
                return View();
            }

            var extension = Path.GetExtension(file.FileName) ?? string.Empty;
            if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Invalid file type. Only PDF files (.pdf) are allowed.";
                return View();
            }

            string extractedText = string.Empty;

            try
            {
                var uploadsDir = Server.MapPath("~/Uploads/SalesOrderPdfs");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                var safeName = Path.GetFileNameWithoutExtension(file.FileName);
                var ext = Path.GetExtension(file.FileName) ?? string.Empty;
                var uniqueName = string.Format("{0}_{1:yyyyMMddHHmmssfff}{2}", safeName, DateTime.Now, ext);
                var fullPath = Path.Combine(uploadsDir, uniqueName);

                file.SaveAs(fullPath);

                using (var reader = new PdfReader(fullPath))
                using (var pdfDoc = new PdfDocument(reader))
                {
                    var sb = new StringBuilder();
                    int totalPages = pdfDoc.GetNumberOfPages();

                    for (int page = 1; page <= totalPages; page++)
                    {
                        var strategy = new SimpleTextExtractionStrategy();
                        string pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(page), strategy);
                        sb.AppendLine(pageText);
                    }

                    extractedText = sb.ToString();
                }
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "There was a problem reading the PDF file.";
                return View();
            }

            // Directly process extracted text into TransactionMaster / TransactionDetail and temp tables,
            // then redirect to SalesOrder index (single-step flow for the user).
            return SaveTemp(extractedText, file.FileName, customerId);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SaveTemp(string extractedText, string originalFileName, int? customerId)
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
                    : "Upload";

                // Remember the customer for this upload so that we can recreate the Sales Order
                if (customerId.HasValue && customerId.Value > 0)
                {
                    TempData["UploadCustomerId"] = customerId.Value;
                    Session["SalesOrderUploadCustomerId"] = customerId.Value;
                }

                // Clean up any previous temp data for this session (if user uploaded again without confirming)
                try
                {
                    var lastBatchStr = Session["LastUploadBatchId"] as string;
                    var lastMasterTempObj = Session["LastTransactionMasterTempId"];

                    Guid lastBatchGuid;
                    int lastMasterTempId;
                    if (!string.IsNullOrWhiteSpace(lastBatchStr)
                        && Guid.TryParse(lastBatchStr, out lastBatchGuid)
                        && lastMasterTempObj != null
                        && int.TryParse(lastMasterTempObj.ToString(), out lastMasterTempId)
                        && lastMasterTempId > 0)
                    {
                        db.Database.ExecuteSqlCommand(
                            "DELETE FROM TransactionDetailTemp WHERE TransactionMasterTempId = @p0; DELETE FROM TransactionMasterTemp WHERE UploadBatchId = @p1;",
                            lastMasterTempId,
                            lastBatchGuid);
                    }
                }
                catch
                {
                    // Swallow cleanup errors; do not block new upload
                }

                // Unique batch id for this upload so we can safely clean up temp rows later
                var uploadBatchId = Guid.NewGuid();

                string fullText = extractedText ?? string.Empty;
                var allLines = fullText
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

                // ---------------- Header parsing ----------------
                string poNumber = null;
                DateTime? poDate = null;
                string billingName = null;
                string billingCustomerName = null;
                string billingAddress = null;
                string billingGstin = null;
                string supplierName = null;
                decimal? totalAmount = null;
                decimal? grossAmount = null;
                int? creditPeriodDays = null;
                DateTime? receiveByDate = null;
                DateTime? approvedDate = null;

                // PO number and PO date
                var poMatch = Regex.Match(fullText,
                    @"PO\s*#\s*(?<po>.+?)\s+PO Date\s+(?<date>\d{1,2}/\d{1,2}/\d{4})",
                    RegexOptions.IgnoreCase);
                if (poMatch.Success)
                {
                    poNumber = poMatch.Groups["po"].Value.Trim();
                    var dateStr = poMatch.Groups["date"].Value.Trim();
                    if (DateTime.TryParse(dateStr, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out var dtPo))
                    {
                        poDate = dtPo;
                    }
                }

                // Remember PO Number for reference number in final TransactionMaster
                Session["SalesOrderUploadPoNumber"] = poNumber;

                // Billing and supplier names (multi-line billing block, then supplier name)
                int headerIndex = allLines.FindIndex(l => l.StartsWith("Billing Name and Address", StringComparison.OrdinalIgnoreCase));
                if (headerIndex >= 0)
                {
                    int indiaIndex = allLines.FindIndex(headerIndex + 1, l => string.Equals(l, "India", StringComparison.OrdinalIgnoreCase));

                    if (indiaIndex >= 0)
                    {
                        // Billing block: from first line under the header up to the line after "India"
                        int start = headerIndex + 1;
                        int end = Math.Min(indiaIndex + 1, allLines.Count - 1); // includes GST line after India

                        if (start <= end)
                        {
                            var billingLines = allLines
                                .Skip(start)
                                .Take(end - start + 1)
                                .ToList();

                            // First line is billing name (e.g. PHARMA STORE)
                            if (billingLines.Count > 0)
                            {
                                billingName = billingLines[0];
                            }

                            // Detect GSTIN in last line if present
                            if (billingLines.Count > 1)
                            {
                                var gstCandidate = billingLines[billingLines.Count - 1].Replace(" ", string.Empty);
                                if (Regex.IsMatch(gstCandidate, @"^[0-9]{2}[A-Z0-9]{13}$", RegexOptions.IgnoreCase))
                                {
                                    billingGstin = billingLines[billingLines.Count - 1];
                                    billingLines.RemoveAt(billingLines.Count - 1);
                                }
                            }

                            // Second line contains customer name + maybe part of address
                            if (billingLines.Count > 1)
                            {
                                var secondLine = billingLines[1];
                                int commaIndex = secondLine.IndexOf(',');
                                var addrParts = new List<string>();

                                if (commaIndex > 0)
                                {
                                    billingCustomerName = secondLine.Substring(0, commaIndex).Trim();
                                    var restSecond = secondLine.Substring(commaIndex + 1).Trim();
                                    if (!string.IsNullOrEmpty(restSecond))
                                    {
                                        addrParts.Add(restSecond);
                                    }
                                }
                                else
                                {
                                    billingCustomerName = secondLine.Trim();
                                }

                                // Remaining lines (from index 2 onwards) are address lines
                                for (int i = 2; i < billingLines.Count; i++)
                                {
                                    addrParts.Add(billingLines[i]);
                                }

                                if (addrParts.Count > 0)
                                {
                                    billingAddress = string.Join(Environment.NewLine, addrParts);
                                }
                            }
                        }

                        // Supplier name: first line after the billing block (e.g. 8848 SMA REMEDIES)
                        if (indiaIndex + 2 < allLines.Count)
                        {
                            supplierName = allLines[indiaIndex + 2];
                        }
                    }
                    else if (headerIndex + 1 < allLines.Count)
                    {
                        // Fallback: at least capture the first line under billing header
                        billingName = allLines[headerIndex + 1];
                    }
                }

                // Totals
                var totalAmtMatch = Regex.Match(fullText,
                    @"Total Amount\s+Rs\.\s*(?<amt>[0-9,]+\.\d+)",
                    RegexOptions.IgnoreCase);
                if (totalAmtMatch.Success)
                {
                    var amtStr = totalAmtMatch.Groups["amt"].Value.Replace(",", "");
                    if (decimal.TryParse(amtStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    {
                        totalAmount = dec;
                    }
                }

                var grossAmtMatch = Regex.Match(fullText,
                    @"Gross Amount\s+Rs\.\s*(?<amt>[0-9,]+\.\d+)",
                    RegexOptions.IgnoreCase);
                if (grossAmtMatch.Success)
                {
                    var amtStr = grossAmtMatch.Groups["amt"].Value.Replace(",", "");
                    if (decimal.TryParse(amtStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    {
                        grossAmount = dec;
                    }
                }

                // Credit period
                var creditMatch = Regex.Match(fullText,
                    @"Credit Period\s+(?<days>\d+)\s+days",
                    RegexOptions.IgnoreCase);
                if (creditMatch.Success && int.TryParse(creditMatch.Groups["days"].Value, out var days))
                {
                    creditPeriodDays = days;
                }

                // Receive By date
                var receiveMatch = Regex.Match(fullText,
                    @"Receive By\s+(?<date>\d{1,2}/\d{1,2}/\d{4})",
                    RegexOptions.IgnoreCase);
                if (receiveMatch.Success)
                {
                    var dateStr = receiveMatch.Groups["date"].Value.Trim();
                    if (DateTime.TryParse(dateStr, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out var dtRec))
                    {
                        receiveByDate = dtRec;
                    }
                }

                // Approved Date (full date-time string before "Total CGST Amt")
                var approvedMatch = Regex.Match(fullText,
                    @"Approved Date\s+(?<dt>.+?)\s+Total CGST Amt",
                    RegexOptions.IgnoreCase);
                if (approvedMatch.Success)
                {
                    var dtStr = approvedMatch.Groups["dt"].Value.Trim();
                    if (DateTime.TryParse(dtStr, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.AssumeLocal, out var dtApp))
                    {
                        approvedDate = dtApp;
                    }
                }

                // Insert structured header row (plus full text)
                object DbValue(object value) => value ?? (object)DBNull.Value;

                int masterId = db.Database.SqlQuery<int>(
                    "INSERT INTO TransactionMasterTemp (UploadBatchId, OriginalPdfFileName, UploadedOn, UploadedBy, PoNumber, PoDate, BillingName, BillingCustomerName, BillingAddress, BillingGstin, SupplierName, TotalAmount, GrossAmount, CreditPeriodDays, ReceiveByDate, ApprovedDate, FullExtractedText) " +
                    "VALUES (@p0, @p1, GETDATE(), @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15); " +
                    "SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    uploadBatchId,
                    originalFileName ?? string.Empty,
                    uploadedBy,
                    DbValue(poNumber),
                    DbValue(poDate),
                    DbValue(billingName),
                    DbValue(billingCustomerName),
                    DbValue(billingAddress),
                    DbValue(billingGstin),
                    DbValue(supplierName),
                    DbValue(totalAmount),
                    DbValue(grossAmount),
                    DbValue(creditPeriodDays),
                    DbValue(receiveByDate),
                    DbValue(approvedDate),
                    fullText
                ).Single();

                // ---------------- Detail parsing (only item rows) ----------------
                int itemsHeaderIndex = allLines.FindIndex(l => l.StartsWith("Sno Item/Drug Name", StringComparison.OrdinalIgnoreCase));
                if (itemsHeaderIndex >= 0)
                {
                    var itemBlocks = new List<string>();
                    StringBuilder currentItem = null;

                    for (int i = itemsHeaderIndex + 1; i < allLines.Count; i++)
                    {
                        var line = allLines[i];

                        if (line.StartsWith("Prepared by", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        bool startsWithNumber = Regex.IsMatch(line, @"^\d+(\.\d+)?\s+");

                        if (startsWithNumber)
                        {
                            if (currentItem != null)
                            {
                                itemBlocks.Add(currentItem.ToString().Trim());
                            }
                            currentItem = new StringBuilder();
                            currentItem.Append(line);
                        }
                        else if (currentItem != null)
                        {
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
                        if (tokens.Length < 10)
                        {
                            continue;
                        }

                        int hsnIndex = Array.FindIndex(tokens, t => Regex.IsMatch(t, @"^\d{6,8}$"));
                        if (hsnIndex <= 1)
                        {
                            continue;
                        }

                        string itemName = string.Join(" ", tokens.Skip(1).Take(hsnIndex - 1));
                        string hsnCode = tokens[hsnIndex];

                        decimal ParseDecimalOrZero(string s)
                        {
                            if (string.IsNullOrWhiteSpace(s)) return 0m;
                            s = s.Replace(",", string.Empty);
                            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                            {
                                return val;
                            }
                            return 0m;
                        }

                        decimal qty = 0m;
                        if (hsnIndex + 1 < tokens.Length)
                        {
                            qty = ParseDecimalOrZero(tokens[hsnIndex + 1]);
                        }

                        int idx = hsnIndex + 2; // skip qty
                        if (idx < tokens.Length)
                        {
                            idx++; // skip Free Qty flag (e.g. "No")
                        }

                        var uqcParts = new List<string>();
                        while (idx < tokens.Length && tokens[idx] != "Rs.")
                        {
                            uqcParts.Add(tokens[idx]);
                            idx++;
                        }
                        string uqc = string.Join(" ", uqcParts).Trim();

                        decimal ratePerUnit = 0m;
                        decimal discountPercent = 0m;
                        decimal cgstPercent = 0m;
                        decimal sgstPercent = 0m;
                        decimal igstPercent = 0m;
                        decimal grossLineAmount = 0m;

                        if (idx < tokens.Length && tokens[idx] == "Rs." && idx + 1 < tokens.Length)
                        {
                            ratePerUnit = ParseDecimalOrZero(tokens[idx + 1]);
                            idx += 2;

                            if (idx < tokens.Length) { discountPercent = ParseDecimalOrZero(tokens[idx]); idx++; }
                            if (idx < tokens.Length) { cgstPercent = ParseDecimalOrZero(tokens[idx]); idx++; }
                            if (idx < tokens.Length) { sgstPercent = ParseDecimalOrZero(tokens[idx]); idx++; }
                            if (idx < tokens.Length) { igstPercent = ParseDecimalOrZero(tokens[idx]); idx++; }

                            int secondRsIndex = Array.FindIndex(tokens, idx, t => t == "Rs.");
                            if (secondRsIndex >= 0 && secondRsIndex + 1 < tokens.Length)
                            {
                                grossLineAmount = ParseDecimalOrZero(tokens[secondRsIndex + 1]);
                            }
                            else
                            {
                                grossLineAmount = ParseDecimalOrZero(tokens.Last());
                            }
                        }

                        var rawLineText = block.Length > 500 ? block.Substring(0, 500) : block;

                        db.Database.ExecuteSqlCommand(
                            "INSERT INTO TransactionDetailTemp (TransactionMasterTempId, [LineNo], ItemDrugName, HsnCode, Qty, FreeQty, Uqc, RatePerUnit, DiscountPercent, CgstPercent, SgstPercent, IgstPercent, GrossAmount, RawLineText) " +
                            "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13);",
                            masterId,
                            lineNo,
                            itemName,
                            hsnCode,
                            qty,
                            0m, // FreeQty
                            uqc,
                            ratePerUnit,
                            discountPercent,
                            cgstPercent,
                            sgstPercent,
                            igstPercent,
                            grossLineAmount,
                            rawLineText
                        );

                        lineNo++;
                    }
                }

                TempData["UploadBatchId"] = uploadBatchId.ToString();
                TempData["TransactionMasterTempId"] = masterId;

                // Track current temp ids in session so we can clean them on next upload if user does not confirm
                Session["LastUploadBatchId"] = uploadBatchId.ToString();
                Session["LastTransactionMasterTempId"] = masterId;
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error saving extracted data: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ConfirmUploadedDetails(
            int uploadedSalesOrderId,
            int[] detailIds,
            int[] actualMaterialIds,
            string uploadBatchId,
            int? transactionMasterTempId)
        {
            // uploadedSalesOrderId is no longer used; a fresh TransactionMaster will be created here
            if (detailIds == null || actualMaterialIds == null || detailIds.Length == 0 || detailIds.Length != actualMaterialIds.Length)
            {
                TempData["ErrorMessage"] = "Unable to confirm uploaded details. Please try again.";
                return RedirectToAction("Index");
            }

            if (transactionMasterTempId == null || transactionMasterTempId.Value <= 0 || string.IsNullOrWhiteSpace(uploadBatchId))
            {
                TempData["ErrorMessage"] = "Upload session information is missing. Please upload the file again.";
                return RedirectToAction("Index");
            }

            if (actualMaterialIds.Any(id => id <= 0))
            {
                TempData["ErrorMessage"] = "Please select an actual material for all rows before saving.";
                return RedirectToAction("Index");
            }

            try
            {
                // Resolve customer from session (selected at upload time)
                int customerId;
                var customerIdObj = Session["SalesOrderUploadCustomerId"];
                if (customerIdObj == null || !int.TryParse(customerIdObj.ToString(), out customerId) || customerId <= 0)
                {
                    TempData["ErrorMessage"] = "Customer information for this upload could not be found. Please upload again.";
                    return RedirectToAction("Index");
                }

                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == customerId);
                if (customer == null)
                {
                    TempData["ErrorMessage"] = "Selected customer no longer exists. Please upload again.";
                    return RedirectToAction("Index");
                }

                short tranStateType = 0;
                var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
                if (state != null)
                {
                    tranStateType = state.STATETYPE;
                }

                // Load temp header and detail rows
                int masterTempId = transactionMasterTempId.Value;

                var tempDetails = db.Database.SqlQuery<TransactionDetailTempRow>(
                    "SELECT [LineNo], ItemDrugName, HsnCode, Qty, RatePerUnit, GrossAmount FROM TransactionDetailTemp WHERE TransactionMasterTempId = @p0 ORDER BY [LineNo];",
                    masterTempId
                ).ToList();

                if (!tempDetails.Any())
                {
                    TempData["ErrorMessage"] = "No temporary detail rows were found for this upload. Please upload again.";
                    return RedirectToAction("Index");
                }

                // Preload materials and HSN info
                var distinctMaterialIds = actualMaterialIds.Distinct().ToList();
                var materialMap = db.MaterialMasters
                    .Where(m => distinctMaterialIds.Contains(m.MTRLID))
                    .ToDictionary(m => m.MTRLID, m => m);

                var hsnIds = materialMap.Values
                    .Where(m => m.HSNID > 0)
                    .Select(m => m.HSNID)
                    .Distinct()
                    .ToList();

                var hsnMap = db.HSNCodeMasters
                    .Where(h => hsnIds.Contains(h.HSNID))
                    .ToDictionary(h => h.HSNID, h => h);

                var calcRows = new List<UploadDetailCalcRow>();

                for (int i = 0; i < detailIds.Length; i++)
                {
                    int lineNo = detailIds[i];
                    int materialId = actualMaterialIds[i];

                    var tempRow = tempDetails.FirstOrDefault(d => d.LineNo == lineNo);
                    if (tempRow == null)
                    {
                        continue;
                    }

                    MaterialMaster material;
                    materialMap.TryGetValue(materialId, out material);

                    int hsnId = material != null ? material.HSNID : 0;
                    HSNCodeMaster hsn = null;
                    if (hsnId > 0)
                    {
                        hsnMap.TryGetValue(hsnId, out hsn);
                    }

                    decimal qty = tempRow.Qty;
                    decimal rate = tempRow.RatePerUnit > 0 ? tempRow.RatePerUnit : (material != null ? material.RATE : 0m);
                    decimal profitPercent = material != null ? material.MTRLPRFT : 0m;
                    decimal actualRate = rate;

                    if (actualRate <= 0 && rate > 0 && profitPercent != 0)
                    {
                        actualRate = Math.Round(rate + ((rate * profitPercent) / 100m), 2);
                    }

                    decimal gross = tempRow.GrossAmount > 0 ? tempRow.GrossAmount : qty * actualRate;

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

                    var row = new UploadDetailCalcRow
                    {
                        MaterialId = material != null ? material.MTRLID : materialId,
                        MaterialCode = material != null ? material.MTRLCODE : string.Empty,
                        MaterialName = material != null ? material.MTRLDESC : tempRow.ItemDrugName,
                        ProfitPercent = profitPercent,
                        HsnId = hsnId,
                        Qty = qty,
                        Rate = rate,
                        ActualRate = actualRate,
                        Gross = gross,
                        Cgst = cgstAmt,
                        Sgst = sgstAmt,
                        Igst = igstAmt,
                        Net = net
                    };

                    calcRows.Add(row);
                }

                if (!calcRows.Any())
                {
                    TempData["ErrorMessage"] = "No detail rows could be calculated for this upload. Please upload again.";
                    return RedirectToAction("Index");
                }

                decimal totalGross = calcRows.Sum(r => r.Gross);
                decimal totalCgst = calcRows.Sum(r => r.Cgst);
                decimal totalSgst = calcRows.Sum(r => r.Sgst);
                decimal totalIgst = calcRows.Sum(r => r.Igst);
                decimal totalNet = calcRows.Sum(r => r.Net);

                // Company and transaction numbering
                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesOrderRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;
                string trandNo = nextTranNo.ToString("D4");

                // User Ids for stored procedure (int, as originally used by PR_TRANSACTIONMASTER_INSRT)
                int cusrId = 0;
                var sessUsr = Session["CUSRID"];
                if (sessUsr != null)
                {
                    int.TryParse(sessUsr.ToString(), out cusrId);
                }

                int lmusId = cusrId;

                // User name string we want ultimately stored in TRANSACTIONMASTER.CUSRID / LMUSRID
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

                // Use PO number from session (if parsed) as TRANREFNO
                string poNumberRef = Session["SalesOrderUploadPoNumber"] as string;
                if (string.IsNullOrWhiteSpace(poNumberRef))
                {
                    poNumberRef = "-";
                }
                else
                {
                    poNumberRef = poNumberRef.Trim();
                }

                // Check if this TRANREFNO already exists for Sales Order (REGSTRID = 1)
                if (!string.IsNullOrWhiteSpace(poNumberRef) && poNumberRef != "-")
                {
                    var existingOrder = db.TransactionMasters
                        .Where(t => t.REGSTRID == SalesOrderRegisterId)
                        .AsEnumerable()
                        .FirstOrDefault(t => string.Equals(t.TRANREFNO?.Trim(), poNumberRef, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingOrder != null)
                    {
                        TempData["ErrorMessage"] = $"A Sales Order with PO Number '{poNumberRef}' already exists (Sales Order #{existingOrder.TRANDNO}). Please check the PO Number or contact the administrator.";
                        return RedirectToAction("Index");
                    }
                }

                DateTime tranDate = DateTime.Today;
                DateTime tranTime = DateTime.Now;
                DateTime prcsDate = DateTime.Now;

                // Call PR_TRANSACTIONMASTER_INSRT to create TransactionMaster
                var pCompyId = new SqlParameter("@COMPYID", SqlDbType.Int) { Value = compyId };
                var pSdptId = new SqlParameter("@SDPTID", SqlDbType.Int) { Value = 0 };
                var pRegstrId = new SqlParameter("@REGSTRID", SqlDbType.Int) { Value = SalesOrderRegisterId };
                var pTranBType = new SqlParameter("@TRANBTYPE", SqlDbType.Int) { Value = 0 };
                var pTranDate = new SqlParameter("@TRANDATE", SqlDbType.DateTime) { Value = tranDate };
                var pTranTime = new SqlParameter("@TRANTIME", SqlDbType.DateTime) { Value = tranTime };
                var pTranNo = new SqlParameter("@TRANNO", SqlDbType.Int) { Value = nextTranNo };
                var pTrandNo = new SqlParameter("@TRANDNO", SqlDbType.VarChar, 25) { Value = trandNo };
                var pTranRefId = new SqlParameter("@TRANREFID", SqlDbType.Int) { Value = customerId };
                var pTranRefName = new SqlParameter("@TRANREFNAME", SqlDbType.VarChar, 100) { Value = (object)customer.CATENAME ?? DBNull.Value };
                var pTranStateType = new SqlParameter("@TRANSTATETYPE", SqlDbType.Int) { Value = (int)tranStateType };
                var pTranRefNo = new SqlParameter("@TRANREFNO", SqlDbType.VarChar, 25) { Value = poNumberRef };
                var pTranGAmt = new SqlParameter("@TRANGAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalGross };
                var pTranCgstAmt = new SqlParameter("@TRANCGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalCgst };
                var pTranSgstAmt = new SqlParameter("@TRANSGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalSgst };
                var pTranIgstAmt = new SqlParameter("@TRANIGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalIgst };
                var pTranNAmt = new SqlParameter("@TRANNAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = totalNet };
                var pTranAmtWrds = new SqlParameter("@TRANAMTWRDS", SqlDbType.VarChar, 250) { Value = (object)ConvertAmountToWords(totalNet) ?? DBNull.Value };
                var pTranLMId = new SqlParameter("@TRANLMID", SqlDbType.Int) { Value = 0 };
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
                    throw new Exception("Failed to create TransactionMaster record.");
                }

                // After the SP insert, update the created TRANSACTIONMASTER row to store the username
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
                    // Do not block the operation if this post-update fails
                }

                // Insert detail rows using PR_TRANSACTIONDETAIL_INSRT
                foreach (var r in calcRows)
                {
                    var pPtranMid = new SqlParameter("@PTRANMID", SqlDbType.Int) { Value = tranmid };
                    var pPtrandRefId = new SqlParameter("@PTRANDREFID", SqlDbType.Int) { Value = r.MaterialId };
                    var pPtrandRefNo = new SqlParameter("@PTRANDREFNO", SqlDbType.VarChar, 25) { Value = (object)r.MaterialCode ?? string.Empty };
                    var pPtrandRefName = new SqlParameter("@PTRANDREFNAME", SqlDbType.VarChar, 100) { Value = (object)r.MaterialName ?? string.Empty };
                    var pPtrandMtrlPrft = new SqlParameter("@PTRANDMTRLPRFT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.ProfitPercent };
                    var pPhsnId = new SqlParameter("@PHSNID", SqlDbType.Int) { Value = r.HsnId };
                    var pPpackmId = new SqlParameter("@PPACKMID", SqlDbType.Int) { Value = 0 };
                    var pPtrandQty = new SqlParameter("@PTRANDQTY", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Qty };
                    var pPtrandRate = new SqlParameter("@PTRANDRATE", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Rate };
                    var pPtrandGAmt = new SqlParameter("@PTRANDGAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Gross };
                    var pPtrandCgstAmt = new SqlParameter("@PTRANDCGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Cgst };
                    var pPtrandSgstAmt = new SqlParameter("@PTRANDSGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Sgst };
                    var pPtrandIgstAmt = new SqlParameter("@PTRANDIGSTAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Igst };
                    var pPtrandNAmt = new SqlParameter("@PTRANDNAMT", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.Net };
                    var pPtrandAid = new SqlParameter("@PTRANDAID", SqlDbType.Int) { Value = 0 };
                    var pPtrandNartn = new SqlParameter("@PTRANDNARTN", SqlDbType.VarChar) { Value = DBNull.Value };
                    var pPtrandRmks = new SqlParameter("@PTRANDRMKS", SqlDbType.VarChar) { Value = DBNull.Value };
                    var pPtrandARate = new SqlParameter("@PTRANDARATE", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = r.ActualRate };
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
                }

                // Clean up temp tables
                Guid batchGuid;
                if (!string.IsNullOrWhiteSpace(uploadBatchId) && Guid.TryParse(uploadBatchId, out batchGuid))
                {
                    db.Database.ExecuteSqlCommand(
                        "DELETE FROM TransactionDetailTemp WHERE TransactionMasterTempId = @p0; DELETE FROM TransactionMasterTemp WHERE UploadBatchId = @p1;",
                        masterTempId,
                        batchGuid);

                    // Clear session markers now that temp data has been fully cleaned up for this upload
                    Session["LastUploadBatchId"] = null;
                    Session["LastTransactionMasterTempId"] = null;
                    Session["SalesOrderUploadCustomerId"] = null;
                    Session["SalesOrderUploadPoNumber"] = null;
                }

                TempData["SuccessMessage"] = "Sales order uploaded successfully.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error confirming uploaded details: " + ex.Message;
                return RedirectToAction("Index");
            }
        }
    }
}
