using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Masters
{
    [SSK_ERP.SessionExpire]
    public class MaterialTypeMasterController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();

        // GET: MaterialTypeMaster
        [Authorize(Roles = "MaterialTypeMasterIndex")]
        public ActionResult Index()
        {
            return View();
        }

        // GET: MaterialTypeMaster/Form
        [Authorize(Roles = "MaterialTypeMasterCreate,MaterialTypeMasterEdit")]
        public ActionResult Form(int id = 0)
        {
            MaterialTypeMaster tab = new MaterialTypeMaster();

            if (id == 0)
            {
                // New record
                tab.MTRLTID = 0;
                tab.DISPSTATUS = 0; // Default to Enabled
            }
            else
            {
                // Edit existing record
                tab = db.MaterialTypeMasters.Find(id);
                if (tab == null)
                {
                    ViewBag.msg = "<div class='alert alert-danger'>Record not found!</div>";
                    tab = new MaterialTypeMaster();
                    tab.DISPSTATUS = 0;
                }
            }

            // Populate status dropdown with proper selection
            var statusList = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Enabled" },
                new SelectListItem { Value = "1", Text = "Disabled" }
            };

            ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", tab.DISPSTATUS.ToString());

            return View(tab);
        }

        // POST: MaterialTypeMaster/savedata
        [HttpPost]
        [Authorize(Roles = "MaterialTypeMasterCreate,MaterialTypeMasterEdit")]
        public ActionResult savedata(MaterialTypeMaster tab)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    // Check for duplicate code on server side
                    var duplicateCheck = db.Database.SqlQuery<int>(
                        @"SELECT COUNT(*) FROM MATERIALTYPEMASTER 
                          WHERE UPPER(MTRLTCODE) = @p0 AND MTRLTID != @p1",
                        tab.MTRLTCODE.ToUpper(), tab.MTRLTID
                    ).FirstOrDefault();

                    if (duplicateCheck > 0)
                    {
                        ModelState.AddModelError("MTRLTCODE", "This material type code is already used.");
                        ViewBag.msg = "<div class='alert alert-danger'>Material type code already exists. Please use a different code.</div>";
                    }
                    else
                    {
                        // Get current user information from session
                        var currentUserName = Session["CUSRID"]?.ToString();

                        if (string.IsNullOrEmpty(currentUserName))
                        {
                            currentUserName = User.Identity.Name ?? "admin";
                        }

                        System.Diagnostics.Debug.WriteLine($"Session CUSRID: {Session["CUSRID"]}");
                        System.Diagnostics.Debug.WriteLine($"Session Group: {Session["Group"]}");
                        System.Diagnostics.Debug.WriteLine($"User.Identity.Name: {User.Identity.Name}");
                        System.Diagnostics.Debug.WriteLine($"Final currentUserName: {currentUserName}");

                        var prcsdate = DateTime.Now;

                        // Auto-format description to title case
                        if (!string.IsNullOrEmpty(tab.MTRLTDESC))
                        {
                            tab.MTRLTDESC = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(tab.MTRLTDESC.ToLower());
                        }

                        // Auto-format code to uppercase
                        if (!string.IsNullOrEmpty(tab.MTRLTCODE))
                        {
                            tab.MTRLTCODE = tab.MTRLTCODE.ToUpper();
                        }

                        if (tab.MTRLTID == 0)
                        {
                            // New record
                            System.Diagnostics.Debug.WriteLine($"Creating new record with CUSRID: {currentUserName}, LMUSRID: {currentUserName}");

                            db.Database.ExecuteSqlCommand(
                                @"INSERT INTO MATERIALTYPEMASTER (MTRLTDESC, MTRLTCODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE) 
                                  VALUES (@p0, @p1, @p2, @p3, @p4, @p5)",
                                tab.MTRLTDESC, tab.MTRLTCODE, currentUserName, currentUserName, tab.DISPSTATUS, prcsdate);

                            return RedirectToAction("Index");
                        }
                        else
                        {
                            // Update existing record
                            System.Diagnostics.Debug.WriteLine($"Updating existing record ID: {tab.MTRLTID}, LMUSRID: {currentUserName}");

                            db.Database.ExecuteSqlCommand(
                                @"UPDATE MATERIALTYPEMASTER 
                                  SET MTRLTDESC = @p0, MTRLTCODE = @p1, LMUSRID = @p2, 
                                      DISPSTATUS = @p3, PRCSDATE = @p4 
                                  WHERE MTRLTID = @p5",
                                tab.MTRLTDESC, tab.MTRLTCODE, currentUserName, tab.DISPSTATUS, prcsdate, tab.MTRLTID);

                            return RedirectToAction("Index");
                        }
                    }
                }
                else
                {
                    ViewBag.msg = "<div class='alert alert-danger'>Please fix the validation errors.</div>";
                }
            }
            catch (Exception ex)
            {
                ViewBag.msg = "<div class='alert alert-danger'>Error: " + ex.Message + "</div>";
            }

            // Rebuild dropdown for status (for validation errors)
            var statusList = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Enabled" },
                new SelectListItem { Value = "1", Text = "Disabled" }
            };

            ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", tab.DISPSTATUS.ToString());

            return View("Form", tab);
        }

        // GET: MaterialTypeMaster/GetAjaxData
        [HttpGet]
        public JsonResult GetAjaxData()
        {
            try
            {
                var materialTypes = db.Database.SqlQuery<MaterialTypeMaster>(
                    @"SELECT MTRLTID, MTRLTDESC, MTRLTCODE, CUSRID, LMUSRID, 
                             DISPSTATUS, PRCSDATE 
                      FROM MATERIALTYPEMASTER 
                      ORDER BY MTRLTCODE"
                ).ToList();

                var result = materialTypes.Select(m => new
                {
                    m.MTRLTID,
                    m.MTRLTCODE,
                    m.MTRLTDESC,
                    m.DISPSTATUS
                }).ToList();

                return Json(new { data = result }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MaterialTypeMaster GetAjaxData Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

                return Json(new
                {
                    data = new List<object>(),
                    error = ex.Message,
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: MaterialTypeMaster/Del
        [HttpPost]
        public ActionResult Del(int id)
        {
            try
            {
                if (!User.IsInRole("MaterialTypeMasterDelete"))
                {
                    return Content("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }

                var rowsAffected = db.Database.ExecuteSqlCommand(
                    "DELETE FROM MATERIALTYPEMASTER WHERE MTRLTID = @p0", id);

                if (rowsAffected > 0)
                {
                    return Content("Successfully deleted");
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

        // Remote validation for unique material type code
        [HttpPost]
        public JsonResult ValidateMTRLTCODE(string MTRLTCODE, int MTRLTID = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(MTRLTCODE))
                {
                    return Json(true, JsonRequestBehavior.AllowGet);
                }

                var existingRecord = db.Database.SqlQuery<int>(
                    @"SELECT COUNT(*) FROM MATERIALTYPEMASTER 
                      WHERE UPPER(MTRLTCODE) = @p0 AND MTRLTID != @p1",
                    MTRLTCODE.ToUpper(), MTRLTID
                ).FirstOrDefault();

                bool isUnique = existingRecord == 0;

                if (isUnique)
                {
                    return Json(true, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    return Json("This material type code is already used.", JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Validation Error: {ex.Message}");
                return Json(true, JsonRequestBehavior.AllowGet);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
