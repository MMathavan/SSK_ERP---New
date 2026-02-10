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
    public class ServiceLevelReportController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        [Authorize(Roles = "ServiceLevelReport")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "ServiceLevelReport")]
        public JsonResult GetData(string fromDate = null, string toDate = null, string debug = null)
        {
            try
            {
                var table = LoadServiceLevelData(fromDate, toDate);

                var columns = table.Columns.Cast<DataColumn>()
                    .Select(c => c.ColumnName)
                    .ToList();

                var rows = new List<Dictionary<string, object>>();
                foreach (DataRow dr in table.Rows)
                {
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataColumn col in table.Columns)
                    {
                        var val = dr[col];
                        dict[col.ColumnName] = val == DBNull.Value ? null : val;
                    }
                    rows.Add(dict);
                }

                if (string.Equals(debug, "1", StringComparison.OrdinalIgnoreCase))
                {
                    return new JsonResult
                    {
                        Data = new
                        {
                            columns,
                            data = rows,
                            rowCount = rows.Count,
                            debug = new
                            {
                                fromDate,
                                toDate,
                                parsedFrom = TryParseIsoDate(fromDate),
                                parsedTo = TryParseIsoDate(toDate),
                                totalRowCount = GetTotalRowCount()
                            }
                        },
                        JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                        MaxJsonLength = int.MaxValue,
                        RecursionLimit = 100
                    };
                }

                return new JsonResult
                {
                    Data = new { columns, data = rows, rowCount = rows.Count },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                    MaxJsonLength = int.MaxValue,
                    RecursionLimit = 100
                };
            }
            catch (Exception ex)
            {
                return Json(new { columns = new string[0], data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "ServiceLevelReport")]
        public FileResult ExportToExcel(string fromDate = null, string toDate = null)
        {
            var table = LoadServiceLevelData(fromDate, toDate);

            DateTime? parsedFrom = TryParseIsoDate(fromDate);
            DateTime? parsedTo = TryParseIsoDate(toDate);

            string displayFrom = parsedFrom.HasValue ? parsedFrom.Value.ToString("dd-MM-yyyy") : string.Empty;
            string displayTo = parsedTo.HasValue ? parsedTo.Value.ToString("dd-MM-yyyy") : string.Empty;

            string[] extraColumns =
            {
                "Dispatch Date",
                "Despatch Mode",
                "Delivery Date",
                "Reason for Delay in Invoicing",
                "Reason in Delay in Despatch",
                "Reason in Delay in Delivery"
            };

            int baseColumnCount = table.Columns.Count > 0 ? table.Columns.Count : 1;
            int columnCount = baseColumnCount + extraColumns.Length;

            var sb = new StringBuilder();
            sb.AppendLine("<table border='1'>");
            sb.AppendLine("<tr><th colspan='" + columnCount + "' style='text-align:center;'>SSK ENTERPRISE</th></tr>");

            var header = new StringBuilder("SERVICE LEVEL REPORT");
            if (!string.IsNullOrEmpty(displayFrom) || !string.IsNullOrEmpty(displayTo))
            {
                header.Append(" - ");
                if (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                {
                    header.Append(displayFrom).Append(" TO ").Append(displayTo);
                }
                else if (!string.IsNullOrEmpty(displayFrom))
                {
                    header.Append("FROM ").Append(displayFrom);
                }
                else
                {
                    header.Append("UP TO ").Append(displayTo);
                }
            }
            sb.AppendLine("<tr><th colspan='" + columnCount + "' style='text-align:center;'>" + HttpUtility.HtmlEncode(header.ToString()) + "</th></tr>");

            if (table.Columns.Count > 0)
            {
                sb.AppendLine("<tr>");
                foreach (DataColumn col in table.Columns)
                {
                    sb.AppendFormat("<th>{0}</th>", HttpUtility.HtmlEncode(col.ColumnName));
                }

                foreach (var colName in extraColumns)
                {
                    sb.AppendFormat("<th>{0}</th>", HttpUtility.HtmlEncode(colName));
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

                for (int i = 0; i < extraColumns.Length; i++)
                {
                    sb.Append("<td></td>");
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            var namePart = (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                ? string.Format("{0}_to_{1}", displayFrom, displayTo)
                : DateTime.Now.ToString("yyyyMMddHHmmss");

            var fileName = string.Format("Service Level Report_{0}.xls", namePart);
            return File(bytes, "application/vnd.ms-excel", fileName);
        }

        private static DateTime? TryParseIsoDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt.Date;
            }

            return null;
        }

        private DataTable LoadServiceLevelData(string fromDate, string toDate)
        {
            DateTime? parsedFrom = TryParseIsoDate(fromDate);
            DateTime? parsedTo = TryParseIsoDate(toDate);

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM VW_SERVICELEVEL_RPT WHERE 1 = 1 ");

            // invoicedate can be stored as date/datetime/text depending on the view definition.
            // Make filtering resilient by trying common formats.
            const string invoiceDateExpr = "COALESCE(" +
                                           "TRY_CONVERT(date, invoicedate)," +         // works for date/datetime and many string formats
                                           "TRY_CONVERT(date, invoicedate, 23)," +     // yyyy-mm-dd
                                           "TRY_CONVERT(date, invoicedate, 120)," +    // yyyy-mm-dd hh:mi:ss
                                           "TRY_CONVERT(date, invoicedate, 103)" +     // dd/mm/yyyy
                                           ")";

            var parameters = new List<object>();
            int paramIndex = 0;

            if (parsedFrom.HasValue)
            {
                sql.Append("AND " + invoiceDateExpr + " >= @p" + paramIndex + " ");
                parameters.Add(parsedFrom.Value);
                paramIndex++;
            }

            if (parsedTo.HasValue)
            {
                sql.Append("AND " + invoiceDateExpr + " < @p" + paramIndex + " ");
                parameters.Add(parsedTo.Value.AddDays(1));
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

            return table;
        }

        private int GetTotalRowCount()
        {
            using (var conn = db.Database.Connection)
            {
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
                        cmd.CommandText = "SELECT COUNT(1) FROM VW_SERVICELEVEL_RPT";
                        var result = cmd.ExecuteScalar();
                        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
                    }
                }
                finally
                {
                    if (shouldClose)
                    {
                        conn.Close();
                    }
                }
            }
        }
    }
}
