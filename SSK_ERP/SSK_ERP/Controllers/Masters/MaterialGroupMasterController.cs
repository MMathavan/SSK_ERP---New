using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Masters
{
    [SSK_ERP.SessionExpire]
    public class MaterialGroupMasterController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();

        // GET: MaterialGroupMaster
        [Authorize(Roles = "MaterialGroupMasterIndex")]
        public ActionResult Index()
        {
            return View();
        }

        // GET: MaterialGroupMaster/Form
        [Authorize(Roles = "MaterialGroupMasterCreate,MaterialGroupMasterEdit")]
        public ActionResult Form(int id = 0)
        {
            MaterialGroupMaster tab = new MaterialGroupMaster();

            if (id == 0)
            {
                // New record
                tab.MTRLGID = 0;
                tab.MTRLTID = 0;
                tab.DISPSTATUS = 0; // Default to Enabled
            }
            else
            {
                // Edit existing record - use raw SQL like MaterialMaster so MTRLTID is correctly loaded
                tab = db.Database.SqlQuery<MaterialGroupMaster>(
                    @"SELECT MTRLGID, MTRLTID, MTRLGDESC, MTRLGCODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE 
                      FROM MATERIALGROUPMASTER WHERE MTRLGID = @p0",
                    id
                ).FirstOrDefault();

                if (tab == null)
                {
                    ViewBag.msg = "<div class='alert alert-danger'>Record not found!</div>";
                    tab = new MaterialGroupMaster();
                    tab.MTRLGID = 0;
                    tab.MTRLTID = 0;
                    tab.DISPSTATUS = 0;
                }
            }

            PopulateDropdowns(tab);

            return View(tab);
        }

        // POST: MaterialGroupMaster/savedata
        [HttpPost]
        [Authorize(Roles = "MaterialGroupMasterCreate,MaterialGroupMasterEdit")]
        public ActionResult savedata(MaterialGroupMaster tab)
        {
            try
            {
                // Enforce Material Type selection (MTRLTID > 0)
                if (tab.MTRLTID <= 0)
                {
                    ModelState.AddModelError("MTRLTID", "Please select material type.");
                }

                if (ModelState.IsValid)
                {
                    // Check for duplicate code on server side
                    var duplicateCheck = db.Database.SqlQuery<int>(
                        @"SELECT COUNT(*) FROM MATERIALGROUPMASTER 
                          WHERE UPPER(MTRLGCODE) = @p0 AND MTRLGID != @p1",
                        tab.MTRLGCODE.ToUpper(), tab.MTRLGID
                    ).FirstOrDefault();

                    if (duplicateCheck > 0)
                    {
                        ModelState.AddModelError("MTRLGCODE", "This material group code is already used.");
                        ViewBag.msg = "<div class='alert alert-danger'>Material group code already exists. Please use a different code.</div>";
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
                        if (!string.IsNullOrEmpty(tab.MTRLGDESC))
                        {
                            tab.MTRLGDESC = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(tab.MTRLGDESC.ToLower());
                        }

                        // Auto-format code to uppercase
                        if (!string.IsNullOrEmpty(tab.MTRLGCODE))
                        {
                            tab.MTRLGCODE = tab.MTRLGCODE.ToUpper();
                        }

                        if (tab.MTRLGID == 0)
                        {
                            // New record
                            System.Diagnostics.Debug.WriteLine($"Creating new record with CUSRID: {currentUserName}, LMUSRID: {currentUserName}");

                            db.Database.ExecuteSqlCommand(
                                @"INSERT INTO MATERIALGROUPMASTER (MTRLTID, MTRLGDESC, MTRLGCODE, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE) 
                                  VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)",
                                tab.MTRLTID, tab.MTRLGDESC, tab.MTRLGCODE, currentUserName, currentUserName, tab.DISPSTATUS, prcsdate);

                            return RedirectToAction("Index");
                        }
                        else
                        {
                            // Update existing record
                            System.Diagnostics.Debug.WriteLine($"Updating existing record ID: {tab.MTRLGID}, LMUSRID: {currentUserName}");

                            db.Database.ExecuteSqlCommand(
                                @"UPDATE MATERIALGROUPMASTER 
                                  SET MTRLTID = @p0, MTRLGDESC = @p1, MTRLGCODE = @p2, LMUSRID = @p3, 
                                      DISPSTATUS = @p4, PRCSDATE = @p5 
                                  WHERE MTRLGID = @p6",
                                tab.MTRLTID, tab.MTRLGDESC, tab.MTRLGCODE, currentUserName, tab.DISPSTATUS, prcsdate, tab.MTRLGID);

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

            PopulateDropdowns(tab);

            return View("Form", tab);
        }

        private void PopulateDropdowns(MaterialGroupMaster tab)
        {
            // Status dropdown
            var statusList = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Enabled" },
                new SelectListItem { Value = "1", Text = "Disabled" }
            };

            ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", tab.DISPSTATUS.ToString());

            // Material Type dropdown (only enabled items), same pattern as MaterialMaster
            var typeItems = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "-- Select Material Type --" }
            };

            try
            {
                if (db.MaterialTypeMasters != null)
                {
                    var types = db.MaterialTypeMasters
                        .Where(t => t.DISPSTATUS == 0)
                        .OrderBy(t => t.MTRLTDESC)
                        .ToList();

                    // If in edit mode and current type is disabled, still include it
                    if (tab.MTRLTID > 0 && !types.Any(t => t.MTRLTID == tab.MTRLTID))
                    {
                        var currentType = db.MaterialTypeMasters.FirstOrDefault(t => t.MTRLTID == tab.MTRLTID);
                        if (currentType != null)
                        {
                            types.Add(currentType);
                        }
                    }

                    var selectedType = tab.MTRLTID > 0 ? tab.MTRLTID.ToString() : string.Empty;

                    foreach (var t in types.OrderBy(t => t.MTRLTDESC))
                    {
                        typeItems.Add(new SelectListItem
                        {
                            Value = t.MTRLTID.ToString(),
                            Text = t.MTRLTDESC,
                            Selected = (t.MTRLTID.ToString() == selectedType)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MaterialGroupMaster PopulateDropdowns MaterialType error: " + ex.Message);
            }

            // Use the prepared list with Selected flag so edit mode shows saved material type
            ViewBag.MTRLTID = typeItems;
        }

        // GET: MaterialGroupMaster/GetAjaxData
        [HttpGet]
        public JsonResult GetAjaxData()
        {
            try
            {
                var materialGroups = db.Database.SqlQuery<MaterialGroupMaster>(
                    @"SELECT MTRLGID, MTRLTID, MTRLGDESC, MTRLGCODE, CUSRID, LMUSRID, 
                             DISPSTATUS, PRCSDATE 
                      FROM MATERIALGROUPMASTER 
                      ORDER BY MTRLGCODE"
                ).ToList();

                var result = materialGroups.Select(m => new
                {
                    m.MTRLGID,
                    m.MTRLGCODE,
                    m.MTRLGDESC,
                    MaterialTypeName = GetMaterialTypeName(m.MTRLTID),
                    m.DISPSTATUS
                }).ToList();

                return Json(new { data = result }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MaterialGroupMaster GetAjaxData Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

                return Json(new
                {
                    data = new List<object>(),
                    error = ex.Message,
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        private string GetMaterialTypeName(int mtrltid)
        {
            try
            {
                var result = db.Database.SqlQuery<string>(
                    "SELECT MTRLTDESC FROM MATERIALTYPEMASTER WHERE MTRLTID = @p0",
                    mtrltid
                ).FirstOrDefault();

                return result ?? "N/A";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetMaterialTypeName Error: {ex.Message}");
                return "N/A";
            }
        }

        // POST: MaterialGroupMaster/Del
        [HttpPost]
        public ActionResult Del(int id)
        {
            try
            {
                if (!User.IsInRole("MaterialGroupMasterDelete"))
                {
                    return Content("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }

                var rowsAffected = db.Database.ExecuteSqlCommand(
                    "DELETE FROM MATERIALGROUPMASTER WHERE MTRLGID = @p0", id);

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

        // Remote validation for unique material group code
        [HttpPost]
        public JsonResult ValidateMTRLGCODE(string MTRLGCODE, int MTRLGID = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(MTRLGCODE))
                {
                    return Json(true, JsonRequestBehavior.AllowGet);
                }

                var existingRecord = db.Database.SqlQuery<int>(
                    @"SELECT COUNT(*) FROM MATERIALGROUPMASTER 
                      WHERE UPPER(MTRLGCODE) = @p0 AND MTRLGID != @p1",
                    MTRLGCODE.ToUpper(), MTRLGID
                ).FirstOrDefault();

                bool isUnique = existingRecord == 0;

                if (isUnique)
                {
                    return Json(true, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    return Json("This material group code is already used.", JsonRequestBehavior.AllowGet);
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
