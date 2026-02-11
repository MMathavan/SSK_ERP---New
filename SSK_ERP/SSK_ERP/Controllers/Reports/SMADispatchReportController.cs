using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SMADispatchReportController : Controller
    {
        [Authorize(Roles = "SMADispatchReport")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "SMADispatchReport")]
        public JsonResult GetData(string fromDate = null, string toDate = null)
        {
            try
            {
                var table = LoadSmaDispatchData(fromDate, toDate);

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
        [Authorize(Roles = "SMADispatchReport")]
        public FileResult ExportToExcel(string fromDate = null, string toDate = null)
        {
            var table = LoadSmaDispatchData(fromDate, toDate);

            DateTime? parsedFrom = TryParseIsoDate(fromDate);
            DateTime? parsedTo = TryParseIsoDate(toDate);

            string displayFrom = parsedFrom.HasValue ? parsedFrom.Value.ToString("dd-MM-yyyy") : string.Empty;
            string displayTo = parsedTo.HasValue ? parsedTo.Value.ToString("dd-MM-yyyy") : string.Empty;

            int columnCount = table.Columns.Count > 0 ? table.Columns.Count : 1;

            var sb = new StringBuilder();
            sb.AppendLine("<table border='1'>");
            sb.AppendLine("<tr><th colspan='" + columnCount + "' style='text-align:center;'>SSK ENTERPRISE</th></tr>");

            var header = new StringBuilder("SMA DISPATCH REPORT");
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

            var namePart = (!string.IsNullOrEmpty(displayFrom) && !string.IsNullOrEmpty(displayTo))
                ? string.Format("{0}_to_{1}", displayFrom, displayTo)
                : DateTime.Now.ToString("yyyyMMddHHmmss");

            var fileName = string.Format("SMA Dispatch Report_{0}.xls", namePart);
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

        private DataTable LoadSmaDispatchData(string fromDate, string toDate)
        {
            DateTime? parsedFrom = TryParseIsoDate(fromDate);
            DateTime? parsedTo = TryParseIsoDate(toDate);

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM VW_SMAsales_report WHERE 1 = 1 ");

            string dateColumnName = GetSmaDateColumnName();
            string dateExpr = null;
            if (!string.IsNullOrWhiteSpace(dateColumnName))
            {
                var col = "[" + dateColumnName.Replace("]", "]]" ) + "]";
                dateExpr = "COALESCE(" +
                           "TRY_CONVERT(date, " + col + ")," +
                           "TRY_CONVERT(date, " + col + ", 23)," +
                           "TRY_CONVERT(date, " + col + ", 120)," +
                           "TRY_CONVERT(date, " + col + ", 103)" +
                           ")";
            }

            var parameters = new List<SqlParameter>();
            int paramIndex = 0;

            if (parsedFrom.HasValue && !string.IsNullOrEmpty(dateExpr))
            {
                sql.Append("AND " + dateExpr + " >= @p" + paramIndex + " ");
                parameters.Add(new SqlParameter("@p" + paramIndex, SqlDbType.Date) { Value = parsedFrom.Value });
                paramIndex++;
            }

            if (parsedTo.HasValue && !string.IsNullOrEmpty(dateExpr))
            {
                sql.Append("AND " + dateExpr + " < @p" + paramIndex + " ");
                parameters.Add(new SqlParameter("@p" + paramIndex, SqlDbType.Date) { Value = parsedTo.Value.AddDays(1) });
                paramIndex++;
            }

            var table = new DataTable();
            using (var conn = CreateOpenConnection())
            using (var cmd = new SqlCommand(sql.ToString(), conn))
            {
                if (parameters.Count > 0)
                {
                    cmd.Parameters.AddRange(parameters.ToArray());
                }

                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(table);
                }
            }

            return table;
        }

        private string GetSmaDateColumnName()
        {
            // Prefer explicit known names, fallback to first column containing 'date'.
            var preferred = new[] { "invoicedate", "invdate", "billdate", "dispatchdate", "despatchdate", "docdate", "date" };

            var cols = new List<string>();
            using (var conn = CreateOpenConnection())
            using (var cmd = new SqlCommand("SELECT TOP 0 * FROM VW_SMAsales_report", conn))
            using (var reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly))
            {
                var schema = reader.GetSchemaTable();
                if (schema != null)
                {
                    foreach (DataRow row in schema.Rows)
                    {
                        var name = Convert.ToString(row["ColumnName"], CultureInfo.InvariantCulture);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            cols.Add(name);
                        }
                    }
                }
            }

            foreach (var p in preferred)
            {
                var hit = cols.FirstOrDefault(c => string.Equals(c, p, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(hit))
                {
                    return hit;
                }
            }

            var fallback = cols.FirstOrDefault(c => c.IndexOf("date", StringComparison.OrdinalIgnoreCase) >= 0);
            return fallback;
        }

        private static SqlConnection CreateOpenConnection()
        {
            var connStr = ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(connStr))
            {
                throw new InvalidOperationException("Connection string 'SSK_DefaultConnection' is missing.");
            }

            var conn = new SqlConnection(connStr);
            conn.Open();
            return conn;
        }
    }       
}
