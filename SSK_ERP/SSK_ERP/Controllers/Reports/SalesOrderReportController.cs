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
    public class SalesOrderReportController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesOrderRegisterId = 1;
        private const int PurchaseRegisterId = 2;

        private static string ResolveCustomerNameColumn(DataTable table)
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

                if (normalized == "customername" ||
                    normalized == "customer" ||
                    normalized == "customerdesc" ||
                    normalized == "customerdescription" ||
                    normalized == "tranrefname" ||
                    normalized == "partyname" ||
                    normalized == "party" ||
                    normalized == "buyername" ||
                    normalized == "buyer")
                {
                    return col.ColumnName;
                }
            }

            return null;
        }

        [Authorize(Roles = "SalesOrderReport")]
        public ActionResult Index()
        {
            var customers = db.CustomerMasters
                .Where(c => c.DISPSTATUS == 0)
                .OrderBy(c => c.CATENAME)
                .ToList();

            ViewBag.CustomerList = customers;

            return View();
        }

        [HttpGet]
        [Authorize(Roles = "SalesOrderReport")]
        public JsonResult GetDateWise(string fromDate = null, string toDate = null)
        {
            try
            {
                var query = db.TransactionMasters.Where(t => t.REGSTRID == SalesOrderRegisterId);

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
                    var exclusiveToDate = parsedToDate.Date.AddDays(1);
                    query = query.Where(t => t.TRANDATE < exclusiveToDate);
                }

                var items = query
                    .GroupBy(t => t.TRANDATE.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        Date = g.Key,
                        OrderCount = g.Count(),
                        TotalAmount = g.Sum(x => x.TRANNAMT)
                    })
                    .ToList();

                return Json(new { data = items }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "SalesOrderReport")]
        public JsonResult GetCustomerWise(string fromDate = null, string toDate = null, string mode = "Consolidated")
        {
            try
            {
                var query = db.TransactionMasters.Where(t => t.REGSTRID == SalesOrderRegisterId);

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
                    var exclusiveToDate = parsedToDate.Date.AddDays(1);
                    query = query.Where(t => t.TRANDATE < exclusiveToDate);
                }

                var masters = query.ToList();

                var linkedTranIds = db.TransactionMasters
                    .Where(po => po.REGSTRID == PurchaseRegisterId && po.TRANLMID > 0)
                    .Select(po => po.TRANLMID)
                    .ToList();

                var poLinkSet = new System.Collections.Generic.HashSet<int>(linkedTranIds.Cast<int>());

                var rows = masters
                    .Select(t => new
                    {
                        t.TRANMID,
                        t.TRANDATE,
                        t.TRANNO,
                        t.TRANREFNO,
                        CustomerId = t.TRANREFID,
                        CustomerName = t.TRANREFNAME,
                        Amount = t.TRANNAMT,
                        HasPo = poLinkSet.Contains(t.TRANMID)
                    })
                    .ToList();

                var normalizedMode = (mode ?? "Consolidated").Trim().ToLowerInvariant();
                bool onlyPending = normalizedMode.Contains("pending");
                bool consolidated = normalizedMode.Contains("consolidated");

                if (onlyPending)
                {
                    rows = rows.Where(r => !r.HasPo).ToList();
                }

                if (consolidated)
                {
                    var grouped = rows
                        .GroupBy(r => new { r.CustomerId, r.CustomerName })
                        .OrderBy(g => g.Key.CustomerName)
                        .Select(g => new
                        {
                            CustomerName = g.Key.CustomerName,
                            Mode = onlyPending ? "Consolidated Pending" : "Consolidated",
                            OrderCount = g.Count(),
                            PendingCount = g.Count(x => !x.HasPo),
                            TotalAmount = g.Sum(x => x.Amount),
                            PendingAmount = g.Where(x => !x.HasPo).Sum(x => x.Amount),
                            LastOrderDate = g.Max(x => x.TRANDATE),
                            OrderDate = (DateTime?)null,
                            OrderNumber = (int?)null,
                            BillNo = string.Empty
                        })
                        .ToList();

                    return Json(new { data = grouped }, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    var detailed = rows
                        .OrderBy(r => r.CustomerName)
                        .ThenBy(r => r.TRANDATE)
                        .Select(r => new
                        {
                            CustomerName = r.CustomerName,
                            Mode = onlyPending ? "Pending" : "Detailed",
                            OrderCount = 1,
                            PendingCount = r.HasPo ? 0 : 1,
                            TotalAmount = r.Amount,
                            PendingAmount = r.HasPo ? 0 : r.Amount,
                            LastOrderDate = r.TRANDATE,
                            OrderDate = r.TRANDATE,
                            OrderNumber = r.TRANNO,
                            BillNo = r.TRANREFNO
                        })
                        .ToList();

                    return Json(new { data = detailed }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "SalesOrderReport")]
        public FileResult ExportDatewiseConsolidated(string fromDate = null, string toDate = null)
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

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM VW_SALESORDER_DATEWISE_CONSOLIDATED_RPT WHERE 1 = 1 ");

            var parameters = new List<object>();
            int paramIndex = 0;

            if (parsedFrom.HasValue)
            {
                sql.Append("AND [SALES ORDER DATE] >= @p" + paramIndex + " ");
                parameters.Add(parsedFrom.Value);
                paramIndex++;
            }

            if (parsedToExclusive.HasValue)
            {
                sql.Append("AND [SALES ORDER DATE] < @p" + paramIndex + " ");
                parameters.Add(parsedToExclusive.Value);
                paramIndex++;
            }

            var table = new DataTable();
            using (var conn = db.Database.Connection)
            {
                bool shouldClose = false;
                if (conn.State != ConnectionState.Open)
                {
                    conn.Open();
                    shouldClose = true;
                }

                using (var cmd = conn.CreateCommand())
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

                if (shouldClose)
                {
                    conn.Close();
                }
            }

            int columnCount = table.Columns.Count > 0 ? table.Columns.Count : 1;

            const string companyName = "SSK ENTERPRISE";
            var headerLine = new StringBuilder("SALES ORDER DATEWISE - CONSOLIDATED");
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

                    sb.AppendFormat("<td>{0}</td>", HttpUtility.HtmlEncode(text));
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var rangePart = (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                ? string.Format("{0}_to_{1}", displayFrom, displayTo)
                : DateTime.Now.ToString("yyyyMMddHHmmss");

            var fileName = string.Format("SSK_ENTERPRISE_SALES_ORDER_DATEWISE_CONSOLIDATED_{0}.xls", rangePart);

            return File(bytes, "application/vnd.ms-excel", fileName);
        }

        [HttpGet]
        [Authorize(Roles = "SalesOrderReport")]
        public FileResult ExportDatewiseDetailed(string fromDate = null, string toDate = null)
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

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM vw_salesorder_detailed_rpt WHERE 1 = 1 ");

            var parameters = new List<object>();
            int paramIndex = 0;

            if (parsedFrom.HasValue)
            {
                sql.Append("AND TRANDATE >= @p" + paramIndex + " ");
                parameters.Add(parsedFrom.Value);
                paramIndex++;
            }

            if (parsedToExclusive.HasValue)
            {
                sql.Append("AND TRANDATE < @p" + paramIndex + " ");
                parameters.Add(parsedToExclusive.Value);
                paramIndex++;
            }

            var table = new DataTable();
            using (var conn = db.Database.Connection)
            {
                bool shouldClose = false;
                if (conn.State != ConnectionState.Open)
                {
                    conn.Open();
                    shouldClose = true;
                }

                using (var cmd = conn.CreateCommand())
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

                if (shouldClose)
                {
                    conn.Close();
                }
            }

            int columnCount = table.Columns.Count > 0 ? table.Columns.Count : 1;

            const string companyName = "SSK ENTERPRISE";
            var headerLine = new StringBuilder("SALES ORDER DATEWISE - DETAILED");
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

                    sb.AppendFormat("<td>{0}</td>", HttpUtility.HtmlEncode(text));
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var rangePart = (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                ? string.Format("{0}_to_{1}", displayFrom, displayTo)
                : DateTime.Now.ToString("yyyyMMddHHmmss");

            var fileName = string.Format("SSK_ENTERPRISE_SALES_ORDER_DATEWISE_DETAILED_{0}.xls", rangePart);

            return File(bytes, "application/vnd.ms-excel", fileName);
        }

        [HttpGet]
        [Authorize(Roles = "SalesOrderReport")]
        public FileResult ExportCustomerwiseConsolidated(string fromDate = null, string toDate = null, string customerIds = null)
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

            var selectedCustomerIds = new HashSet<int>();
            if (!string.IsNullOrWhiteSpace(customerIds))
            {
                var parts = customerIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    int id;
                    if (int.TryParse(part, out id))
                    {
                        selectedCustomerIds.Add(id);
                    }
                }
            }

            HashSet<string> allowedCustomerNames = null;
            if (selectedCustomerIds.Count > 0)
            {
                var names = db.CustomerMasters
                    .Where(c => selectedCustomerIds.Contains(c.CATEID))
                    .Select(c => new { c.CATENAME, c.CATEDNAME })
                    .ToList();

                if (names.Count > 0)
                {
                    allowedCustomerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var n in names)
                    {
                        if (!string.IsNullOrWhiteSpace(n.CATENAME))
                        {
                            allowedCustomerNames.Add(n.CATENAME.Trim());
                        }
                        if (!string.IsNullOrWhiteSpace(n.CATEDNAME))
                        {
                            allowedCustomerNames.Add(n.CATEDNAME.Trim());
                        }
                    }
                }
            }

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM VW_SALESORDER_DATEWISE_CONSOLIDATED_RPT WHERE 1 = 1 ");

            var parameters = new List<object>();
            int paramIndex = 0;

            if (parsedFrom.HasValue)
            {
                sql.Append("AND [SALES ORDER DATE] >= @p" + paramIndex + " ");
                parameters.Add(parsedFrom.Value);
                paramIndex++;
            }

            if (parsedToExclusive.HasValue)
            {
                sql.Append("AND [SALES ORDER DATE] < @p" + paramIndex + " ");
                parameters.Add(parsedToExclusive.Value);
                paramIndex++;
            }

            var table = new DataTable();
            using (var conn = db.Database.Connection)
            {
                bool shouldClose = false;
                if (conn.State != ConnectionState.Open)
                {
                    conn.Open();
                    shouldClose = true;
                }

                using (var cmd = conn.CreateCommand())
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

                if (shouldClose)
                {
                    conn.Close();
                }
            }

            if (allowedCustomerNames != null && allowedCustomerNames.Count > 0)
            {
                var customerColumnName = ResolveCustomerNameColumn(table);
                if (!string.IsNullOrEmpty(customerColumnName))
                {
                    for (int i = table.Rows.Count - 1; i >= 0; i--)
                    {
                        var row = table.Rows[i];
                        var value = row[customerColumnName];
                        var name = value == null || value == DBNull.Value
                            ? string.Empty
                            : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();

                        if (!allowedCustomerNames.Contains(name))
                        {
                            table.Rows.RemoveAt(i);
                        }
                    }
                }
            }

            int columnCount = table.Columns.Count > 0 ? table.Columns.Count : 1;

            const string companyName = "SSK ENTERPRISE";
            var headerLine = new StringBuilder("SALES ORDER CUSTOMER WISE - CONSOLIDATED");
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

                    sb.AppendFormat("<td>{0}</td>", HttpUtility.HtmlEncode(text));
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var rangePart = (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                ? string.Format("{0}_to_{1}", displayFrom, displayTo)
                : DateTime.Now.ToString("yyyyMMddHHmmss");

            var fileName = string.Format("SSK_ENTERPRISE_SALES_ORDER_CUSTOMER_WISE_CONSOLIDATED_{0}.xls", rangePart);

            return File(bytes, "application/vnd.ms-excel", fileName);
        }

        [HttpGet]
        [Authorize(Roles = "SalesOrderReport")]
        public FileResult ExportCustomerwiseDetailed(string fromDate = null, string toDate = null, string customerIds = null)
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

            var selectedCustomerIds = new HashSet<int>();
            if (!string.IsNullOrWhiteSpace(customerIds))
            {
                var parts = customerIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    int id;
                    if (int.TryParse(part, out id))
                    {
                        selectedCustomerIds.Add(id);
                    }
                }
            }

            HashSet<string> allowedCustomerNames = null;
            if (selectedCustomerIds.Count > 0)
            {
                var names = db.CustomerMasters
                    .Where(c => selectedCustomerIds.Contains(c.CATEID))
                    .Select(c => new { c.CATENAME, c.CATEDNAME })
                    .ToList();

                if (names.Count > 0)
                {
                    allowedCustomerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var n in names)
                    {
                        if (!string.IsNullOrWhiteSpace(n.CATENAME))
                        {
                            allowedCustomerNames.Add(n.CATENAME.Trim());
                        }
                        if (!string.IsNullOrWhiteSpace(n.CATEDNAME))
                        {
                            allowedCustomerNames.Add(n.CATEDNAME.Trim());
                        }
                    }

                    if (allowedCustomerNames.Count == 0)
                    {
                        allowedCustomerNames = null;
                    }
                }
            }

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM vw_salesorder_detailed_rpt WHERE 1 = 1 ");

            var parameters = new List<object>();
            int paramIndex = 0;

            if (parsedFrom.HasValue)
            {
                sql.Append("AND TRANDATE >= @p" + paramIndex + " ");
                parameters.Add(parsedFrom.Value);
                paramIndex++;
            }

            if (parsedToExclusive.HasValue)
            {
                sql.Append("AND TRANDATE < @p" + paramIndex + " ");
                parameters.Add(parsedToExclusive.Value);
                paramIndex++;
            }

            var table = new DataTable();
            using (var conn = db.Database.Connection)
            {
                bool shouldClose = false;
                if (conn.State != ConnectionState.Open)
                {
                    conn.Open();
                    shouldClose = true;
                }

                using (var cmd = conn.CreateCommand())
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

                if (shouldClose)
                {
                    conn.Close();
                }
            }

            if (allowedCustomerNames != null && allowedCustomerNames.Count > 0)
            {
                var customerColumnName = ResolveCustomerNameColumn(table);
                if (!string.IsNullOrEmpty(customerColumnName))
                {
                    for (int i = table.Rows.Count - 1; i >= 0; i--)
                    {
                        var row = table.Rows[i];
                        var value = row[customerColumnName];
                        var name = value == null || value == DBNull.Value
                            ? string.Empty
                            : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();

                        if (!allowedCustomerNames.Contains(name))
                        {
                            table.Rows.RemoveAt(i);
                        }
                    }
                }
            }

            int columnCount = table.Columns.Count > 0 ? table.Columns.Count : 1;

            const string companyName = "SSK ENTERPRISE";
            var headerLine = new StringBuilder("SALES ORDER CUSTOMER WISE - DETAILED");
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

                    sb.AppendFormat("<td>{0}</td>", HttpUtility.HtmlEncode(text));
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var rangePart = (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                ? string.Format("{0}_to_{1}", displayFrom, displayTo)
                : DateTime.Now.ToString("yyyyMMddHHmmss");

            var fileName = string.Format("SSK_ENTERPRISE_SALES_ORDER_CUSTOMER_WISE_DETAILED_{0}.xls", rangePart);

            return File(bytes, "application/vnd.ms-excel", fileName);
        }
    }
}
