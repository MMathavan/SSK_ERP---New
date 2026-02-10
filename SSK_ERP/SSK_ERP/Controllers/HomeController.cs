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
                int transactionsCount = 0;
                int customersCount = 0, suppliersCount = 0, materialsCount = 0;
                int rawMaterialIntakeCount = 0, invoicesCount = 0;
                int pendingInvoicesCount = 0, approvedInvoicesCount = 0;

                try { transactionsCount = _db.TransactionMasters.Count(t => t.DISPSTATUS == 0 || t.DISPSTATUS == null); } catch { }
                try { customersCount = _db.CustomerMasters.Count(c => c.DISPSTATUS == 0 || c.DISPSTATUS == null); } catch { }
                try { suppliersCount = _db.SupplierMasters.Count(s => s.DISPSTATUS == 0 || s.DISPSTATUS == null); } catch { }
                try { materialsCount = _db.MaterialMasters.Count(m => m.DISPSTATUS == 0 || m.DISPSTATUS == null); } catch { }
                try { rawMaterialIntakeCount = _db.TransactionMasters.Count(t => t.REGSTRID == 1 && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)); } catch { }
                try { invoicesCount = _db.TransactionMasters.Count(t => t.REGSTRID == 2 && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)); } catch { }
                
                // For pending/approved invoices - try SQL query, fallback to simple count if it fails
                try
                {
                    var pendingInvoicesQuery = @"SELECT COUNT(*) FROM TRANSACTIONMASTER tm
                                                LEFT JOIN PURCHASEINVOICESTATUS pis ON tm.DISPSTATUS = pis.PUINSTID
                                                WHERE tm.REGSTRID = 2 
                                                AND (tm.DISPSTATUS = 0 OR tm.DISPSTATUS IS NULL)
                                                AND pis.PUINSTCODE = 'PUS003'";
                    var result = _db.Database.SqlQuery<int>(pendingInvoicesQuery).FirstOrDefault();
                    pendingInvoicesCount = result;
                }
                catch 
                { 
                    // Fallback: count all invoices as pending if status check fails
                    try { pendingInvoicesCount = _db.TransactionMasters.Count(t => t.REGSTRID == 2 && (t.DISPSTATUS == 0 || t.DISPSTATUS == null)); } catch { }
                }
                
                try
                {
                    var approvedInvoicesQuery = @"SELECT COUNT(*) FROM TRANSACTIONMASTER tm
                                                 LEFT JOIN PURCHASEINVOICESTATUS pis ON tm.DISPSTATUS = pis.PUINSTID
                                                 WHERE tm.REGSTRID = 2 
                                                 AND (tm.DISPSTATUS = 0 OR tm.DISPSTATUS IS NULL)
                                                 AND pis.PUINSTCODE = 'PUS004'";
                    var result = _db.Database.SqlQuery<int>(approvedInvoicesQuery).FirstOrDefault();
                    approvedInvoicesCount = result;
                }
                catch 
                { 
                    approvedInvoicesCount = 0;
                }

                // Pass essential business data to view
                ViewBag.TransactionsCount = transactionsCount;
                ViewBag.CustomersCount = customersCount;
                ViewBag.SuppliersCount = suppliersCount;
                ViewBag.MaterialsCount = materialsCount;
                ViewBag.RawMaterialIntakeCount = rawMaterialIntakeCount;
                ViewBag.InvoicesCount = invoicesCount;
                ViewBag.PendingInvoicesCount = pendingInvoicesCount;
                ViewBag.ApprovedInvoicesCount = approvedInvoicesCount;

                // Chart Data - Monthly Invoice Trends (Last 6 months)
                var monthlyInvoiceLabels = new List<string>();
                var monthlyInvoiceCounts = new List<int>();
                var monthlyInvoiceAmounts = new List<double>();
                
                try
                {
                    var sixMonthsAgo = DateTime.Now.AddMonths(-6);
                    var invoices = _db.TransactionMasters
                        .Where(t => t.REGSTRID == 2 && 
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

                // Chart Data - Transaction Status Distribution
                ViewBag.StatusLabels = new List<string> { "Pending", "Approved", "Raw Material Intake" };
                ViewBag.StatusCounts = new List<int> { pendingInvoicesCount, approvedInvoicesCount, rawMaterialIntakeCount };

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
                catch { }

                ViewBag.MaterialGroupLabels = materialGroupLabels;
                ViewBag.MaterialGroupCounts = materialGroupCounts;

                // Chart Data - Transaction Types Distribution
                var transactionTypeLabels = new List<string>();
                var transactionTypeCounts = new List<int>();
                
                try
                {
                    var transactionTypes = _db.TransactionMasters
                        .Where(t => t.DISPSTATUS == 0 || t.DISPSTATUS == null)
                        .GroupBy(t => t.REGSTRID)
                        .Select(g => new
                        {
                            TypeId = g.Key,
                            TypeName = g.Key == 1 ? "Raw Material Intake" : 
                                       g.Key == 2 ? "Raw Material Invoice" : 
                                       $"Transaction Type {g.Key}",
                            Count = g.Count()
                        })
                        .OrderByDescending(x => x.Count)
                        .Take(5)
                        .ToList();

                    transactionTypeLabels = transactionTypes.Select(x => x.TypeName).ToList();
                    transactionTypeCounts = transactionTypes.Select(x => x.Count).ToList();
                }
                catch { }

                ViewBag.TransactionTypeLabels = transactionTypeLabels;
                ViewBag.TransactionTypeCounts = transactionTypeCounts;

                // Debug output (after all variables are declared)
                System.Diagnostics.Debug.WriteLine($"=== Dashboard Data ===");
                System.Diagnostics.Debug.WriteLine($"Transactions: {transactionsCount}, Customers: {customersCount}, Suppliers: {suppliersCount}");
                System.Diagnostics.Debug.WriteLine($"Materials: {materialsCount}, Raw Material Intake: {rawMaterialIntakeCount}");
                System.Diagnostics.Debug.WriteLine($"Invoices: {invoicesCount}, Pending: {pendingInvoicesCount}, Approved: {approvedInvoicesCount}");
                System.Diagnostics.Debug.WriteLine($"Monthly Invoice Labels Count: {monthlyInvoiceLabels.Count}");
                System.Diagnostics.Debug.WriteLine($"Material Group Labels Count: {materialGroupLabels.Count}");
                System.Diagnostics.Debug.WriteLine($"Transaction Type Labels Count: {transactionTypeLabels.Count}");
            }
            catch (Exception ex)
            {
                // Set default values on error
                ViewBag.TransactionsCount = 0;
                ViewBag.CustomersCount = 0;
                ViewBag.SuppliersCount = 0;
                ViewBag.MaterialsCount = 0;
                ViewBag.RawMaterialIntakeCount = 0;
                ViewBag.InvoicesCount = 0;
                ViewBag.PendingInvoicesCount = 0;
                ViewBag.ApprovedInvoicesCount = 0;
                
                // Empty chart data
                ViewBag.MonthlyInvoiceLabels = new List<string>();
                ViewBag.MonthlyInvoiceCounts = new List<int>();
                ViewBag.MonthlyInvoiceAmounts = new List<double>();
                ViewBag.StatusLabels = new List<string> { "Pending", "Approved", "Raw Material Intake" };
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
}