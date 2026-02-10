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
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesReportItemwiseController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        [Authorize(Roles = "SalesReportItemwise")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "SalesReportItemwise")]
        public JsonResult GetMaterials()
        {
            try
            {
                var list = new List<string>();
                using (var conn = CreateOpenConnection())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT DISTINCT MTRLDESC FROM MATERIALMASTER WHERE (DISPSTATUS = 0 OR DISPSTATUS IS NULL) AND MTRLDESC IS NOT NULL AND LTRIM(RTRIM(MTRLDESC)) <> '' ORDER BY MTRLDESC";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if (reader[0] != DBNull.Value)
                                {
                                    list.Add(Convert.ToString(reader[0], CultureInfo.InvariantCulture));
                                }
                            }
                        }
                    }
                }

                return new JsonResult
                {
                    Data = new { materials = list },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                    MaxJsonLength = int.MaxValue,
                    RecursionLimit = 100
                };
            }
            catch (Exception ex)
            {
                return Json(new { materials = new string[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Authorize(Roles = "SalesReportItemwise")]
        public JsonResult GetData(string fromDate = null, string toDate = null, string[] materials = null)
        {
            try
            {
                var table = LoadItemwiseSalesData(fromDate, toDate, materials);

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
        [Authorize(Roles = "SalesReportItemwise")]
        public FileResult ExportToExcel(string fromDate = null, string toDate = null, string[] materials = null)
        {
            var table = LoadItemwiseSalesData(fromDate, toDate, materials);

            DateTime? parsedFrom = TryParseIsoDate(fromDate);
            DateTime? parsedTo = TryParseIsoDate(toDate);

            string displayFrom = parsedFrom.HasValue ? parsedFrom.Value.ToString("dd-MM-yyyy") : string.Empty;
            string displayTo = parsedTo.HasValue ? parsedTo.Value.ToString("dd-MM-yyyy") : string.Empty;

            int columnCount = table.Columns.Count > 0 ? table.Columns.Count : 1;

            var sb = new StringBuilder();
            sb.AppendLine("<table border='1'>");
            sb.AppendLine("<tr><th colspan='" + columnCount + "' style='text-align:center;'>SSK ENTERPRISE</th></tr>");

            var header = new StringBuilder("SALES REPORT ITEMWISE");
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

            var fileName = string.Format("Sales Report Itemwise_{0}.xls", namePart);
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

        private DataTable LoadItemwiseSalesData(string fromDate, string toDate, string[] materials)
        {
            DateTime? parsedFrom = TryParseIsoDate(fromDate);
            DateTime? parsedTo = TryParseIsoDate(toDate);

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM VW_ItemWiseSales_RPT WHERE 1 = 1 ");

            // Date filtering: VW_ItemWiseSales_RPT may not have a consistent date column name.
            // Discover an existing column name at runtime and apply filtering only when available.
            string dateColumnName = GetItemwiseDateColumnName();
            string invoiceDateExpr = null;
            if (!string.IsNullOrWhiteSpace(dateColumnName))
            {
                var col = "[" + dateColumnName.Replace("]", "]]" ) + "]";
                invoiceDateExpr = "COALESCE(" +
                                  "TRY_CONVERT(date, " + col + ")," +
                                  "TRY_CONVERT(date, " + col + ", 23)," +
                                  "TRY_CONVERT(date, " + col + ", 120)," +
                                  "TRY_CONVERT(date, " + col + ", 103)" +
                                  ")";
            }

            var parameters = new List<object>();
            int paramIndex = 0;

            if (parsedFrom.HasValue && !string.IsNullOrEmpty(invoiceDateExpr))
            {
                sql.Append("AND " + invoiceDateExpr + " >= @p" + paramIndex + " ");
                parameters.Add(parsedFrom.Value);
                paramIndex++;
            }

            if (parsedTo.HasValue && !string.IsNullOrEmpty(invoiceDateExpr))
            {
                sql.Append("AND " + invoiceDateExpr + " < @p" + paramIndex + " ");
                parameters.Add(parsedTo.Value.AddDays(1));
                paramIndex++;
            }

            if (materials != null)
            {
                var cleaned = materials
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (cleaned.Count > 0)
                {
                    var inParams = new List<string>();
                    for (int i = 0; i < cleaned.Count; i++)
                    {
                        var pName = "@p" + paramIndex;
                        inParams.Add(pName);
                        parameters.Add(cleaned[i]);
                        paramIndex++;
                    }

                    sql.Append("AND MTRLDESC IN (" + string.Join(",", inParams) + ") ");
                }
            }

            var table = new DataTable();
            using (var conn = CreateOpenConnection())
            {
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
            }

            return table;
        }

        private string GetItemwiseDateColumnName()
        {
            // Prefer common date column names. Must match the view's actual column name.
            var preferred = new[]
            {
                "InvoiceDate",
                "invoicedate",
                "INV_DATE",
                "INVDATE",
                "BillDate",
                "BILLDATE",
                "DocDate",
                "DOCDATE",
                "TransDate",
                "TRANSDATE",
                "OrderDate",
                "ORDERDATE",
                "Date",
                "DATE"
            };

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var conn = CreateOpenConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'VW_ItemWiseSales_RPT'";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader[0] != DBNull.Value)
                            {
                                existing.Add(Convert.ToString(reader[0], CultureInfo.InvariantCulture));
                            }
                        }
                    }
                }
            }

            foreach (var name in preferred)
            {
                if (existing.Contains(name))
                {
                    return existing.First(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                }
            }

            // Fallback: try any column that contains 'date'
            var fallback = existing.FirstOrDefault(x => x != null && x.IndexOf("date", StringComparison.OrdinalIgnoreCase) >= 0);
            return fallback;
        }

        private static SqlConnection CreateOpenConnection()
        {
            var cs = ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(cs))
            {
                throw new InvalidOperationException("SSK_DefaultConnection connection string is missing or empty.");
            }

            var conn = new SqlConnection(cs);
            conn.Open();
            return conn;
        }
    }
}
