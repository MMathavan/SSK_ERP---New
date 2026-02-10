using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Masters
{
    [SSK_ERP.SessionExpire]
    public class RegionMasterController : Controller
    {
        private ApplicationDbContext context = new ApplicationDbContext();

        // GET: RegionMaster
        [Authorize(Roles = "RegionMasterIndex")]
        public ActionResult Index()
        {
            // View uses DataTables via AJAX, so we don't need to pass the model here
            return View();
        }

        // GET: RegionMaster/Form
        [Authorize(Roles = "RegionMasterCreate,RegionMasterEdit")]
        public ActionResult Form(int? id = 0)
        {
            RegionMaster tab = new RegionMaster();
            tab.REGNID = 0;
            tab.DISPSTATUS = 0; // Default to Enabled

            if (id == -1)
                ViewBag.msg = "<div class='msg'>Record Successfully Saved</div>";

            if (id != null && id > 0)
            {
                // Load existing record using raw SQL to match database schema
                var existing = context.Database.SqlQuery<RegionMaster>(
                    @"SELECT REGNID, REGNDESC, REGNCODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE 
                      FROM REGIONMASTER WHERE REGNID = @p0", id).FirstOrDefault();

                if (existing != null)
                {
                    tab = existing;
                }
            }

            // Ensure dropdown binding ignores any previous ModelState values
            ModelState.Remove("DISPSTATUS");

            var statusList = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Enabled" },
                new SelectListItem { Value = "1", Text = "Disabled" }
            };

            ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", tab.DISPSTATUS.ToString());

            return View(tab);
        }

        // POST: RegionMaster/savedata
        [HttpPost]
        [Authorize(Roles = "RegionMasterCreate,RegionMasterEdit")]
        public ActionResult savedata(RegionMaster tab)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var statusListInvalid = new List<SelectListItem>
                    {
                        new SelectListItem { Value = "0", Text = "Enabled" },
                        new SelectListItem { Value = "1", Text = "Disabled" }
                    };

                    ViewBag.DISPSTATUS = new SelectList(statusListInvalid, "Value", "Text", tab.DISPSTATUS.ToString());
                    ViewBag.msg = "<div class='alert alert-danger'>Please fix the validation errors.</div>";
                    return View("Form", tab);
                }

                var prcsdate = DateTime.Now;
                var cusrid = Session["CUSRID"] != null ? Session["CUSRID"].ToString() : "admin";
                var currentUserId = Session["USERID"] != null ? Convert.ToInt32(Session["USERID"]) : 1;

                // Normalize description and code similar to other masters
                var desc = tab.REGNDESC ?? string.Empty;
                desc = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(desc.ToLower());

                var code = tab.REGNCODE ?? string.Empty;
                code = code.ToUpper();

                if (tab.REGNID == 0)
                {
                    // New record - set CRMCOUNTRYID default to 0
                    context.Database.ExecuteSqlCommand(
                        @"INSERT INTO REGIONMASTER (REGNDESC, REGNCODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE, CRMCOUNTRYID) 
                          VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)",
                        desc, code, cusrid, currentUserId, tab.DISPSTATUS, prcsdate, 0);
                }
                else
                {
                    // Update existing record - preserve original CUSRID, update LMUSRID
                    var existing = context.Database.SqlQuery<RegionMaster>(
                        @"SELECT REGNID, REGNDESC, REGNCODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE 
                          FROM REGIONMASTER WHERE REGNID = @p0", tab.REGNID).FirstOrDefault();

                    if (existing != null)
                    {
                        context.Database.ExecuteSqlCommand(
                            @"UPDATE REGIONMASTER 
                              SET REGNDESC = @p0, REGNCODE = @p1, LMUSRID = @p2, 
                                  DISPSTATUS = @p3, PRCSDATE = @p4 
                              WHERE REGNID = @p5",
                            desc, code, currentUserId, tab.DISPSTATUS, prcsdate, tab.REGNID);
                    }
                }

                if (Request.Form.Get("continue") == null)
                {
                    return RedirectToAction("Index");
                }
                else
                {
                    return RedirectToAction("Form", new { id = -1 });
                }
            }
            catch (Exception ex)
            {
                ViewBag.msg = "<div class='alert alert-danger'>Error: " + ex.Message + "</div>";

                var statusList = new List<SelectListItem>
                {
                    new SelectListItem { Value = "0", Text = "Enabled" },
                    new SelectListItem { Value = "1", Text = "Disabled" }
                };

                ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", tab.DISPSTATUS.ToString());
                return View("Form", tab);
            }
        }

        // GET: RegionMaster/GetAjaxData
        [HttpGet]
        public JsonResult GetAjaxData()
        {
            try
            {
                var regions = context.Database.SqlQuery<RegionMaster>(
                    @"SELECT REGNID, REGNDESC, REGNCODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE 
                      FROM REGIONMASTER ORDER BY REGNCODE").ToList();

                var result = regions.Select(r => new
                {
                    r.REGNID,
                    r.REGNCODE,
                    r.REGNDESC,
                    DISPSTATUS = r.DISPSTATUS == 0 ? "Enabled" : "Disabled"
                }).ToList();

                return Json(new { data = result }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RegionMaster GetAjaxData Error: " + ex.Message);
                return Json(new
                {
                    data = new List<object>(),
                    error = ex.Message,
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: RegionMaster/Del
        [HttpPost]
        public ActionResult Del(int id, string fld)
        {
            try
            {
                // Match existing delete pattern used in the RegionMaster Index view
                if (!User.IsInRole("RegionMasterDelete"))
                {
                    // Empty string signals "not authorized" to the client script
                    return Content(string.Empty);
                }

                var rows = context.Database.ExecuteSqlCommand(
                    @"DELETE FROM REGIONMASTER WHERE REGNID = @p0", id);

                if (rows > 0)
                {
                    return Content("Deleted Successfully ...");
                }
                else
                {
                    return Content("Record not found");
                }
            }
            catch (Exception ex)
            {
                return Content("Error: " + ex.Message);
            }
        }
    }
}
