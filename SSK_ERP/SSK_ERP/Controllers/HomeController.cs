using SSK_ERP.Data;
using SSK_ERP.Filters;
using SSK_ERP.Models;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using log4net;
using Microsoft.AspNet.Identity;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace SSK_ERP.Controllers
{

    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        //private static readonly ILog log = LogManager.GetLogger(typeof(MembersController));

        public HomeController()
        {
            _db = new ApplicationDbContext();
        }


        public ActionResult AdminDashboard()
        {
            // Dashboard accessible to all users (Admin and regular users)
            try
            {
                var statsDict = new Dictionary<string, DashboardStat>();

                System.Diagnostics.Debug.WriteLine("=== Dashboard Data Loading Started ===");


                ViewBag.DashboardStats = statsDict;

                System.Diagnostics.Debug.WriteLine("=== Dashboard Data Loading Completed Successfully ===");

                System.Diagnostics.Debug.WriteLine($"Dashboard stats loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR loading dashboard stats: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }

                ViewBag.DashboardStats = new Dictionary<string, DashboardStat>();
                ViewBag.ShrimpByType = new List<ShrimpByTypeDTO>();
                ViewBag.MonthlyInvoices = new List<MonthlyInvoiceDTO>();
                ViewBag.TopShrimpTypes = new List<TopShrimpTypeDTO>();
                ViewBag.ErrorMessage = ex.Message;
            }

            return View();
        }

        public ActionResult Index()
        {
            // Show the same dashboard for all users (Admin or regular users)
            try
            {
                // Fetch essential business statistics from database
                int customersCount = 0, suppliersCount = 0, materialsCount = 0;

                int salesOrderCount = 0;
                int pendingSalesOrderCount = 0;
                int purchaseOrderCount = 0;

                int purchaseInvoiceCount = 0;
                int pendingPurchaseOrderCount = 0;
                int pendingPurchaseInvoiceCount = 0;

                int partialPurchaseInvoiceCount = 0;
                int salesInvoiceCount = 0;
                int pendingSalesInvoiceCount = 0;

                var pendingPurchaseOrderDetails = new List<PendingDocRow>();
                var pendingSalesOrderDetails = new List<PendingDocRow>();
                var pendingPurchaseInvoiceDetails = new List<PendingDocRow>();
                var pendingSalesInvoiceDetails = new List<PendingDocRow>();
                var partialPurchaseInvoiceDetails = new List<PendingDocRow>();

                try { customersCount = _db.CustomerMasters.Count(c => c.DISPSTATUS == 0 || c.DISPSTATUS == null); } catch { }
                try { suppliersCount = _db.SupplierMasters.Count(s => s.DISPSTATUS == 0 || s.DISPSTATUS == null); } catch { }
                try { materialsCount = _db.MaterialMasters.Count(m => m.DISPSTATUS == 0 || m.DISPSTATUS == null); } catch { }


                // New dashboard counts (based on register IDs used by controllers)
                // Sales Order = 1, Purchase Order = 2, Purchase Invoice = 18, Sales Invoice = 20
                try { salesOrderCount = _db.TransactionMasters.Count(t => t.REGSTRID == 1 && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)); } catch { }
                try { purchaseOrderCount = _db.TransactionMasters.Count(t => t.REGSTRID == 2 && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)); } catch { }
                try { purchaseInvoiceCount = _db.TransactionMasters.Count(t => t.REGSTRID == 18 && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)); } catch { }
                try { salesInvoiceCount = _db.TransactionMasters.Count(t => t.REGSTRID == 20 && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)); } catch { }

                // Pending Sales Order: Sales order not yet converted to a Purchase Order
                try
                {
                    var sql = @"SELECT COUNT(*)
                                FROM TRANSACTIONMASTER so
                                WHERE so.REGSTRID = 1
                                  AND (so.DISPSTATUS = 0 OR so.DISPSTATUS IS NULL)
                                  AND NOT EXISTS (
                                      SELECT 1
                                      FROM TRANSACTIONMASTER po
                                      WHERE po.REGSTRID = 2
                                        AND (po.DISPSTATUS = 0 OR po.DISPSTATUS IS NULL)
                                        AND po.TRANLMID = so.TRANMID
                                  )";
                    pendingSalesOrderCount = _db.Database.SqlQuery<int>(sql).FirstOrDefault();
                }
                catch
                {
                    pendingSalesOrderCount = 0;
                }

                // Pending Purchase Order: PO not yet converted into a Purchase Invoice
                try
                {
                    var sql = @"SELECT COUNT(*)
                                FROM TRANSACTIONMASTER po
                                WHERE po.REGSTRID = 2
                                  AND (po.DISPSTATUS = 0 OR po.DISPSTATUS IS NULL)
                                  AND NOT EXISTS (
                                      SELECT 1
                                      FROM TRANSACTIONMASTER pi
                                      WHERE pi.REGSTRID = 18
                                        AND (pi.DISPSTATUS = 0 OR pi.DISPSTATUS IS NULL)
                                        AND pi.TRANLMID = po.TRANMID
                                  )";
                    pendingPurchaseOrderCount = _db.Database.SqlQuery<int>(sql).FirstOrDefault();
                }
                catch
                {
                    pendingPurchaseOrderCount = 0;
                }

                try
                {
                    var sql = @"SELECT TOP 10
                                    po.TRANDATE AS [Date],
                                    po.TRANNO AS [Number],
                                    po.TRANREFNO AS [DocNo],
                                    po.TRANREFNAME AS [CustomerName],
                                    ISNULL(po.TRANNAMT, 0) AS [Amount]
                               FROM TRANSACTIONMASTER po
                               WHERE po.REGSTRID = 2
                                 AND (po.DISPSTATUS = 0 OR po.DISPSTATUS IS NULL)
                                 AND NOT EXISTS (
                                     SELECT 1
                                     FROM TRANSACTIONMASTER pi
                                     WHERE pi.REGSTRID = 18
                                       AND (pi.DISPSTATUS = 0 OR pi.DISPSTATUS IS NULL)
                                       AND pi.TRANLMID = po.TRANMID
                                 )
                               ORDER BY po.TRANDATE DESC, po.TRANNO DESC";

                    pendingPurchaseOrderDetails = _db.Database.SqlQuery<PendingDocRow>(sql).ToList();
                }
                catch
                {
                    pendingPurchaseOrderDetails = new List<PendingDocRow>();
                }

                try
                {
                    var sql = @"SELECT TOP 10
                                    so.TRANDATE AS [Date],
                                    so.TRANNO AS [Number],
                                    so.TRANREFNO AS [DocNo],
                                    so.TRANREFNAME AS [CustomerName],
                                    ISNULL(so.TRANNAMT, 0) AS [Amount]
                               FROM TRANSACTIONMASTER so
                               WHERE so.REGSTRID = 1
                                 AND (so.DISPSTATUS = 0 OR so.DISPSTATUS IS NULL)
                                 AND NOT EXISTS (
                                     SELECT 1
                                     FROM TRANSACTIONMASTER po
                                     WHERE po.REGSTRID = 2
                                       AND (po.DISPSTATUS = 0 OR po.DISPSTATUS IS NULL)
                                       AND po.TRANLMID = so.TRANMID
                                 )
                               ORDER BY so.TRANDATE DESC, so.TRANNO DESC";

                    pendingSalesOrderDetails = _db.Database.SqlQuery<PendingDocRow>(sql).ToList();
                }
                catch
                {
                    pendingSalesOrderDetails = new List<PendingDocRow>();
                }

                // Pending Purchase Invoice: Purchase invoice not yet converted into a Sales Invoice
                try
                {
                    var sql = @"SELECT COUNT(*)
                                FROM TRANSACTIONMASTER pi
                                WHERE pi.REGSTRID = 18
                                  AND (pi.DISPSTATUS = 0 OR pi.DISPSTATUS IS NULL)
                                  AND NOT EXISTS (
                                      SELECT 1
                                      FROM TRANSACTIONMASTER si
                                      WHERE si.REGSTRID = 20
                                        AND (si.DISPSTATUS = 0 OR si.DISPSTATUS IS NULL)
                                        AND si.TRANLMID = pi.TRANMID
                                  )";
                    pendingPurchaseInvoiceCount = _db.Database.SqlQuery<int>(sql).FirstOrDefault();
                }
                catch
                {
                    pendingPurchaseInvoiceCount = 0;
                }

                try
                {
                    var sql = @"SELECT TOP 10
                                    pi.TRANDATE AS [Date],
                                    pi.TRANNO AS [Number],
                                    pi.TRANREFNO AS [DocNo],
                                    pi.TRANREFNAME AS [CustomerName],
                                    ISNULL(pi.TRANNAMT, 0) AS [Amount]
                               FROM TRANSACTIONMASTER pi
                               WHERE pi.REGSTRID = 18
                                 AND (pi.DISPSTATUS = 0 OR pi.DISPSTATUS IS NULL)
                                 AND NOT EXISTS (
                                     SELECT 1
                                     FROM TRANSACTIONMASTER si
                                     WHERE si.REGSTRID = 20
                                       AND (si.DISPSTATUS = 0 OR si.DISPSTATUS IS NULL)
                                       AND si.TRANLMID = pi.TRANMID
                                 )
                               ORDER BY pi.TRANDATE DESC, pi.TRANNO DESC";

                    pendingPurchaseInvoiceDetails = _db.Database.SqlQuery<PendingDocRow>(sql).ToList();
                }
                catch
                {
                    pendingPurchaseInvoiceDetails = new List<PendingDocRow>();
                }

                // Pending Sales Invoice: Sales order which does not yet have a Sales Invoice through SO->PO->PI->SI link
                try
                {
                    var sql = @"SELECT COUNT(*)
                                FROM TRANSACTIONMASTER so
                                WHERE so.REGSTRID = 1
                                  AND (so.DISPSTATUS = 0 OR so.DISPSTATUS IS NULL)
                                  AND NOT EXISTS (
                                      SELECT 1
                                      FROM TRANSACTIONMASTER po
                                      INNER JOIN TRANSACTIONMASTER pi ON pi.REGSTRID = 18 AND pi.TRANLMID = po.TRANMID AND (pi.DISPSTATUS = 0 OR pi.DISPSTATUS IS NULL)
                                      INNER JOIN TRANSACTIONMASTER si ON si.REGSTRID = 20 AND si.TRANLMID = pi.TRANMID AND (si.DISPSTATUS = 0 OR si.DISPSTATUS IS NULL)
                                      WHERE po.REGSTRID = 2
                                        AND (po.DISPSTATUS = 0 OR po.DISPSTATUS IS NULL)
                                        AND po.TRANLMID = so.TRANMID
                                  )";
                    pendingSalesInvoiceCount = _db.Database.SqlQuery<int>(sql).FirstOrDefault();
                }
                catch
                {
                    pendingSalesInvoiceCount = 0;
                }

                try
                {
                    var sql = @"SELECT TOP 10
                                    so.TRANDATE AS [Date],
                                    so.TRANNO AS [Number],
                                    so.TRANREFNO AS [DocNo],
                                    so.TRANREFNAME AS [CustomerName],
                                    ISNULL(so.TRANNAMT, 0) AS [Amount]
                               FROM TRANSACTIONMASTER so
                               WHERE so.REGSTRID = 1
                                 AND (so.DISPSTATUS = 0 OR so.DISPSTATUS IS NULL)
                                 AND NOT EXISTS (
                                     SELECT 1
                                     FROM TRANSACTIONMASTER po
                                     INNER JOIN TRANSACTIONMASTER pi ON pi.REGSTRID = 18 AND pi.TRANLMID = po.TRANMID AND (pi.DISPSTATUS = 0 OR pi.DISPSTATUS IS NULL)
                                     INNER JOIN TRANSACTIONMASTER si ON si.REGSTRID = 20 AND si.TRANLMID = pi.TRANMID AND (si.DISPSTATUS = 0 OR si.DISPSTATUS IS NULL)
                                     WHERE po.REGSTRID = 2
                                       AND (po.DISPSTATUS = 0 OR po.DISPSTATUS IS NULL)
                                       AND po.TRANLMID = so.TRANMID
                                 )
                               ORDER BY so.TRANDATE DESC, so.TRANNO DESC";

                    pendingSalesInvoiceDetails = _db.Database.SqlQuery<PendingDocRow>(sql).ToList();
                }
                catch
                {
                    pendingSalesInvoiceDetails = new List<PendingDocRow>();
                }

                // Partial Purchase Invoice: Purchase invoices created against a PO but total invoice qty < total PO qty
                try
                {
                    var sql = @"SELECT COUNT(DISTINCT inv.TRANMID)
                                FROM TRANSACTIONMASTER inv
                                INNER JOIN TRANSACTIONMASTER po ON po.TRANMID = inv.TRANLMID AND po.REGSTRID = 2
                                INNER JOIN (
                                    SELECT TRANMID, SUM(ISNULL(TRANDQTY, 0)) AS QTY
                                    FROM TRANSACTIONDETAIL
                                    GROUP BY TRANMID
                                ) invd ON invd.TRANMID = inv.TRANMID
                                INNER JOIN (
                                    SELECT TRANMID, SUM(ISNULL(TRANDQTY, 0)) AS QTY
                                    FROM TRANSACTIONDETAIL
                                    GROUP BY TRANMID
                                ) pod ON pod.TRANMID = po.TRANMID
                                WHERE inv.REGSTRID = 18
                                  AND inv.TRANLMID IS NOT NULL
                                  AND inv.TRANLMID > 0
                                  AND (inv.DISPSTATUS = 0 OR inv.DISPSTATUS IS NULL)
                                  AND invd.QTY < pod.QTY";
                    partialPurchaseInvoiceCount = _db.Database.SqlQuery<int>(sql).FirstOrDefault();
                }
                catch
                {
                    partialPurchaseInvoiceCount = 0;
                }

                try
                {
                    var sql = @"SELECT TOP 10
                                    inv.TRANDATE AS [Date],
                                    inv.TRANNO AS [Number],
                                    inv.TRANREFNO AS [DocNo],
                                    inv.TRANREFNAME AS [CustomerName],
                                    ISNULL(inv.TRANNAMT, 0) AS [Amount]
                               FROM TRANSACTIONMASTER inv
                               INNER JOIN TRANSACTIONMASTER po ON po.TRANMID = inv.TRANLMID AND po.REGSTRID = 2
                               INNER JOIN (
                                   SELECT TRANMID, SUM(ISNULL(TRANDQTY, 0)) AS QTY
                                   FROM TRANSACTIONDETAIL
                                   GROUP BY TRANMID
                               ) invd ON invd.TRANMID = inv.TRANMID
                               INNER JOIN (
                                   SELECT TRANMID, SUM(ISNULL(TRANDQTY, 0)) AS QTY
                                   FROM TRANSACTIONDETAIL
                                   GROUP BY TRANMID
                               ) pod ON pod.TRANMID = po.TRANMID
                               WHERE inv.REGSTRID = 18
                                 AND inv.TRANLMID IS NOT NULL
                                 AND inv.TRANLMID > 0
                                 AND (inv.DISPSTATUS = 0 OR inv.DISPSTATUS IS NULL)
                                 AND invd.QTY < pod.QTY
                               ORDER BY inv.TRANDATE DESC, inv.TRANNO DESC";

                    partialPurchaseInvoiceDetails = _db.Database.SqlQuery<PendingDocRow>(sql).ToList();
                }
                catch
                {
                    partialPurchaseInvoiceDetails = new List<PendingDocRow>();
                }


                // Pass essential business data to view
                ViewBag.CustomersCount = customersCount;
                ViewBag.SuppliersCount = suppliersCount;
                ViewBag.MaterialsCount = materialsCount;

                ViewBag.SalesOrderCount = salesOrderCount;
                ViewBag.PendingSalesOrderCount = pendingSalesOrderCount;
                ViewBag.PurchaseOrderCount = purchaseOrderCount;
                ViewBag.PendingPurchaseOrderCount = pendingPurchaseOrderCount;
                ViewBag.PurchaseInvoiceCount = purchaseInvoiceCount;
                ViewBag.PendingPurchaseInvoiceCount = pendingPurchaseInvoiceCount;
                ViewBag.PartialPurchaseInvoiceCount = partialPurchaseInvoiceCount;
                ViewBag.SalesInvoiceCount = salesInvoiceCount;
                ViewBag.PendingSalesInvoiceCount = pendingSalesInvoiceCount;

                ViewBag.PendingPurchaseOrderDetails = pendingPurchaseOrderDetails;
                ViewBag.PendingSalesOrderDetails = pendingSalesOrderDetails;
                ViewBag.PendingPurchaseInvoiceDetails = pendingPurchaseInvoiceDetails;
                ViewBag.PendingSalesInvoiceDetails = pendingSalesInvoiceDetails;
                ViewBag.PartialPurchaseInvoiceDetails = partialPurchaseInvoiceDetails;

                ViewBag.TransactionMetricsTotal = salesOrderCount + pendingSalesOrderCount + purchaseOrderCount + pendingPurchaseOrderCount + purchaseInvoiceCount + pendingPurchaseInvoiceCount + partialPurchaseInvoiceCount + salesInvoiceCount + pendingSalesInvoiceCount;

                // Chart Data - Monthly Sales Invoice Trends (Last 6 months)
                var monthlyInvoiceLabels = new List<string>();
                var monthlyInvoiceCounts = new List<int>();
                var monthlyInvoiceAmounts = new List<double>();

                
                try
                {
                    var sixMonthsAgo = DateTime.Now.AddMonths(-6);
                    var invoices = _db.TransactionMasters
                        .Where(t => t.REGSTRID == 20 &&
                                    (t.DISPSTATUS == 0 || t.DISPSTATUS == null) &&
                                    t.TRANDATE >= sixMonthsAgo)
                        .ToList();

                    if (invoices.Any())
                    {
                        var monthlyInvoices = invoices
                            .GroupBy(t => new { Year = t.TRANDATE.Year, Month = t.TRANDATE.Month })
                            .Select(g => new
                            {
                                Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                                Count = g.Count(),
                                TotalAmount = g.Sum(t => t.TRANNAMT)
                            })
                            .OrderBy(x => x.Month)
                            .ToList();

                        monthlyInvoiceLabels = monthlyInvoices.Select(x => x.Month).ToList();
                        monthlyInvoiceCounts = monthlyInvoices.Select(x => x.Count).ToList();
                        monthlyInvoiceAmounts = monthlyInvoices.Select(x => (double)x.TotalAmount).ToList();
                    }
                }
                catch { }

                ViewBag.MonthlyInvoiceLabels = monthlyInvoiceLabels;
                ViewBag.MonthlyInvoiceCounts = monthlyInvoiceCounts;
                ViewBag.MonthlyInvoiceAmounts = monthlyInvoiceAmounts;

                // Chart Data - Invoice Distribution
                ViewBag.StatusLabels = new List<string> { "Generated Purchase Invoice", "Partial Purchase Invoice", "Sales Invoice" };
                ViewBag.StatusCounts = new List<int> { purchaseInvoiceCount, partialPurchaseInvoiceCount, salesInvoiceCount };
                // Chart Data - Material Group Distribution
                var materialGroupLabels = new List<string>();
                var materialGroupCounts = new List<int>();
                
                try
                {
                    var materialGroups = _db.MaterialMasters
                        .Where(m => m.DISPSTATUS == 0 || m.DISPSTATUS == null)
                        .Join(_db.MaterialGroupMasters,
                            m => m.MTRLGID,
                            mg => mg.MTRLGID,
                            (m, mg) => new { Material = m, Group = mg })
                        .Where(x => x.Group.DISPSTATUS == 0 || x.Group.DISPSTATUS == null)
                        .GroupBy(x => x.Group.MTRLGDESC)
                        .Select(g => new { GroupName = g.Key ?? "Unknown", Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .Take(5)
                        .ToList();

                    materialGroupLabels = materialGroups.Select(x => x.GroupName).ToList();
                    materialGroupCounts = materialGroups.Select(x => x.Count).ToList();
                }
                catch
                {
                    materialGroupLabels = new List<string>();
                    materialGroupCounts = new List<int>();
                }

                ViewBag.MaterialGroupLabels = materialGroupLabels;
                ViewBag.MaterialGroupCounts = materialGroupCounts;

                // Chart Data - Key Metrics Distribution (for Pie/Bar toggle)
                var transactionTypeLabels = new List<string>
                {
                    "Total Sales Order",
                    "Pending Sales Order",
                    "Total Purchase Order",
                    "Pending Purchase Order",
                    "Total Purchase Invoice",
                    "Pending Purchase Invoice",
                    "Partial Purchase Invoice",
                    "Total Sales Invoice",
                    "Pending Sales Invoice"
                };

                var transactionTypeCounts = new List<int>
                {
                    salesOrderCount,
                    pendingSalesOrderCount,
                    purchaseOrderCount,
                    pendingPurchaseOrderCount,
                    purchaseInvoiceCount,
                    pendingPurchaseInvoiceCount,
                    partialPurchaseInvoiceCount,
                    salesInvoiceCount,
                    pendingSalesInvoiceCount
                };

                ViewBag.TransactionTypeLabels = transactionTypeLabels;
                ViewBag.TransactionTypeCounts = transactionTypeCounts;

                System.Diagnostics.Debug.WriteLine($"Customers: {customersCount}, Suppliers: {suppliersCount}, Materials: {materialsCount}");
                System.Diagnostics.Debug.WriteLine($"Monthly Invoice Labels Count: {monthlyInvoiceLabels.Count}");
                System.Diagnostics.Debug.WriteLine($"Material Group Labels Count: {materialGroupLabels.Count}");
                System.Diagnostics.Debug.WriteLine($"Transaction Type Labels Count: {transactionTypeLabels.Count}");
            }
            catch (Exception ex)
            {
                // Set default values on error
                ViewBag.CustomersCount = 0;
                ViewBag.SuppliersCount = 0;
                ViewBag.MaterialsCount = 0;

                ViewBag.SalesOrderCount = 0;
                ViewBag.PendingSalesOrderCount = 0;
                ViewBag.PurchaseOrderCount = 0;
                ViewBag.PendingPurchaseOrderCount = 0;
                ViewBag.PurchaseInvoiceCount = 0;
                ViewBag.PendingPurchaseInvoiceCount = 0;
                ViewBag.PartialPurchaseInvoiceCount = 0;
                ViewBag.SalesInvoiceCount = 0;
                ViewBag.PendingSalesInvoiceCount = 0;

                ViewBag.PendingPurchaseOrderDetails = new List<PendingDocRow>();
                ViewBag.PendingSalesOrderDetails = new List<PendingDocRow>();
                ViewBag.PendingPurchaseInvoiceDetails = new List<PendingDocRow>();
                ViewBag.PendingSalesInvoiceDetails = new List<PendingDocRow>();
                ViewBag.PartialPurchaseInvoiceDetails = new List<PendingDocRow>();

                ViewBag.TransactionMetricsTotal = 0;

                ViewBag.MonthlyInvoiceLabels = new List<string>();
                ViewBag.MonthlyInvoiceCounts = new List<int>();
                ViewBag.MonthlyInvoiceAmounts = new List<double>();

                ViewBag.StatusLabels = new List<string> { "Generated Purchase Invoice", "Partial Purchase Invoice", "Sales Invoice" };
                ViewBag.StatusCounts = new List<int> { 0, 0, 0 };

                ViewBag.MaterialGroupLabels = new List<string>();
                ViewBag.MaterialGroupCounts = new List<int>();
                ViewBag.TransactionTypeLabels = new List<string>();
                ViewBag.TransactionTypeCounts = new List<int>();
                
                ViewBag.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"ERROR in Index: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            return View();
        }

        [HttpGet]
        public ActionResult RenewalPopup(int memberId)
        {
            return HttpNotFound();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SubmitRenewal(RenewalSubmitRequest request)
        {
            Response.StatusCode = 404;
            return Json(new { success = false, message = "Not available" });
        }

        [HttpGet]
        public ActionResult Notifications()
        {
            return HttpNotFound();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AcceptNotification(int eventId)
        {
            return new HttpStatusCodeResult(204);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeclineNotification(int eventId)
        {
            return new HttpStatusCodeResult(204);
        }

        [HttpGet]
        public ActionResult UserDashboard()
        {
            return HttpNotFound();
        }
    }

    // Dashboard DTOs
    public class DashboardStat
    {
        public string StatType { get; set; }
        public int TotalCount { get; set; }
        public string Details { get; set; }
    }

    public class ShrimpByTypeDTO
    {
        public string ReceivedType { get; set; }
        public int Count { get; set; }
    }

    public class MonthlyInvoiceDTO
    {
        public string MonthName { get; set; }
        public int InvoiceCount { get; set; }
    }

    public class TopShrimpTypeDTO
    {
        public string ShrimpType { get; set; }
        public int Transactions { get; set; }
        public decimal TotalQuantity { get; set; }
    }

    public class PendingDocRow
    {
        public DateTime? Date { get; set; }
        public int? Number { get; set; }
        public string DocNo { get; set; }
        public string CustomerName { get; set; }
        public decimal Amount { get; set; }
    }
}