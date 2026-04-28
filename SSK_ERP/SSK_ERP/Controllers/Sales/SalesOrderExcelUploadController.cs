using System;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Linq;
using System.Data;
using System.Data.SqlClient;
using System.Collections.Generic;
using ClosedXML.Excel;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    [Authorize(Roles = "SalesOrderCreate")]
    public class SalesOrderExcelUploadController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesOrderRegisterId = 1;

        private void PopulateCustomerList(int? selectedCustomerId = null)
        {
            var customerList = db.CustomerMasters
                .Where(c => c.DISPSTATUS == 0)
                .OrderBy(c => c.CATENAME)
                .Select(c => new { c.CATEID, c.CATENAME })
                .ToList();
            ViewBag.CustomerList = new SelectList(customerList, "CATEID", "CATENAME", selectedCustomerId);
        }

        [HttpGet]
        public ActionResult Index()
        {
            int? selectedCustomerId = null;

            var uploadCustomerIdObj = TempData["UploadCustomerId"] ?? Session["SalesOrderExcelUploadCustomerId"];
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
                        .GroupBy(d => d.LineNo)
                        .Select(g => g
                            .OrderByDescending(d => GetMaterialMatchScore(d))
                            .ThenByDescending(d => NormalizeMaterialMatchText(d.MTRLDESC).Length)
                            .First())
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
                                decimal actualRate = d.RatePerUnit;
                                if (actualRate > 0 && profitPercent != 0m)
                                {
                                    actualRate = Math.Round(actualRate + ((actualRate * profitPercent) / 100m), 2);
                                }

                                return new SalesOrderExcelUploadItemViewModel
                                {
                                    DetailId = d.LineNo,
                                    ExtractedItemName = d.ItemDrugName,
                                    MaterialName = material != null ? material.MTRLDESC : null,
                                    MaterialGroupName = groupName,
                                    ProfitPercent = profitPercent,
                                    Qty = d.Qty,
                                    Rate = d.RatePerUnit,
                                    ActualRate = actualRate,
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

        public class SalesOrderExcelUploadItemViewModel
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
            public int? MTRLID { get; set; }
            public string MTRLDESC { get; set; }
        }

        private static string NormalizeMaterialMatchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.ToUpperInvariant();
            normalized = normalized.Replace("-", " ");
            normalized = normalized.Replace(".", " ");
            normalized = normalized.Replace("%", " ");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private static int GetMaterialMatchScore(TransactionDetailTempRow row)
        {
            if (row == null)
            {
                return int.MinValue;
            }

            string itemName = NormalizeMaterialMatchText(row.ItemDrugName);
            string materialName = NormalizeMaterialMatchText(row.MTRLDESC);
            int score = row.MTRLID.HasValue && row.MTRLID.Value > 0 ? 1000 : 0;

            if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(materialName))
            {
                return score;
            }

            if (string.Equals(itemName, materialName, StringComparison.OrdinalIgnoreCase))
            {
                score += 10000;
            }

            if (itemName.Contains(materialName))
            {
                score += 5000;
            }

            if (materialName.Contains(itemName))
            {
                score += 2500;
            }

            score += materialName.Length;
            return score;
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
            if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xls", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Invalid file type. Only Excel files (.xlsx, .xls) are allowed.";
                return View();
            }

            try
            {
                var uploadsDir = Server.MapPath("~/Uploads/SalesOrderExcels");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                var safeName = Path.GetFileNameWithoutExtension(file.FileName);
                var ext = Path.GetExtension(file.FileName) ?? string.Empty;
                var uniqueName = string.Format("{0}_{1:yyyyMMddHHmmssfff}{2}", safeName, DateTime.Now, ext);
                var fullPath = Path.Combine(uploadsDir, uniqueName);
                file.SaveAs(fullPath);

                // Read Excel file
                var excelData = ReadExcelFile(fullPath);
                
                if (excelData == null || !excelData.Items.Any())
                {
                    // For debugging - show what was read
                    string debugInfo = $"PO Number: {excelData?.PoNumber ?? "null"}, Items count: {excelData?.Items?.Count ?? 0}";
                    TempData["ErrorMessage"] = "No data found in the Excel file. " + debugInfo;
                    return View();
                }

                // Save temp data
                return SaveTemp(excelData, file.FileName, customerId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error reading Excel file: " + ex.Message;
                return View();
            }
        }

        private ExcelUploadData ReadExcelFile(string filePath)
        {
            var data = new ExcelUploadData();

            using (var workbook = new XLWorkbook(filePath))
            {
                var worksheet = workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                {
                    return data;
                }

                // Read PO Number from B1
                var poNumberCell = worksheet.Cell("B1");
                data.PoNumber = poNumberCell.GetString().Trim();

                // Finalized Excel format:
                // Row 1: A1 = PO text, B1 = PO Number
                // Row 2: headers
                // Row 3+: items (A=S.No, B=Item/Drug Name, C=Qty, D=Rate/Unit)
                var lastUsedRow = worksheet.LastRowUsed();
                if (lastUsedRow == null)
                {
                    return data;
                }

                int lastRowNumber = lastUsedRow.RowNumber();
                int lineNo = 1;

                for (int r = 3; r <= lastRowNumber; r++)
                {
                    try
                    {
                        var row = worksheet.Row(r);
                        var itemName = GetCellValue(row.Cell(2)); // Column B: Item Name
                        var qty = GetDecimalValue(row.Cell(3)); // Column C: Qty
                        var rate = GetDecimalValue(row.Cell(4)); // Column D: Rate

                        if (string.IsNullOrWhiteSpace(itemName))
                        {
                            continue;
                        }

                        decimal qtyValue = qty ?? 0m;
                        decimal rateValue = rate ?? 0m;
                        decimal amount = qtyValue * rateValue;

                        data.Items.Add(new ExcelDataRow
                        {
                            LineNo = lineNo++,
                            ItemDrugName = itemName,
                            Qty = qtyValue,
                            RatePerUnit = rateValue,
                            GrossAmount = amount
                        });
                    }
                    catch
                    {
                        // Skip rows with errors
                    }
                }
            }

            return data;
        }

        private string GetCellValue(IXLCell cell)
        {
            if (cell == null)
                return string.Empty;
            return cell.GetString().Trim();
        }

        private decimal? GetDecimalValue(IXLCell cell)
        {
            if (cell == null)
                return null;

            decimal value;
            if (cell.TryGetValue(out value))
            {
                return value;
            }

            string strValue = cell.GetString();
            if (decimal.TryParse(strValue, out value))
            {
                return value;
            }

            return null;
        }

        public class ExcelUploadData
        {
            public string PoNumber { get; set; }
            public List<ExcelDataRow> Items { get; set; } = new List<ExcelDataRow>();
        }

        public class ExcelDataRow
        {
            public int LineNo { get; set; }
            public string ItemDrugName { get; set; }
            public decimal Qty { get; set; }
            public decimal RatePerUnit { get; set; }
            public decimal GrossAmount { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SaveTemp(ExcelUploadData excelData, string originalFileName, int? customerId)
        {
            if (excelData == null || !excelData.Items.Any())
            {
                TempData["ErrorMessage"] = "No data to save.";
                return RedirectToAction("Index");
            }

            try
            {
                var uploadedBy = (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                    ? User.Identity.Name
                    : "Upload";

                // Remember the customer and PO number
                if (customerId.HasValue && customerId.Value > 0)
                {
                    TempData["UploadCustomerId"] = customerId.Value;
                    Session["SalesOrderExcelUploadCustomerId"] = customerId.Value;
                }

                if (!string.IsNullOrWhiteSpace(excelData.PoNumber))
                {
                    Session["SalesOrderExcelUploadPoNumber"] = excelData.PoNumber;
                }

                // Clean up previous temp data
                try
                {
                    var lastBatchStr = Session["LastExcelUploadBatchId"] as string;
                    var lastMasterTempObj = Session["LastExcelTransactionMasterTempId"];

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
                    // Swallow cleanup errors
                }

                var uploadBatchId = Guid.NewGuid();
                object DbValue(object value) => value ?? (object)DBNull.Value;

                int masterId = db.Database.SqlQuery<int>(
                    "INSERT INTO TransactionMasterTemp (UploadBatchId, OriginalPdfFileName, UploadedOn, UploadedBy, PoNumber, PoDate, BillingName, BillingCustomerName, BillingAddress, BillingGstin, SupplierName, TotalAmount, GrossAmount, CreditPeriodDays, ReceiveByDate, ApprovedDate, FullExtractedText) " +
                    "VALUES (@p0, @p1, GETDATE(), @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15); " +
                    "SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    uploadBatchId,
                    originalFileName ?? string.Empty,
                    uploadedBy,
                    DbValue(excelData.PoNumber),
                    DbValue(null),
                    DbValue("Excel Upload"),
                    DbValue(null),
                    DbValue(null),
                    DbValue(null),
                    DbValue(null),
                    DbValue(null),
                    DbValue(null),
                    DbValue(null),
                    DbValue(null),
                    DbValue(null),
                    DbValue("Excel Upload")).FirstOrDefault();

                if (masterId <= 0)
                {
                    TempData["ErrorMessage"] = "Failed to create temporary record.";
                    return RedirectToAction("Index");
                }

                // Insert detail rows
                foreach (var row in excelData.Items)
                {
                    db.Database.ExecuteSqlCommand(
                        "INSERT INTO TransactionDetailTemp (TransactionMasterTempId, [LineNo], ItemDrugName, HsnCode, Qty, RatePerUnit, GrossAmount) " +
                        "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)",
                        masterId,
                        row.LineNo,
                        DbValue(row.ItemDrugName),
                        DbValue(null),
                        DbValue(row.Qty),
                        DbValue(row.RatePerUnit),
                        DbValue(row.GrossAmount));
                }

                Session["LastExcelUploadBatchId"] = uploadBatchId.ToString();
                Session["LastExcelTransactionMasterTempId"] = masterId;
                TempData["UploadBatchId"] = uploadBatchId.ToString();
                TempData["TransactionMasterTempId"] = masterId;

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error saving Excel data: " + ex.Message;
                return RedirectToAction("Index");
            }
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
                int customerId;
                var customerIdObj = Session["SalesOrderExcelUploadCustomerId"];
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

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesOrderRegisterId)
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

                string poNumberRef = Session["SalesOrderExcelUploadPoNumber"] as string;
                if (string.IsNullOrWhiteSpace(poNumberRef))
                {
                    poNumberRef = "-";
                }
                else
                {
                    poNumberRef = poNumberRef.Trim();
                }

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

                var pCompyId = new SqlParameter("@COMPYID", SqlDbType.Int) { Value = compyId };
                var pSdptId = new SqlParameter("@SDPTID", SqlDbType.Int) { Value = 0 };
                var pRegstrId = new SqlParameter("@REGSTRID", SqlDbType.Int) { Value = SalesOrderRegisterId };
                var pTranBType = new SqlParameter("@TRANBTYPE", SqlDbType.Int) { Value = 2 };
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

                try
                {
                    var createdMaster = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == tranmid);
                    if (createdMaster != null)
                    {
                        createdMaster.CUSRID = userNameForTran;
                        createdMaster.LMUSRID = userNameForTran;
                        createdMaster.TRANETYPE = 0;
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

                Guid batchGuid;
                if (!string.IsNullOrWhiteSpace(uploadBatchId) && Guid.TryParse(uploadBatchId, out batchGuid))
                {
                    db.Database.ExecuteSqlCommand(
                        "DELETE FROM TransactionDetailTemp WHERE TransactionMasterTempId = @p0; DELETE FROM TransactionMasterTemp WHERE UploadBatchId = @p1;",
                        masterTempId,
                        batchGuid);
                }

                Session["LastExcelUploadBatchId"] = null;
                Session["LastExcelTransactionMasterTempId"] = null;
                Session["SalesOrderExcelUploadCustomerId"] = null;
                Session["SalesOrderExcelUploadPoNumber"] = null;

                TempData["SuccessMessage"] = "Sales Order created successfully from Excel upload.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error saving sales order: " + ex.Message;
                return RedirectToAction("Index");
            }
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
    }
}
