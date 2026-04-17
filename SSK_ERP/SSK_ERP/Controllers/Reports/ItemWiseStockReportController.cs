using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using ClosedXML.Excel;
using SSK_ERP.Filters;
using SSK_ERP.Models;
using System.Data.SqlClient;
using System.Configuration;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    [Authorize(Roles = "ItemWiseStockReport")]
    public class ItemWiseStockReportController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        [Authorize(Roles = "ItemWiseStockReport")]
        public ActionResult Index()
        {
            var materials = db.MaterialMasters
                .Where(m => m.DISPSTATUS == 0)
                .OrderBy(m => m.MTRLDESC)
                .ToList();

            ViewBag.MaterialList = materials;
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "ItemWiseStockReport")]
        public ActionResult GetMaterialStock(string fromDate = null, string toDate = null, string materialIds = null)
        {
            try
            {
                DateTime? parsedFrom = null;
                DateTime? parsedTo = null;

                DateTime temp;
                if (!string.IsNullOrWhiteSpace(fromDate) &&
                    DateTime.TryParseExact(fromDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out temp))
                {
                    parsedFrom = temp.Date;
                }

                if (!string.IsNullOrWhiteSpace(toDate) &&
                    DateTime.TryParseExact(toDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out temp))
                {
                    parsedTo = temp.Date;
                }

                var selectedMaterialIds = new HashSet<int>();
                if (!string.IsNullOrWhiteSpace(materialIds))
                {
                    var parts = materialIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        int id;
                        if (int.TryParse(part, out id))
                        {
                            selectedMaterialIds.Add(id);
                        }
                    }
                }

                var selectedMaterials = db.MaterialMasters
                    .Where(m => selectedMaterialIds.Contains(m.MTRLID))
                    .ToDictionary(m => m.MTRLID, m => m.MTRLDESC);

                var stockData = new List<dynamic>();

                string connectionString = ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"].ConnectionString;
                
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    foreach (int materialId in selectedMaterialIds)
                    {
                        using (SqlCommand command = new SqlCommand("PR_ItemStockLedger", connection))
                        {
                            command.CommandType = System.Data.CommandType.StoredProcedure;
                            command.Parameters.AddWithValue("@FromDate", parsedFrom.HasValue ? (object)parsedFrom.Value : DBNull.Value);
                            command.Parameters.AddWithValue("@ToDate", parsedTo.HasValue ? (object)parsedTo.Value : DBNull.Value);
                            command.Parameters.AddWithValue("@TRANDREFID", materialId);

                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    stockData.Add(new
                                    {
                                        MTRLID = materialId,
                                        MaterialName = GetString(reader, "ItemName", selectedMaterials.ContainsKey(materialId) ? selectedMaterials[materialId] : ""),
                                        BatchNo = GetString(reader, "BatchNumber"),
                                        BillNumber = GetString(reader, "BillNumber"),
                                        TranDate = GetDateString(reader, "Date"),
                                        TranType = GetString(reader, "Type"),
                                        PartyName = GetString(reader, "CustomerName"),
                                        QtyIN = GetDecimal(reader, "InQty"),
                                        QtyOUT = GetDecimal(reader, "OutQty"),
                                        Amount = GetDecimal(reader, "Value"),
                                        Balance = GetDecimal(reader, "Balance")
                                    });
                                }
                            }
                        }
                    }
                }

                return Json(new { success = true, data = stockData }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "ItemWiseStockReport")]
        public ActionResult ExportMaterialStockExcel(string fromDate = null, string toDate = null, string materialIds = null)
        {
            try
            {
                DateTime parsedFrom;
                DateTime parsedTo;

                if (!DateTime.TryParseExact(fromDate ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedFrom) ||
                    !DateTime.TryParseExact(toDate ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTo))
                {
                    return Content("Invalid date range.");
                }

                var selectedMaterialIdsOrdered = ParseMaterialIdsOrdered(materialIds);
                if (selectedMaterialIdsOrdered.Count == 0)
                {
                    return Content("No materials selected.");
                }

                int? sessionCompId = null;
                var compIdObj = Session != null ? Session["COMPID"] : null;
                if (compIdObj != null)
                {
                    int compIdParsed;
                    if (int.TryParse(compIdObj.ToString(), out compIdParsed))
                    {
                        sessionCompId = compIdParsed;
                    }
                }

                var company = (sessionCompId.HasValue && sessionCompId.Value > 0)
                    ? db.companymasters.FirstOrDefault(c => c.COMPID == sessionCompId.Value)
                    : null;
                if (company == null)
                {
                    company = db.companymasters.OrderBy(c => c.COMPID).FirstOrDefault();
                }

                var materialNames = db.MaterialMasters
                    .Where(m => selectedMaterialIdsOrdered.Contains(m.MTRLID))
                    .ToDictionary(m => m.MTRLID, m => m.MTRLDESC);

                var rowsByMaterial = new Dictionary<int, List<StockLedgerRow>>();

                string connectionString = ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"].ConnectionString;
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    foreach (var materialId in selectedMaterialIdsOrdered)
                    {
                        var rows = new List<StockLedgerRow>();

                        using (var command = new SqlCommand("PR_ItemStockLedger", connection))
                        {
                            command.CommandType = System.Data.CommandType.StoredProcedure;
                            command.Parameters.AddWithValue("@FromDate", parsedFrom.Date);
                            command.Parameters.AddWithValue("@ToDate", parsedTo.Date);
                            command.Parameters.AddWithValue("@TRANDREFID", materialId);

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    rows.Add(new StockLedgerRow
                                    {
                                        MaterialId = materialId,
                                        ItemName = GetString(reader, "ItemName", materialNames.ContainsKey(materialId) ? materialNames[materialId] : ""),
                                        BatchNumber = GetString(reader, "BatchNumber"),
                                        BillNumber = GetString(reader, "BillNumber"),
                                        TranDate = GetDate(reader, "Date"),
                                        TranType = GetString(reader, "Type"),
                                        PartyName = GetString(reader, "CustomerName"),
                                        InQty = GetDecimal(reader, "InQty"),
                                        OutQty = GetDecimal(reader, "OutQty"),
                                        Value = GetDecimal(reader, "Value"),
                                        Balance = GetDecimal(reader, "Balance")
                                    });
                                }
                            }
                        }

                        rowsByMaterial[materialId] = rows;
                    }
                }

                var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("ITEM_WISE_STOCK_REPORT");

                const int colCount = 9; // A..I
                int row = 1;

                // Company Header
                var companyName = (company != null && !string.IsNullOrWhiteSpace(company.COMPNAME))
                    ? company.COMPNAME.Trim()
                    : "SSK ENTERPRISE";

                ws.Range(row, 1, row, colCount).Merge().Value = companyName.ToUpperInvariant();
                ws.Range(row, 1, row, colCount).Style.Font.Bold = true;
                ws.Range(row, 1, row, colCount).Style.Font.FontSize = 16;
                ws.Range(row, 1, row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row++;

                var addrLines = SplitAddressLines(company != null ? company.COMPADDR : null);
                foreach (var line in addrLines)
                {
                    ws.Range(row, 1, row, colCount).Merge().Value = line;
                    ws.Range(row, 1, row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    row++;
                }

                var phone = company != null ? (company.COMPPHN3 ?? company.COMPPHN1 ?? company.COMPPHN2 ?? company.COMPPHN4) : null;
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    ws.Range(row, 1, row, colCount).Merge().Value = "Phone : " + phone.Trim();
                    ws.Range(row, 1, row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    row++;
                }

                var gstin = company != null ? company.COMPGSTNO : null;
                if (!string.IsNullOrWhiteSpace(gstin))
                {
                    ws.Range(row, 1, row, colCount).Merge().Value = "GSTIN : " + gstin.Trim();
                    ws.Range(row, 1, row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    row++;
                }

                row++; // blank line

                var title = string.Format(
                    "ITEM WISE STOCK REPORT FROM {0} - {1}",
                    parsedFrom.ToString("dd-MM-yyyy"),
                    parsedTo.ToString("dd-MM-yyyy"));
                ws.Range(row, 1, row, colCount).Merge().Value = title;
                ws.Range(row, 1, row, colCount).Style.Font.Bold = true;
                ws.Range(row, 1, row, colCount).Style.Font.FontSize = 12;
                ws.Range(row, 1, row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row += 2;

                // Column Headers (once)
                WriteColumnHeaders(ws, row);
                row++;

                foreach (var materialId in selectedMaterialIdsOrdered)
                {
                    var materialTitle = materialNames.ContainsKey(materialId) ? materialNames[materialId] : ("Material " + materialId);

                    // Material Header Row
                    var matRange = ws.Range(row, 1, row, colCount);
                    matRange.Merge().Value = materialTitle;
                    matRange.Style.Font.Bold = true;
                    matRange.Style.Font.FontSize = 12;
                    matRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e9f3ff");
                    matRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    matRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    row++;

                    var materialRows = rowsByMaterial.ContainsKey(materialId) ? rowsByMaterial[materialId] : new List<StockLedgerRow>();
                    var openingRow = materialRows.FirstOrDefault(r => string.Equals(r.TranType, "Opening", StringComparison.OrdinalIgnoreCase));
                    if (openingRow != null)
                    {
                        // Opening Balance Line
                        var openTextRange = ws.Range(row, 1, row, colCount - 1); // A..H
                        openTextRange.Merge().Value = "Opening Balance as on " + parsedFrom.ToString("dd-MM-yyyy");
                        openTextRange.Style.Font.Bold = true;
                        openTextRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f7f7f7");
                        openTextRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        openTextRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                        var openBalCell = ws.Cell(row, colCount); // I
                        openBalCell.Value = openingRow.Balance;
                        openBalCell.Style.Font.Bold = true;
                        openBalCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f7f7f7");
                        openBalCell.Style.NumberFormat.Format = "0.00";
                        openBalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        openBalCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                        row++;
                    }

                    foreach (var r in materialRows.Where(r => !string.Equals(r.TranType, "Opening", StringComparison.OrdinalIgnoreCase)))
                    {
                        ws.Cell(row, 1).Value = r.BillNumber;
                        ws.Cell(row, 2).Value = r.TranDate.HasValue ? r.TranDate.Value.ToString("dd-MM-yyyy") : "";
                        ws.Cell(row, 3).Value = r.TranType;
                        ws.Cell(row, 4).Value = r.PartyName;
                        ws.Cell(row, 5).Value = r.BatchNumber;
                        ws.Cell(row, 6).Value = r.InQty;
                        ws.Cell(row, 7).Value = r.OutQty;
                        ws.Cell(row, 8).Value = r.Value;
                        ws.Cell(row, 9).Value = r.Balance;

                        var dataRange = ws.Range(row, 1, row, colCount);
                        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        ws.Cell(row, 6).Style.NumberFormat.Format = "0.00";
                        ws.Cell(row, 7).Style.NumberFormat.Format = "0.00";
                        ws.Cell(row, 8).Style.NumberFormat.Format = "0.00";
                        ws.Cell(row, 9).Style.NumberFormat.Format = "0.00";
                        ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        ws.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        ws.Cell(row, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        row++;
                    }

                    row++; // blank line between materials
                }

                ws.Columns(1, colCount).AdjustToContents();

                var fileName = string.Format(
                    "ITEM_WISE_STOCK_REPORT_{0}_{1}.xlsx",
                    parsedFrom.ToString("yyyy-MM-dd"),
                    parsedTo.ToString("yyyy-MM-dd"));

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return File(
                        ms.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileName);
                }
            }
            catch (Exception ex)
            {
                return Content("Error exporting Excel: " + ex.Message);
            }
        }

        private class StockLedgerRow
        {
            public int MaterialId { get; set; }
            public string ItemName { get; set; }
            public string BatchNumber { get; set; }
            public string BillNumber { get; set; }
            public DateTime? TranDate { get; set; }
            public string TranType { get; set; }
            public string PartyName { get; set; }
            public decimal InQty { get; set; }
            public decimal OutQty { get; set; }
            public decimal Value { get; set; }
            public decimal Balance { get; set; }
        }

        private static List<int> ParseMaterialIdsOrdered(string materialIds)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();

            if (string.IsNullOrWhiteSpace(materialIds))
            {
                return result;
            }

            var parts = materialIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                int id;
                if (int.TryParse(part, out id) && id > 0 && !seen.Contains(id))
                {
                    seen.Add(id);
                    result.Add(id);
                }
            }

            return result;
        }

        private static List<string> SplitAddressLines(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return new List<string>();
            }

            var normalized = address.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            // Prefer comma-aware wrapping so the Excel header looks like the sample:
            // a few centered lines, not one line per comma-separated segment.
            var commaTokens = normalized
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            List<string> lines;
            if (commaTokens.Count > 1)
            {
                lines = WrapTokens(commaTokens, 60, ", ");
            }
            else
            {
                var rawLines = normalized
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                lines = WrapTokens(rawLines, 60, " ");
            }

            return lines.Select(l => l.ToUpperInvariant()).ToList();
        }

        private static List<string> WrapTokens(List<string> tokens, int maxLen, string joiner)
        {
            var result = new List<string>();
            var current = "";

            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(current))
                {
                    current = token;
                    continue;
                }

                var candidate = current + joiner + token;
                if (candidate.Length > maxLen)
                {
                    result.Add(current);
                    current = token;
                }
                else
                {
                    current = candidate;
                }
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                result.Add(current);
            }

            return result;
        }

        private static void WriteColumnHeaders(IXLWorksheet ws, int row)
        {
            ws.Cell(row, 1).Value = "Bill Number";
            ws.Cell(row, 2).Value = "Date";
            ws.Cell(row, 3).Value = "Type";
            ws.Cell(row, 4).Value = "Party Name";
            ws.Cell(row, 5).Value = "Batch No.";
            ws.Cell(row, 6).Value = "Qty IN";
            ws.Cell(row, 7).Value = "Qty OUT";
            ws.Cell(row, 8).Value = "Value";
            ws.Cell(row, 9).Value = "Balance";

            var headerRange = ws.Range(row, 1, row, 9);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#d9d9d9");
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        private static string GetString(SqlDataReader reader, string columnName, string defaultValue = "")
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetValue(ordinal).ToString();
        }

        private static DateTime? GetDate(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(ordinal));
        }

        private static string GetDateString(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? "" : Convert.ToDateTime(reader.GetValue(ordinal)).ToString("yyyy-MM-dd");
        }

        private static decimal GetDecimal(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal));
        }
    }
}
