using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using KVM_ERP.Models;
using ClosedXML.Excel;
using System.IO;

namespace KVM_ERP.Controllers
{
    [SessionExpire]
    public class StockViewReportController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();

        // GET: StockViewReport
        [Authorize(Roles = "StockViewReportIndex")]
        public ActionResult Index()
        {
            ViewBag.Title = "Stock View Report";
            return View();
        }

        [HttpPost]
        public JsonResult GetStockData(string fromDate, string toDate, string tab = "HL")
        {
            try
            {
                DateTime from = DateTime.Parse(fromDate);
                DateTime to = DateTime.Parse(toDate);

                System.Diagnostics.Debug.WriteLine($"GetStockData called - From: {from:yyyy-MM-dd}, To: {to:yyyy-MM-dd}, Tab: {tab}");

                // Get stock data from TRANSACTION_PRODUCT_CALCULATION table
                var stockData = GetStockViewReportData(from, to, tab);

                return Json(new { success = true, data = stockData }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetStockData: {ex.Message}");
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public ActionResult ExportToExcel(string fromDate, string toDate)
        {
            try
            {
                DateTime from = DateTime.Parse(fromDate);
                DateTime to = DateTime.Parse(toDate);

                // Get all stock data
                var allStockData = GetStockViewReportData(from, to, "ALL");

                using (var workbook = new XLWorkbook())
                {
                    // Create tabs: Overall, Head On, Head Less, etc.
                    var receivedTypes = allStockData
                        .Select(x => string.IsNullOrWhiteSpace(x.ReceivedType) ? "Unknown" : x.ReceivedType)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                    
                    // Tab 1: Overall (all data grouped by packing type)
                    CreateExcelSheetGrouped(workbook, "Overall", allStockData, to);
                    
                    // Tab 2+: Individual ReceivedTypes
                    foreach (var receivedType in receivedTypes)
                    {
                        var filteredData = allStockData.Where(x => 
                            (string.IsNullOrWhiteSpace(x.ReceivedType) ? "Unknown" : x.ReceivedType) == receivedType
                        ).ToList();
                        
                        if (filteredData.Any())
                        {
                            // Ensure sheet name is valid (max 31 chars, no special chars)
                            string sheetName = receivedType;
                            if (string.IsNullOrWhiteSpace(sheetName))
                            {
                                sheetName = "Unknown";
                            }
                            // Remove invalid characters for Excel sheet names
                            sheetName = sheetName.Replace("/", "-").Replace("\\", "-").Replace("?", "").Replace("*", "").Replace("[", "").Replace("]", "");
                            // Limit to 31 characters (Excel limit)
                            if (sheetName.Length > 31)
                            {
                                sheetName = sheetName.Substring(0, 31);
                            }
                            
                            CreateExcelSheet(workbook, sheetName, filteredData, to);
                        }
                    }

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();
                        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"StockViewReport_{to:yyyyMMdd}.xlsx");
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error exporting to Excel: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        private void CreateExcelSheetGrouped(XLWorkbook workbook, string sheetName, List<StockViewReportData> stockData, DateTime toDate)
        {
            if (stockData == null || !stockData.Any())
                return;

            var worksheet = workbook.Worksheets.Add(sheetName);
            
            // Group data by packing type
            var groupedByPackingType = stockData
                .GroupBy(x => new { x.PackingMasterId, x.ReceivedType })
                .OrderBy(g => g.Key.ReceivedType)
                .ToList();

            int row = 1;
            
            // Add main header
            worksheet.Cell(row, 1).Value = $"STOCK AS ON {toDate:dd/MM/yyyy}";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 1).Style.Font.FontSize = 14;
            worksheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(row, 1, row, 20).Merge();
            row += 2;

            // Process each packing type group
            foreach (var group in groupedByPackingType)
            {
                var groupData = group.ToList();
                var columnHeaders = groupData.First().ColumnHeaders ?? new List<string>();
                int totalColumns = columnHeaders.Count + 2;

                // Add packing type header
                worksheet.Cell(row, 1).Value = $"=== {group.Key.ReceivedType} ===";
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 1).Style.Font.FontSize = 12;
                worksheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(100, 149, 237); // Cornflower Blue
                worksheet.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                worksheet.Range(row, 1, row, totalColumns).Merge();
                worksheet.Range(row, 1, row, totalColumns).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row++;

                // Add column headers
                worksheet.Cell(row, 1).Value = "PARTICULARS";
                worksheet.Cell(row, totalColumns).Value = "TOTAL NO. OF SLABS";
                worksheet.Range(row, 1, row + 1, 1).Merge();
                worksheet.Range(row, totalColumns, row + 1, totalColumns).Merge();
                
                row++;
                for (int i = 0; i < columnHeaders.Count; i++)
                {
                    var cell = worksheet.Cell(row, i + 2);
                    cell.Style.NumberFormat.Format = "@"; // Set format to text
                    cell.Value = columnHeaders[i];
                }
                
                // Style headers
                worksheet.Range(row - 1, 1, row, totalColumns).Style.Font.Bold = true;
                worksheet.Range(row - 1, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.LightGray;
                worksheet.Range(row - 1, 1, row, totalColumns).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Range(row - 1, 1, row, totalColumns).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                worksheet.Range(row - 1, 1, row, totalColumns).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row++;

                // Add data rows for this group
                int itemNumber = 1;
                foreach (var item in groupData)
                {
                    var openingData = item.OpeningData ?? new List<decimal>();
                    var productionData = item.ProductionData ?? new List<decimal>();
                    var totalData = item.TotalData ?? new List<decimal>();
                    
                    // Product Name Row (merged, NO total value shown)
                    worksheet.Cell(row, 1).Value = $"{itemNumber}. {item.ProductName}";
                    worksheet.Range(row, 1, row, totalColumns).Merge();
                    worksheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                    worksheet.Cell(row, 1).Style.Font.Bold = true;
                    row++;

                    // OPENING STOCK Row (data before fromDate)
                    worksheet.Cell(row, 1).Value = "OPENING STOCK";
                    for (int i = 0; i < openingData.Count; i++)
                    {
                        worksheet.Cell(row, i + 2).Value = openingData[i];
                    }
                    worksheet.Cell(row, totalColumns).Value = item.OpeningTotalSlabs;
                    worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(227, 242, 253);
                    row++;

                    // PRODUCTION Row (data from fromDate to toDate)
                    worksheet.Cell(row, 1).Value = "PRODUCTION";
                    for (int i = 0; i < productionData.Count; i++)
                    {
                        worksheet.Cell(row, i + 2).Value = productionData[i];
                    }
                    worksheet.Cell(row, totalColumns).Value = item.ProductionTotalSlabs;
                    worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(173, 216, 230);
                    worksheet.Range(row, 1, row, totalColumns).Style.Font.FontColor = XLColor.Blue;
                    row++;

                    // TOTAL Row
                    worksheet.Cell(row, 1).Value = "TOTAL";
                    for (int i = 0; i < totalData.Count; i++)
                    {
                        worksheet.Cell(row, i + 2).Value = totalData[i];
                    }
                    worksheet.Cell(row, totalColumns).Value = item.TotalSlabs;
                    worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(212, 237, 218);
                    worksheet.Range(row, 1, row, totalColumns).Style.Font.Bold = true;
                    row++;

                    // RATE Row
                    worksheet.Cell(row, 1).Value = "RATE";
                    for (int col = 2; col <= totalColumns; col++)
                    {
                        worksheet.Cell(row, col).Value = 0;
                    }
                    worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 215, 218);
                    row++;

                    // AMOUNT Row
                    worksheet.Cell(row, 1).Value = "AMOUNT";
                    for (int col = 2; col <= totalColumns; col++)
                    {
                        worksheet.Cell(row, col).Value = 0;
                    }
                    worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(209, 236, 241);
                    row++;
                    
                    // Add blank row after AMOUNT
                    row++;

                    itemNumber++;
                }

                // Add spacing between groups
                row += 2;
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();
        }

        private void CreateExcelSheet(XLWorkbook workbook, string sheetName, List<StockViewReportData> stockData, DateTime toDate)
        {
            if (stockData == null || !stockData.Any())
                return;

            var worksheet = workbook.Worksheets.Add(sheetName);
            
            // Get column headers from first item (all items in same sheet have same headers)
            var columnHeaders = stockData.First().ColumnHeaders ?? new List<string>();
            int totalColumns = columnHeaders.Count + 2; // +2 for Particulars and Total

            // Add main header - "STOCK AS ON dd/MM/yyyy"
            int row = 1;
            worksheet.Cell(row, 1).Value = $"STOCK AS ON {toDate:dd/MM/yyyy}";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(row, 1, row, totalColumns).Merge();
                    
            // Add column headers - Row 1
            row++;
            worksheet.Cell(row, 1).Value = "PARTICULARS";
            worksheet.Cell(row, totalColumns).Value = "TOTAL NO. OF SLABS";
            
            // Merge PARTICULARS cell from row 2 to row 3
            worksheet.Range(row, 1, row + 1, 1).Merge();
            // Merge TOTAL NO. OF SLABS cell from row 2 to row 3
            worksheet.Range(row, totalColumns, row + 1, totalColumns).Merge();
            
            // Add dynamic column headers - Row 2 (Size columns from PackingTypeMaster)
            row++;
            for (int i = 0; i < columnHeaders.Count; i++)
            {
                var cell = worksheet.Cell(row, i + 2);
                cell.Style.NumberFormat.Format = "@"; // Set format to text
                cell.Value = columnHeaders[i];
            }
            
            // Style headers
            worksheet.Range(2, 1, 3, totalColumns).Style.Font.Bold = true;
            worksheet.Range(2, 1, 3, totalColumns).Style.Fill.BackgroundColor = XLColor.LightGray;
            worksheet.Range(2, 1, 3, totalColumns).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            worksheet.Range(2, 1, 3, totalColumns).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            worksheet.Range(2, 1, 3, totalColumns).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Add data rows
            row++;
            int itemNumber = 1;
            foreach (var item in stockData)
            {
                var openingData = item.OpeningData ?? new List<decimal>();
                var productionData = item.ProductionData ?? new List<decimal>();
                var totalData = item.TotalData ?? new List<decimal>();
                
                // Product Name Row (merged across ALL columns, NO total value shown)
                worksheet.Cell(row, 1).Value = $"{itemNumber}. {item.ProductName}";
                worksheet.Range(row, 1, row, totalColumns).Merge();
                worksheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                row++;

                // OPENING STOCK Row (data before fromDate) - Dynamic
                worksheet.Cell(row, 1).Value = "OPENING STOCK";
                for (int i = 0; i < openingData.Count; i++)
                {
                    worksheet.Cell(row, i + 2).Value = openingData[i];
                }
                worksheet.Cell(row, totalColumns).Value = item.OpeningTotalSlabs;
                worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(227, 242, 253); // Light Blue
                row++;

                // PRODUCTION Row (data from fromDate to toDate) - Dynamic
                worksheet.Cell(row, 1).Value = "PRODUCTION";
                for (int i = 0; i < productionData.Count; i++)
                {
                    worksheet.Cell(row, i + 2).Value = productionData[i];
                }
                worksheet.Cell(row, totalColumns).Value = item.ProductionTotalSlabs;
                worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(173, 216, 230); // Blue
                worksheet.Range(row, 1, row, totalColumns).Style.Font.FontColor = XLColor.Blue;
                row++;

                // TOTAL Row (Opening + Production) - Dynamic
                worksheet.Cell(row, 1).Value = "TOTAL";
                for (int i = 0; i < totalData.Count; i++)
                {
                    worksheet.Cell(row, i + 2).Value = totalData[i];
                }
                worksheet.Cell(row, totalColumns).Value = item.TotalSlabs;
                worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(212, 237, 218); // Light Green
                worksheet.Range(row, 1, row, totalColumns).Style.Font.Bold = true;
                row++;

                // RATE Row (all zeros) - Dynamic
                worksheet.Cell(row, 1).Value = "RATE";
                for (int col = 2; col <= totalColumns; col++)
                {
                    worksheet.Cell(row, col).Value = 0;
                }
                worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 215, 218); // Light Red
                row++;

                // AMOUNT Row (all zeros) - Dynamic
                worksheet.Cell(row, 1).Value = "AMOUNT";
                for (int col = 2; col <= totalColumns; col++)
                {
                    worksheet.Cell(row, col).Value = 0;
                }
                worksheet.Range(row, 1, row, totalColumns).Style.Fill.BackgroundColor = XLColor.FromArgb(209, 236, 241); // Light Cyan
                row++;
                
                // Add blank row after AMOUNT for spacing between products
                row++;

                itemNumber++;
            }

            // Add borders to all data cells
            worksheet.Range(2, 1, row - 1, totalColumns).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            worksheet.Range(2, 1, row - 1, totalColumns).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            
            // Center align all data cells
            worksheet.Range(4, 2, row - 1, totalColumns).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();
        }

        private List<StockViewReportData> GetStockViewReportData(DateTime fromDate, DateTime toDate, string tab)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"GetStockViewReportData - From: {fromDate:yyyy-MM-dd}, To: {toDate:yyyy-MM-dd}, Tab: {tab}");
                System.Diagnostics.Debug.WriteLine($"Opening Stock: Data BEFORE {fromDate:yyyy-MM-dd}");
                System.Diagnostics.Debug.WriteLine($"Production: Data FROM {fromDate:yyyy-MM-dd} TO {toDate:yyyy-MM-dd}");

                var stockData = new List<StockViewReportData>();

                // Call stored procedure to get stock data with proper date filtering
                // Opening Stock: Data BEFORE fromDate (DateCategory = 0)
                // Production: Data FROM fromDate TO toDate (DateCategory = 1)
                var allData = db.Database.SqlQuery<StockDataDTO>(
                    "EXEC pr_GetStockViewReportData @FromDate, @ToDate",
                    new System.Data.SqlClient.SqlParameter("@FromDate", fromDate),
                    new System.Data.SqlClient.SqlParameter("@ToDate", toDate)
                ).ToList();

                System.Diagnostics.Debug.WriteLine($"Stored procedure returned {allData.Count} records");

                // Group by packing master and product
                var groupedByPackingAndProduct = allData
                    .GroupBy(x => new { x.PackingMasterId, x.PackingMasterName, x.ProductId, x.ProductName, x.KGWGT, x.GradeName, x.ColorName, x.ReceivedTypeName })
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Grouped into {groupedByPackingAndProduct.Count} unique products");

                foreach (var productGroup in groupedByPackingAndProduct)
                {
                    // Get column headers for this packing master from PackingTypeMaster
                    var packingTypes = db.PackingTypeMasters
                        .Where(pt => pt.PACKMID == productGroup.Key.PackingMasterId
                                  && (pt.DISPSTATUS == 0 || pt.DISPSTATUS == null))
                        .OrderBy(pt => pt.PACKTMCODE)
                        .ToList();

                    var columnHeaders = new List<string>();
                    foreach (var pt in packingTypes)
                    {
                        var desc = pt.PACKTMDESC ?? string.Empty;
                        var upper = desc.ToUpper().Trim();

                        // Exclude BKN/BROKEN and OTHERS from slab headers – they are handled separately
                        if (upper == "BKN" || upper == "BROKEN" || upper.Contains("BKN"))
                            continue;

                        if (upper == "OTHERS" || upper == "OTHER" || upper.Contains("OTHERS"))
                            continue;

                        columnHeaders.Add(desc);
                    }

                    // Split data based on DateCategory from stored procedure
                    // DateCategory 0 = Opening Stock (before fromDate)
                    // DateCategory 1 = Production (fromDate to toDate)
                    var openingData = productGroup.Where(x => x.DateCategory == 0).ToList();
                    var productionData = productGroup.Where(x => x.DateCategory == 1).ToList();

                    // Build dynamic data arrays
                    var openingDataArray = new List<decimal>();
                    var productionDataArray = new List<decimal>();
                    var totalDataArray = new List<decimal>();

                    // Get PCK values (PCK1-PCK17)
                    var pckProperties = new[] { "PCK1", "PCK2", "PCK3", "PCK4", "PCK5", "PCK6", "PCK7", "PCK8", "PCK9", "PCK10", "PCK11", "PCK12", "PCK13", "PCK14", "PCK15", "PCK16", "PCK17" };
                    
                    for (int i = 0; i < columnHeaders.Count && i < pckProperties.Length; i++)
                    {
                        var prop = typeof(StockDataDTO).GetProperty(pckProperties[i]);
                        
                        decimal openingSum = 0;
                        foreach (var dataItem in openingData)
                        {
                            var value = prop.GetValue(dataItem);
                            openingSum += value != null ? (decimal)value : 0;
                        }
                        openingDataArray.Add(openingSum);

                        decimal productionSum = 0;
                        foreach (var dataItem in productionData)
                        {
                            var value = prop.GetValue(dataItem);
                            productionSum += value != null ? (decimal)value : 0;
                        }
                        productionDataArray.Add(productionSum);

                        totalDataArray.Add(openingSum + productionSum);
                    }

                    // Calculate totals across all slab columns
                    decimal openingTotal = openingDataArray.Sum();
                    decimal productionTotal = productionDataArray.Sum();
                    decimal total = totalDataArray.Sum();

                    // If there are no slabs at all for this product/packing combination,
                    // skip it. These zero-slab groups typically come from BKN/OTHERS-only
                    // calculations which are already handled separately as virtual products
                    // at the bottom of this method.
                    if (openingTotal == 0 && productionTotal == 0 && total == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping product group {productGroup.Key.ProductName} (PACKMID {productGroup.Key.PackingMasterId}) because all slab totals are zero.");
                        continue;
                    }

                    // Create StockViewReportData item
                    var item = new StockViewReportData
                    {
                        ProductName = $"{productGroup.Key.ProductName} 6 x {productGroup.Key.KGWGT:F1}" +
                                     (!string.IsNullOrEmpty(productGroup.Key.GradeName) ? $" - {productGroup.Key.GradeName}" : "") +
                                     (!string.IsNullOrEmpty(productGroup.Key.ColorName) ? $" - {productGroup.Key.ColorName}" : "") +
                                     (!string.IsNullOrEmpty(productGroup.Key.ReceivedTypeName) ? $" - {productGroup.Key.ReceivedTypeName}" : ""),
                        ReceivedType = string.IsNullOrWhiteSpace(productGroup.Key.PackingMasterName) ? "Unknown" : productGroup.Key.PackingMasterName,
                        PackingMasterId = productGroup.Key.PackingMasterId,
                        
                        // Dynamic columns
                        ColumnHeaders = columnHeaders,
                        OpeningData = openingDataArray,
                        OpeningTotalSlabs = openingTotal,
                        ProductionData = productionDataArray,
                        ProductionTotalSlabs = productionTotal,
                        TotalData = totalDataArray,
                        TotalSlabs = total
                    };

                    stockData.Add(item);
                }

                // Add separate summary sections for BKN and Others (like Stock View virtual products)
                try
                {
                    // Common query for BKN and OTHERS with date and status filters
                    // NOTE: Use PRODDATE from TransactionProductCalculations as the effective production date
                    var baseCalcQuery = from tpc in db.TransactionProductCalculations
                                        join td in db.TransactionDetails on tpc.TRANDID equals td.TRANDID
                                        join m in db.MaterialMasters on td.MTRLID equals m.MTRLID
                                        join tm in db.TransactionMasters on td.TRANMID equals tm.TRANMID
                                        where (tpc.DISPSTATUS == 0 || tpc.DISPSTATUS == null)
                                              && (m.DISPSTATUS == 0 || m.DISPSTATUS == null)
                                              && (tm.DISPSTATUS == 0 || tm.DISPSTATUS == null)
                                              && tpc.PRODDATE <= toDate
                                        select new
                                        {
                                            TRANDATE = tpc.PRODDATE,
                                            tpc.BKN,
                                            tpc.OTHERS
                                        };

                    var bknData = baseCalcQuery.Where(x => x.BKN > 0).ToList();
                    if (bknData.Any())
                    {
                        decimal openingBkn = bknData.Where(x => x.TRANDATE < fromDate).Sum(x => x.BKN);
                        decimal productionBkn = bknData.Where(x => x.TRANDATE >= fromDate && x.TRANDATE <= toDate).Sum(x => x.BKN);
                        decimal totalBkn = openingBkn + productionBkn;

                        if (totalBkn > 0)
                        {
                            var bknItem = new StockViewReportData
                            {
                                ProductName = "BKN (Broken)",
                                ReceivedType = "BKN (Broken)",
                                PackingMasterId = -1,
                                ColumnHeaders = new List<string> { "BKN (KG)" },
                                OpeningData = new List<decimal> { openingBkn },
                                OpeningTotalSlabs = openingBkn,
                                ProductionData = new List<decimal> { productionBkn },
                                ProductionTotalSlabs = productionBkn,
                                TotalData = new List<decimal> { totalBkn },
                                TotalSlabs = totalBkn
                            };

                            stockData.Add(bknItem);
                        }
                    }

                    var othersData = baseCalcQuery.Where(x => x.OTHERS > 0).ToList();
                    if (othersData.Any())
                    {
                        decimal openingOthers = othersData.Where(x => x.TRANDATE < fromDate).Sum(x => x.OTHERS);
                        decimal productionOthers = othersData.Where(x => x.TRANDATE >= fromDate && x.TRANDATE <= toDate).Sum(x => x.OTHERS);
                        decimal totalOthers = openingOthers + productionOthers;

                        if (totalOthers > 0)
                        {
                            var othersItem = new StockViewReportData
                            {
                                ProductName = "Others(Peeled)",
                                ReceivedType = "Others(Peeled)",
                                PackingMasterId = -2,
                                ColumnHeaders = new List<string> { "Others(Peeled) (KG)" },
                                OpeningData = new List<decimal> { openingOthers },
                                OpeningTotalSlabs = openingOthers,
                                ProductionData = new List<decimal> { productionOthers },
                                ProductionTotalSlabs = productionOthers,
                                TotalData = new List<decimal> { totalOthers },
                                TotalSlabs = totalOthers
                            };

                            stockData.Add(othersItem);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error adding BKN/Others summary rows: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine($"Combined into {stockData.Count} products (including BKN/Others summaries if any)");
                stockData = stockData.OrderBy(x => x.ReceivedType).ThenBy(x => x.ProductName).ToList();
                
                return stockData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetStockViewReportData: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<StockViewReportData>();
            }
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

    // Model for Stock View Report Data with Dynamic Columns
    public class StockViewReportData
    {
        public string ProductName { get; set; }
        public string ReceivedType { get; set; }
        public int PackingMasterId { get; set; }
        
        // Dynamic column headers for this packing type
        public List<string> ColumnHeaders { get; set; }
        
        // Opening Stock (before fromDate) - Dynamic data
        public List<decimal> OpeningData { get; set; }
        public decimal OpeningTotalSlabs { get; set; }
        
        // Production (fromDate to toDate) - Dynamic data
        public List<decimal> ProductionData { get; set; }
        public decimal ProductionTotalSlabs { get; set; }
        
        // Total (Opening + Production) - Dynamic data
        public List<decimal> TotalData { get; set; }
        public decimal TotalSlabs { get; set; }
    }

    // DTO for Stored Procedure pr_GetStockViewReportData
    public class StockDataDTO
    {
        public DateTime TRANDATE { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int PackingMasterId { get; set; }
        public string PackingMasterName { get; set; }
        public decimal KGWGT { get; set; }
        public string GradeName { get; set; }
        public string ColorName { get; set; }
        public string ReceivedTypeName { get; set; }
        
        // PCK columns
        public decimal PCK1 { get; set; }
        public decimal PCK2 { get; set; }
        public decimal PCK3 { get; set; }
        public decimal PCK4 { get; set; }
        public decimal PCK5 { get; set; }
        public decimal PCK6 { get; set; }
        public decimal PCK7 { get; set; }
        public decimal PCK8 { get; set; }
        public decimal PCK9 { get; set; }
        public decimal PCK10 { get; set; }
        public decimal PCK11 { get; set; }
        public decimal PCK12 { get; set; }
        public decimal PCK13 { get; set; }
        public decimal PCK14 { get; set; }
        public decimal PCK15 { get; set; }
        public decimal PCK16 { get; set; }
        public decimal PCK17 { get; set; }
        
        // Date category: 0 = Opening (before fromDate), 1 = Production (fromDate to toDate)
        public int DateCategory { get; set; }
    }
}
