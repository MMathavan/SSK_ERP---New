using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Masters
{
    [SessionExpire]
    public class TransporterMasterController : Controller
    {
        ApplicationDbContext context = new ApplicationDbContext();

        [Authorize(Roles = "TransporterMasterIndex")]
        public ActionResult Index()
        {
            try
            {
                var transporters = context.Database.SqlQuery<TransporterMaster>(
                    @"SELECT CATEID, CATETID, CATENAME, CATEADDR1, CATEADDR2, CATEADDR3, CATEADDR4,
                             CATEPHN1, CATEPHN2, CATEPHN3, CATEPHN4, CATECPNAME, CATEEMAIL,
                             CATECODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE, CATE_TRACKING_LINK
                      FROM TRANSPORTERMASTER"
                ).ToList();

                return View(transporters);
            }
            catch (Exception ex)
            {
                return Content($"Error loading transporters: {ex.Message}");
            }
        }

        public JsonResult GetAjaxData(JQueryDataTableParamModel param)
        {
            try
            {
                var transporters = context.Database.SqlQuery<TransporterMaster>(
                    @"SELECT CATEID, CATETID, CATENAME, CATEADDR1, CATEADDR2, CATEADDR3, CATEADDR4,
                             CATEPHN1, CATEPHN2, CATEPHN3, CATEPHN4, CATECPNAME, CATEEMAIL,
                             CATECODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE, CATE_TRACKING_LINK
                      FROM TRANSPORTERMASTER"
                ).ToList();

                var allTransporters = transporters.Select(t => new
                {
                    CATEID = t.CATEID,
                    CATECODE = t.CATECODE ?? string.Empty,
                    CATENAME = t.CATENAME ?? string.Empty,
                    CATEPHN1 = t.CATEPHN1 ?? string.Empty,
                    CATECPNAME = t.CATECPNAME ?? string.Empty,
                    CATEEMAIL = t.CATEEMAIL ?? string.Empty,
                    DISPSTATUS = t.DISPSTATUS,
                    CATE_TRACKING_LINK = t.CATE_TRACKING_LINK ?? string.Empty
                }).ToList();

                return Json(new { aaData = allTransporters }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = "Error loading data" }, JsonRequestBehavior.AllowGet);
            }
        }

        [Authorize(Roles = "TransporterMasterCreate,TransporterMasterEdit")]
        public ActionResult Form(int? id)
        {
            try
            {
                TransporterMaster tab = new TransporterMaster();
                ViewBag.msg = "Add New Transporter";

                if (id == null)
                {
                    tab.CATEID = 0;
                    tab.CATETID = 1;
                    tab.DISPSTATUS = 0; // Enabled by default
                }

                if (id == -1)
                    ViewBag.msg = "<div class='msg'>Record Successfully Saved</div>";

                if (id != 0 && id != -1 && id != null)
                {
                    try
                    {
                        tab = context.Database.SqlQuery<TransporterMaster>(
                            "SELECT * FROM TRANSPORTERMASTER WHERE CATEID = {0}", id
                        ).FirstOrDefault();

                        if (tab == null)
                            return HttpNotFound();

                        ViewBag.msg = "Edit Transporter";
                    }
                    catch (Exception)
                    {
                        tab = new TransporterMaster
                        {
                            CATEID = 0,
                            CATETID = 1,
                            DISPSTATUS = 0
                        };
                        ViewBag.msg = $"Error loading transporter {id}. Creating new instead.";
                    }
                }

                var statusList = new List<SelectListItem>
                {
                    new SelectListItem { Value = "0", Text = "Enabled" },
                    new SelectListItem { Value = "1", Text = "Disabled" }
                };
                ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", tab.DISPSTATUS.ToString());

                return View(tab);
            }
            catch (Exception ex)
            {
                return Content($"Error in Form action: {ex.Message}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "TransporterMasterCreate,TransporterMasterEdit")]
        public ActionResult savedata(TransporterMaster tab)
        {
            try
            {
                var currentUserId = Session["USERID"] != null ? Convert.ToInt32(Session["USERID"]) : 1;
                var prcsdate = DateTime.Now;

                if (!ModelState.IsValid)
                {
                    var statusList = new List<SelectListItem>
                    {
                        new SelectListItem { Value = "0", Text = "Enabled" },
                        new SelectListItem { Value = "1", Text = "Disabled" }
                    };
                    ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", tab.DISPSTATUS.ToString());

                    return View("Form", tab);
                }

                if (tab.CATETID == 0)
                    tab.CATETID = 1;

                if (tab.CATEID == 0)
                {
                    tab.CUSRID = Session["USERNAME"]?.ToString() ?? "admin";
                    tab.LMUSRID = currentUserId;
                    tab.PRCSDATE = prcsdate;

                    context.Database.ExecuteSqlCommand(
                        @"INSERT INTO TRANSPORTERMASTER
                          (CATETID, CATENAME, CATEADDR1, CATEADDR2, CATEADDR3, CATEADDR4,
                           CATEPHN1, CATEPHN2, CATEPHN3, CATEPHN4, CATECPNAME, CATEEMAIL,
                           CATECODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE, CATE_TRACKING_LINK)
                          VALUES
                          ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11},
                           {12}, {13}, {14}, {15}, {16}, {17})",
                        tab.CATETID, tab.CATENAME, tab.CATEADDR1, tab.CATEADDR2, tab.CATEADDR3, tab.CATEADDR4,
                        tab.CATEPHN1, tab.CATEPHN2, tab.CATEPHN3, tab.CATEPHN4, tab.CATECPNAME, tab.CATEEMAIL,
                        tab.CATECODE, tab.CUSRID, tab.LMUSRID, tab.DISPSTATUS, tab.PRCSDATE, tab.CATE_TRACKING_LINK
                    );
                }
                else
                {
                    tab.LMUSRID = currentUserId;
                    tab.PRCSDATE = prcsdate;

                    context.Database.ExecuteSqlCommand(
                        @"UPDATE TRANSPORTERMASTER SET
                          CATETID = {1}, CATENAME = {2}, CATEADDR1 = {3}, CATEADDR2 = {4}, CATEADDR3 = {5}, CATEADDR4 = {6},
                          CATEPHN1 = {7}, CATEPHN2 = {8}, CATEPHN3 = {9}, CATEPHN4 = {10}, CATECPNAME = {11}, CATEEMAIL = {12},
                          CATECODE = {13}, LMUSRID = {14}, DISPSTATUS = {15}, PRCSDATE = {16}, CATE_TRACKING_LINK = {17}
                          WHERE CATEID = {0}",
                        tab.CATEID, tab.CATETID, tab.CATENAME, tab.CATEADDR1, tab.CATEADDR2, tab.CATEADDR3, tab.CATEADDR4,
                        tab.CATEPHN1, tab.CATEPHN2, tab.CATEPHN3, tab.CATEPHN4, tab.CATECPNAME, tab.CATEEMAIL,
                        tab.CATECODE, tab.LMUSRID, tab.DISPSTATUS, tab.PRCSDATE, tab.CATE_TRACKING_LINK
                    );
                }

                return RedirectToAction("Index");
            }
            catch (Exception)
            {
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [Authorize(Roles = "TransporterMasterDelete")]
        public ActionResult deletedata(int id)
        {
            try
            {
                context.Database.ExecuteSqlCommand("DELETE FROM TRANSPORTERMASTER WHERE CATEID = {0}", id);
                return Content("Deleted Successfully ...");
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Content("Error deleting transporter: " + ex.Message);
            }
        }
    }
}
