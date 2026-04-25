using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using ClosedXML.Excel;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    [Authorize(Roles = "ChellanWiseStockReport")]
    public class ChellanWiseStockReportController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        [Authorize(Roles = "ChellanWiseStockReport")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "ChellanWiseStockReport")]
        public ActionResult GetReport(string fromDate = null, string toDate = null)
        {
            try
            {
                DateTime? parsedFrom = null;
                DateTime? parsedTo = null;

                DateTime temp;
                if (!string.IsNullOrWhiteSpace(fromDate) &&
                    DateTime.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out temp))
                {
                    parsedFrom = temp.Date;
                }

                if (!string.IsNullOrWhiteSpace(toDate) &&
                    DateTime.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out temp))
                {
                    parsedTo = temp.Date;
                }

                var sql = @"
SELECT
    [CHALLAN NO] AS ChallanNo,
    [DATE] AS ChallanDate,
    [PARTY NAME] AS PartyName,
    [Ref NO] AS RefNo,
    [PRODUCT NAME] AS ProductName,
    [BATCHNO] AS BatchNo,
    [QTY] AS Qty,
    [GAMT] AS Gamt,
    [CGSTAMT] AS CgstAmt,
    [SGSTAMT] AS SgstAmt,
    [IGSTAMT] AS IgstAmt,
    [TOTAL] AS Total
FROM VW_CHALLANDETAIL_RPT
WHERE (@FromDate IS NULL OR TRY_CONVERT(date, [DATE], 103) >= @FromDate)
  AND (@ToDate IS NULL OR TRY_CONVERT(date, [DATE], 103) <= @ToDate)
ORDER BY TRY_CONVERT(date, [DATE], 103), [CHALLAN NO], [PRODUCT NAME], [BATCHNO]";

                var parameters = new[]
                {
                    new SqlParameter("@FromDate", SqlDbType.Date) { Value = (object)parsedFrom ?? DBNull.Value },
                    new SqlParameter("@ToDate", SqlDbType.Date) { Value = (object)parsedTo ?? DBNull.Value }
                };

                var rows = db.Database.SqlQuery<ChellanWiseStockRow>(sql, parameters).ToList();

                return Json(new { success = true, data = rows }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, data = new object[0] }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "ChellanWiseStockReport")]
        public ActionResult ExportToExcel(string fromDate = null, string toDate = null, string searchValue = null)
        {
            try
            {
                DateTime? parsedFrom = null;
                DateTime? parsedTo = null;

                DateTime temp;
                if (!string.IsNullOrWhiteSpace(fromDate) &&
                    DateTime.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out temp))
                {
                    parsedFrom = temp.Date;
                }

                if (!string.IsNullOrWhiteSpace(toDate) &&
                    DateTime.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out temp))
                {
                    parsedTo = temp.Date;
                }

                var sql = @"
SELECT
    [CHALLAN NO] AS ChallanNo,
    [DATE] AS ChallanDate,
    [PARTY NAME] AS PartyName,
    [Ref NO] AS RefNo,
    [PRODUCT NAME] AS ProductName,
    [BATCHNO] AS BatchNo,
    [QTY] AS Qty,
    [GAMT] AS Gamt,
    [CGSTAMT] AS CgstAmt,
    [SGSTAMT] AS SgstAmt,
    [IGSTAMT] AS IgstAmt,
    [TOTAL] AS Total
FROM VW_CHALLANDETAIL_RPT
WHERE (@FromDate IS NULL OR TRY_CONVERT(date, [DATE], 103) >= @FromDate)
  AND (@ToDate IS NULL OR TRY_CONVERT(date, [DATE], 103) <= @ToDate)";

                // Add search filter if provided
                if (!string.IsNullOrWhiteSpace(searchValue))
                {
                    sql += "  AND ([CHALLAN NO] LIKE @SearchValue OR [PRODUCT NAME] LIKE @SearchValue)";
                }

                sql += " ORDER BY TRY_CONVERT(date, [DATE], 103), [CHALLAN NO], [PRODUCT NAME], [BATCHNO]";

                var parameters = new List<SqlParameter>
                {
                    new SqlParameter("@FromDate", SqlDbType.Date) { Value = (object)parsedFrom ?? DBNull.Value },
                    new SqlParameter("@ToDate", SqlDbType.Date) { Value = (object)parsedTo ?? DBNull.Value }
                };

                if (!string.IsNullOrWhiteSpace(searchValue))
                {
                    parameters.Add(new SqlParameter("@SearchValue", SqlDbType.NVarChar) { Value = "%" + searchValue + "%" });
                }

                var rows = db.Database.SqlQuery<ChellanWiseStockRow>(sql, parameters.ToArray()).ToList();

                var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Chellan Stock Report");

                // Get company info for header
                var compIdObj = System.Web.HttpContext.Current.Session["COMPID"];
                int? sessionCompId = null;
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
                const int colCount = 13; // A..M
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

                // Report Title with date range
                var title = string.Format(
                    "CHELLAN STOCK VIEW REPORT FROM {0} - {1}",
                    parsedFrom?.ToString("dd-MM-yyyy") ?? "All",
                    parsedTo?.ToString("dd-MM-yyyy") ?? "All");
                ws.Range(row, 1, row, colCount).Merge().Value = title;
                ws.Range(row, 1, row, colCount).Style.Font.Bold = true;
                ws.Range(row, 1, row, colCount).Style.Font.FontSize = 12;
                ws.Range(row, 1, row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row += 2;

                // Column Headers
                var headers = new[] { "S.No", "Challan No", "Date", "Party Name", "Ref No", "Product Name", "Batch No", "Qty", "Gamt", "CGST Amt", "SGST Amt", "IGST Amt", "Total" };
                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cell(row, i + 1).Value = headers[i];
                    ws.Cell(row, i + 1).Style.Font.Bold = true;
                    ws.Cell(row, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#337ab7");
                    ws.Cell(row, i + 1).Style.Font.FontColor = XLColor.White;
                    ws.Cell(row, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                row++;

                // Data rows
                int sno = 1;
                foreach (var r in rows)
                {
                    ws.Cell(row, 1).Value = sno++;
                    ws.Cell(row, 2).Value = r.ChallanNo ?? "";
                    ws.Cell(row, 3).Value = r.ChallanDate ?? "";
                    ws.Cell(row, 4).Value = r.PartyName ?? "";
                    ws.Cell(row, 5).Value = r.RefNo ?? "";
                    ws.Cell(row, 6).Value = r.ProductName ?? "";
                    ws.Cell(row, 7).Value = r.BatchNo ?? "";
                    ws.Cell(row, 8).Value = r.Qty ?? 0;
                    ws.Cell(row, 9).Value = r.Gamt ?? 0;
                    ws.Cell(row, 10).Value = r.CgstAmt ?? 0;
                    ws.Cell(row, 11).Value = r.SgstAmt ?? 0;
                    ws.Cell(row, 12).Value = r.IgstAmt ?? 0;
                    ws.Cell(row, 13).Value = r.Total ?? 0;

                    // Format numeric columns
                    ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 13).Style.NumberFormat.Format = "#,##0.00";

                    row++;
                }

                // Auto-fit columns
                ws.Columns().AdjustToContents();

                // Generate filename with date range
                var fileName = string.Format(
                    "Chellan_stock_view_report_{0}_{1}.xlsx",
                    parsedFrom?.ToString("dd-MM-yyyy") ?? "all",
                    parsedTo?.ToString("dd-MM-yyyy") ?? "all");

                using (var stream = new MemoryStream())
                {
                    wb.SaveAs(stream);
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                // Log error if needed
                return Content("Error generating Excel: " + ex.Message);
            }
        }

        private List<string> SplitAddressLines(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return new List<string>();

            return address.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(line => line.Trim())
                         .Where(line => line.Length > 0)
                         .ToList();
        }

        private class ChellanWiseStockRow
        {
            public string ChallanNo { get; set; }
            public string ChallanDate { get; set; }
            public string PartyName { get; set; }
            public string RefNo { get; set; }
            public string ProductName { get; set; }
            public string BatchNo { get; set; }
            public decimal? Qty { get; set; }
            public decimal? Gamt { get; set; }
            public decimal? CgstAmt { get; set; }
            public decimal? SgstAmt { get; set; }
            public decimal? IgstAmt { get; set; }
            public decimal? Total { get; set; }
        }
    }
}
