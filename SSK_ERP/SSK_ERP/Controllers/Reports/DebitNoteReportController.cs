using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class DebitNoteReportController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        private const string DnConsolidatedView = "VW_DEBITNOTE_CONSOLIDATE_RPT";
        private const string DnDetailedView = "VW_DEBITNOTE_DETAILED_RPT";

        private static string ResolveDateColumn(DataTable table)
        {
            if (table == null)
            {
                return null;
            }

            foreach (DataColumn col in table.Columns)
            {
                var name = col.ColumnName ?? string.Empty;
                var normalized = new string(name.Where(ch => !char.IsWhiteSpace(ch) && ch != '_').ToArray())
                    .ToLowerInvariant();

                if (normalized == "debitnotedate" ||
                    normalized == "dndate" ||
                    normalized == "trandate" ||
                    normalized == "date")
                {
                    return col.ColumnName;
                }
            }

            foreach (DataColumn col in table.Columns)
            {
                if (col.DataType == typeof(DateTime))
                {
                    var name = col.ColumnName ?? string.Empty;
                    if (name.IndexOf("date", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return col.ColumnName;
                    }
                }
            }

            return null;
        }

        private string ResolveDateColumnForView(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
            {
                return null;
            }

            var table = new DataTable();
            var conn = db.Database.Connection;
            bool shouldClose = false;
            if (conn.State != ConnectionState.Open)
            {
                conn.Open();
                shouldClose = true;
            }

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 0 * FROM " + viewName;
                    using (var reader = cmd.ExecuteReader())
                    {
                        table.Load(reader);
                    }
                }
            }
            finally
            {
                if (shouldClose)
                {
                    conn.Close();
                }
            }

            return ResolveDateColumn(table);
        }

        private static string ResolveSupplierNameColumn(DataTable table)
        {
            if (table == null)
            {
                return null;
            }

            foreach (DataColumn col in table.Columns)
            {
                var name = col.ColumnName ?? string.Empty;
                var normalized = new string(name.Where(ch => !char.IsWhiteSpace(ch) && ch != '_').ToArray())
                    .ToLowerInvariant();

                if (normalized == "suppliername" ||
                    normalized == "supplier" ||
                    normalized == "supplierdesc" ||
                    normalized == "supplierdescription" ||
                    normalized == "tranrefname" ||
                    normalized == "partyname" ||
                    normalized == "party" ||
                    normalized == "vendorname" ||
                    normalized == "vendor")
                {
                    return col.ColumnName;
                }
            }

            return null;
        }

        [Authorize(Roles = "DebitNoteReport")]
        public ActionResult Index()
        {
            var suppliers = db.SupplierMasters
                .Where(s => s.DISPSTATUS == 0)
                .OrderBy(s => s.CATENAME)
                .ToList();

            ViewBag.SupplierList = suppliers;
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "DebitNoteReport")]
        public FileResult ExportDatewiseConsolidated(string fromDate = null, string toDate = null)
        {
            return ExportFromView(DnConsolidatedView, "DEBIT NOTE DATEWISE - CONSOLIDATED", "SSK_ENTERPRISE_DEBIT_NOTE_DATEWISE_CONSOLIDATED", fromDate, toDate, null);
        }

        [HttpGet]
        [Authorize(Roles = "DebitNoteReport")]
        public FileResult ExportDatewiseDetailed(string fromDate = null, string toDate = null)
        {
            return ExportFromView(DnDetailedView, "DEBIT NOTE DATEWISE - DETAILED", "SSK_ENTERPRISE_DEBIT_NOTE_DATEWISE_DETAILED", fromDate, toDate, null);
        }

        [HttpGet]
        [Authorize(Roles = "DebitNoteReport")]
        public FileResult ExportSupplierwiseConsolidated(string fromDate = null, string toDate = null, string supplierIds = null)
        {
            return ExportFromView(DnConsolidatedView, "DEBIT NOTE SUPPLIER WISE - CONSOLIDATED", "SSK_ENTERPRISE_DEBIT_NOTE_SUPPLIER_WISE_CONSOLIDATED", fromDate, toDate, supplierIds);
        }

        [HttpPost]
        [Authorize(Roles = "DebitNoteReport")]
        public FileResult ExportSupplierwiseConsolidatedPost(string fromDate = null, string toDate = null, string supplierIds = null)
        {
            return ExportSupplierwiseConsolidated(fromDate, toDate, supplierIds);
        }

        [HttpGet]
        [Authorize(Roles = "DebitNoteReport")]
        public FileResult ExportSupplierwiseDetailed(string fromDate = null, string toDate = null, string supplierIds = null)
        {
            return ExportFromView(DnDetailedView, "DEBIT NOTE SUPPLIER WISE - DETAILED", "SSK_ENTERPRISE_DEBIT_NOTE_SUPPLIER_WISE_DETAILED", fromDate, toDate, supplierIds);
        }

        [HttpPost]
        [Authorize(Roles = "DebitNoteReport")]
        public FileResult ExportSupplierwiseDetailedPost(string fromDate = null, string toDate = null, string supplierIds = null)
        {
            return ExportSupplierwiseDetailed(fromDate, toDate, supplierIds);
        }

        private FileResult ExportFromView(
            string viewName,
            string reportHeader,
            string fileNamePrefix,
            string fromDate,
            string toDate,
            string supplierIds)
        {
            DateTime? parsedFrom = null;
            DateTime? parsedToExclusive = null;

            DateTime temp;
            if (!string.IsNullOrWhiteSpace(fromDate) &&
                DateTime.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out temp))
            {
                parsedFrom = temp.Date;
            }

            if (!string.IsNullOrWhiteSpace(toDate) &&
                DateTime.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out temp))
            {
                parsedToExclusive = temp.Date.AddDays(1);
            }

            string displayFrom = parsedFrom.HasValue ? parsedFrom.Value.ToString("dd-MM-yyyy") : string.Empty;
            DateTime? inclusiveTo = parsedToExclusive.HasValue ? parsedToExclusive.Value.AddDays(-1) : (DateTime?)null;
            string displayTo = inclusiveTo.HasValue ? inclusiveTo.Value.ToString("dd-MM-yyyy") : string.Empty;

            var selectedSupplierIds = new HashSet<int>();
            if (!string.IsNullOrWhiteSpace(supplierIds))
            {
                var parts = supplierIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    int id;
                    if (int.TryParse(part, out id))
                    {
                        selectedSupplierIds.Add(id);
                    }
                }
            }

            HashSet<string> allowedSupplierNames = null;
            if (selectedSupplierIds.Count > 0)
            {
                var names = db.SupplierMasters
                    .Where(s => selectedSupplierIds.Contains(s.CATEID))
                    .Select(s => new { s.CATENAME, s.CATEDNAME })
                    .ToList();

                if (names.Count > 0)
                {
                    allowedSupplierNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var n in names)
                    {
                        if (!string.IsNullOrWhiteSpace(n.CATENAME))
                        {
                            allowedSupplierNames.Add(n.CATENAME.Trim());
                        }
                        if (!string.IsNullOrWhiteSpace(n.CATEDNAME))
                        {
                            allowedSupplierNames.Add(n.CATEDNAME.Trim());
                        }
                    }
                }
            }

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM ").Append(viewName).Append(" WHERE 1 = 1 ");

            var dateColumn = ResolveDateColumnForView(viewName);

            var parameters = new List<object>();
            int paramIndex = 0;

            if (parsedFrom.HasValue)
            {
                if (!string.IsNullOrWhiteSpace(dateColumn))
                {
                    sql.Append("AND [").Append(dateColumn).Append("] >= @p" + paramIndex + " ");
                    parameters.Add(parsedFrom.Value);
                    paramIndex++;
                }
            }

            if (parsedToExclusive.HasValue)
            {
                if (!string.IsNullOrWhiteSpace(dateColumn))
                {
                    sql.Append("AND [").Append(dateColumn).Append("] < @p" + paramIndex + " ");
                    parameters.Add(parsedToExclusive.Value);
                    paramIndex++;
                }
            }

            var table = new DataTable();
            var exportConn = db.Database.Connection;
            bool exportShouldClose = false;
            if (exportConn.State != ConnectionState.Open)
            {
                exportConn.Open();
                exportShouldClose = true;
            }

            try
            {
                using (var cmd = exportConn.CreateCommand())
                {
                    cmd.CommandText = sql.ToString();
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = "@p" + i;
                        p.Value = parameters[i] ?? DBNull.Value;
                        cmd.Parameters.Add(p);
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        table.Load(reader);
                    }
                }
            }
            finally
            {
                if (exportShouldClose)
                {
                    exportConn.Close();
                }
            }

            if (allowedSupplierNames != null && allowedSupplierNames.Count > 0)
            {
                var supplierColumnName = ResolveSupplierNameColumn(table);
                if (!string.IsNullOrEmpty(supplierColumnName))
                {
                    for (int i = table.Rows.Count - 1; i >= 0; i--)
                    {
                        var row = table.Rows[i];
                        var value = row[supplierColumnName];
                        var name = value == null || value == DBNull.Value
                            ? string.Empty
                            : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();

                        if (!allowedSupplierNames.Contains(name))
                        {
                            table.Rows.RemoveAt(i);
                        }
                    }
                }
            }

            int columnCount = table.Columns.Count > 0 ? table.Columns.Count : 1;

            const string companyName = "SSK ENTERPRISE";
            var headerLine = new StringBuilder(reportHeader);
            if (!string.IsNullOrEmpty(displayFrom) || !string.IsNullOrEmpty(displayTo))
            {
                headerLine.Append(" - ");
                if (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                {
                    headerLine.Append(displayFrom).Append(" TO ").Append(displayTo);
                }
                else if (!string.IsNullOrEmpty(displayFrom))
                {
                    headerLine.Append("FROM ").Append(displayFrom);
                }
                else if (!string.IsNullOrEmpty(displayTo))
                {
                    headerLine.Append("UP TO ").Append(displayTo);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("<table border='1'>");
            sb.AppendLine("<tr><th colspan='" + columnCount + "' style='text-align:center;'>" + HttpUtility.HtmlEncode(companyName) + "</th></tr>");
            sb.AppendLine("<tr><th colspan='" + columnCount + "' style='text-align:center;'>" + HttpUtility.HtmlEncode(headerLine.ToString()) + "</th></tr>");

            if (table.Columns.Count > 0)
            {
                sb.AppendLine("<tr>");
                foreach (DataColumn col in table.Columns)
                {
                    sb.AppendFormat("<th>{0}</th>", HttpUtility.HtmlEncode(col.ColumnName));
                }
                sb.AppendLine("</tr>");
            }

            foreach (DataRow row in table.Rows)
            {
                sb.AppendLine("<tr>");
                foreach (DataColumn col in table.Columns)
                {
                    var value = row[col];
                    string text;
                    var colName = col.ColumnName ?? string.Empty;
                    var normalizedCol = new string(colName.Where(ch => !char.IsWhiteSpace(ch) && ch != '_').ToArray())
                        .ToLowerInvariant();
                    var forceText =
                        normalizedCol.Contains("number") ||
                        (normalizedCol.EndsWith("no") && normalizedCol.Length <= 10) ||
                        normalizedCol.Contains("ref") ||
                        normalizedCol.Contains("code");

                    if (value == null || value == DBNull.Value)
                    {
                        text = string.Empty;
                    }
                    else if (value is DateTime dt)
                    {
                        text = dt.ToString("dd-MM-yyyy");
                    }
                    else if (value is decimal || value is double || value is float)
                    {
                        text = string.Format(CultureInfo.InvariantCulture, "{0:0.###}", value);
                    }
                    else
                    {
                        text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    }

                    var safeText = HttpUtility.HtmlEncode(text);
                    if (forceText)
                    {
                        sb.AppendFormat("<td style=\"mso-number-format:'\\@';\">{0}</td>", safeText);
                    }
                    else
                    {
                        sb.AppendFormat("<td>{0}</td>", safeText);
                    }
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var rangePart = (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                ? string.Format("{0}_to_{1}", displayFrom, displayTo)
                : DateTime.Now.ToString("yyyyMMddHHmmss");

            var fileName = string.Format("{0}_{1}.xls", fileNamePrefix, rangePart);

            return File(bytes, "application/vnd.ms-excel", fileName);
        }
    }
}
