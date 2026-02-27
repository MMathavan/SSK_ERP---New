using SSK_ERP.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;

namespace SSK_ERP.Controllers
{
    public class StockViewIndexController : Controller
    {
        private readonly ApplicationDbContext db;

        public StockViewIndexController()
        {
            db = new ApplicationDbContext();
        }

        [Authorize(Roles = "StockViewIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "StockViewIndex")]
        public JsonResult GetStockSummary(string fromDate, string toDate)
        {
            try
            {
                DateTime fromDt;
                DateTime toDt;

                if (!DateTime.TryParseExact(fromDate ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDt))
                {
                    return Json(new { success = false, message = "Invalid From Date" }, JsonRequestBehavior.AllowGet);
                }

                if (!DateTime.TryParseExact(toDate ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out toDt))
                {
                    return Json(new { success = false, message = "Invalid To Date" }, JsonRequestBehavior.AllowGet);
                }

                var results = new List<Dictionary<string, object>>();

                var conn = db.Database.Connection;
                var wasClosed = conn.State == ConnectionState.Closed;
                if (wasClosed)
                {
                    conn.Open();
                }

                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PR_STOCK_SUMMARY";
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@FromDate", fromDt));
                        cmd.Parameters.Add(new SqlParameter("@ToDate", toDt));

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var name = reader.GetName(i);
                                    object value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                    row[name] = value;
                                }
                                results.Add(row);
                            }
                        }
                    }
                }
                finally
                {
                    if (wasClosed && conn.State != ConnectionState.Closed)
                    {
                        conn.Close();
                    }
                }

                return Json(new { success = true, data = results }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
