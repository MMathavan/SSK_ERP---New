using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Masters
{
    [SSK_ERP.SessionExpire]
    public class MaterialMasterController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        // Lightweight DTO for MaterialMaster index data loaded via stored procedure
        private class MaterialMasterIndexRow
        {
            public int MTRLID { get; set; }
            public string MTRLCODE { get; set; }
            public string MTRLDESC { get; set; }
            public string GroupName { get; set; }
            public string UnitName { get; set; }
            public string HSNName { get; set; }
            public decimal MTRLPRFT { get; set; }
            public short DISPSTATUS { get; set; }
        }

        // GET: MaterialMaster
        [Authorize(Roles = "MaterialMasterIndex")]
        public ActionResult Index()
        {
            return View();
        }

        // GET: MaterialMaster/Form
        [Authorize(Roles = "MaterialMasterCreate,MaterialMasterEdit")]
        public ActionResult Form(int? id)
        {
            MaterialMaster tab = new MaterialMaster();

            if (id != null && id > 0)
            {
                tab = db.Database.SqlQuery<MaterialMaster>(
                    @"SELECT MTRLID, MTRLGID, MTRLDESC, MTRLCODE, UNITID, MTRLPRFT, RATE, HSNID, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE 
                      FROM MATERIALMASTER WHERE MTRLID = @p0", id).FirstOrDefault();

                if (tab == null)
                {
                    ViewBag.msg = "<div class='alert alert-danger'>Material not found!</div>";
                    tab = new MaterialMaster { DISPSTATUS = 0, MTRLPRFT = 0 };
                }
            }
            else
            {
                tab.DISPSTATUS = 0; // Enabled by default
                tab.MTRLPRFT = 0;
            }

            PopulateDropdowns(tab, id);
            return View(tab);
        }

        private void PopulateDropdowns(MaterialMaster tab, int? id)
        {
            // Status dropdown
            var statusList = new List<SelectListItem>
            {
                new SelectListItem { Text = "Enabled", Value = "0" },
                new SelectListItem { Text = "Disabled", Value = "1" }
            };
            ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", tab.DISPSTATUS.ToString());

            // Initialize dropdowns to avoid null ViewBags on error
            ViewBag.MTRLGID = new SelectList(new List<SelectListItem>(), "Value", "Text");
            ViewBag.UNITID = new SelectList(new List<SelectListItem>(), "Value", "Text");
            ViewBag.HSNID = new SelectList(new List<SelectListItem>(), "Value", "Text");

            try
            {
                // Material Group dropdown
                var groupItems = new List<SelectListItem>
                {
                    new SelectListItem { Text = "-- Select Material Group --", Value = "" }
                };

                if (db.MaterialGroupMasters != null)
                {
                    var groups = db.MaterialGroupMasters
                        .OrderBy(g => g.MTRLGDESC)
                        .ToList();

                    foreach (var g in groups)
                    {
                        string text = g.MTRLGDESC ?? string.Empty;
                        if (g.DISPSTATUS != 0)
                        {
                            text += " (Disabled)";
                        }

                        groupItems.Add(new SelectListItem
                        {
                            Text = text,
                            Value = g.MTRLGID.ToString(),
                            Selected = (id != null && id > 0 && tab.MTRLGID == g.MTRLGID)
                        });
                    }
                }

                string selectedGroupId = (id != null && id > 0 && tab.MTRLGID > 0)
                    ? tab.MTRLGID.ToString()
                    : string.Empty;

                ViewBag.MTRLGID = new SelectList(groupItems, "Value", "Text", selectedGroupId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MaterialMaster PopulateDropdowns - Group error: " + ex.Message);
                ViewBag.MTRLGID = new List<SelectListItem>
                {
                    new SelectListItem { Text = "-- Material Group Master not available --", Value = "" }
                };
            }

            try
            {
                // Unit dropdown
                var unitItems = new List<SelectListItem>
                {
                    new SelectListItem { Text = "-- Select Unit --", Value = "" }
                };

                if (db.UnitMasters != null)
                {
                    var units = db.UnitMasters
                        .OrderBy(u => u.UNITDESC)
                        .ToList();

                    foreach (var u in units)
                    {
                        string text = u.UNITDESC ?? string.Empty;
                        if (u.DISPSTATUS != 0)
                        {
                            text += " (Disabled)";
                        }

                        unitItems.Add(new SelectListItem
                        {
                            Text = text,
                            Value = u.UNITID.ToString(),
                            Selected = (id != null && id > 0 && tab.UNITID == u.UNITID)
                        });
                    }
                }

                string selectedUnitId = (id != null && id > 0 && tab.UNITID > 0)
                    ? tab.UNITID.ToString()
                    : string.Empty;

                ViewBag.UNITID = new SelectList(unitItems, "Value", "Text", selectedUnitId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MaterialMaster PopulateDropdowns - Unit error: " + ex.Message);
                ViewBag.UNITID = new List<SelectListItem>
                {
                    new SelectListItem { Text = "-- Unit Master not available --", Value = "" }
                };
            }

            try
            {
                // HSN dropdown
                var hsnItems = new List<SelectListItem>
                {
                    new SelectListItem { Text = "-- Select HSN Code --", Value = "" }
                };

                if (db.HSNCodeMasters != null)
                {
                    var hsns = db.HSNCodeMasters
                        .OrderBy(h => h.HSNDESC)
                        .ToList();

                    foreach (var h in hsns)
                    {
                        string text = h.HSNDESC ?? string.Empty;
                        if (h.DISPSTATUS != 0)
                        {
                            text += " (Disabled)";
                        }

                        hsnItems.Add(new SelectListItem
                        {
                            Text = text,
                            Value = h.HSNID.ToString(),
                            Selected = (id != null && id > 0 && tab.HSNID == h.HSNID)
                        });
                    }
                }

                string selectedHsnId = (id != null && id > 0 && tab.HSNID > 0)
                    ? tab.HSNID.ToString()
                    : string.Empty;

                ViewBag.HSNID = new SelectList(hsnItems, "Value", "Text", selectedHsnId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MaterialMaster PopulateDropdowns - HSN error: " + ex.Message);
                ViewBag.HSNID = new List<SelectListItem>
                {
                    new SelectListItem { Text = "-- HSN Code Master not available --", Value = "" }
                };
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "MaterialMasterCreate,MaterialMasterEdit")]
        public ActionResult savedata(MaterialMaster tab)
        {
            try
            {
                // Extra validation for dropdowns and profit
                if (tab.MTRLGID <= 0)
                {
                    ModelState.AddModelError("MTRLGID", "Please select material group.");
                }
                if (tab.UNITID <= 0)
                {
                    ModelState.AddModelError("UNITID", "Please select unit.");
                }
                if (tab.HSNID <= 0)
                {
                    ModelState.AddModelError("HSNID", "Please select HSN code.");
                }
                if (tab.MTRLPRFT < 0)
                {
                    ModelState.AddModelError("MTRLPRFT", "Profit cannot be negative.");
                }
                if (tab.RATE < 0)
                {
                    ModelState.AddModelError("RATE", "Rate cannot be negative.");
                }

                if (ModelState.IsValid)
                {
                    // Duplicate code check (case-insensitive)
                    var duplicateCheck = db.Database.SqlQuery<int>(
                        @"SELECT COUNT(*) FROM MATERIALMASTER 
                          WHERE UPPER(MTRLCODE) = @p0 AND MTRLID != @p1",
                        tab.MTRLCODE.ToUpper(), tab.MTRLID
                    ).FirstOrDefault();

                    if (duplicateCheck > 0)
                    {
                        ModelState.AddModelError("MTRLCODE", "This material code is already used.");
                        ViewBag.msg = "<div class='alert alert-danger'>Material code already exists. Please use a different code.</div>";
                    }
                    else
                    {
                        // Current user
                        var currentUserName = Session["CUSRID"]?.ToString();
                        if (string.IsNullOrEmpty(currentUserName))
                        {
                            currentUserName = User.Identity.Name ?? "admin";
                        }

                        var now = DateTime.Now;

                        // Normalize description and code
                        if (!string.IsNullOrEmpty(tab.MTRLDESC))
                        {
                            tab.MTRLDESC = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo
                                .ToTitleCase(tab.MTRLDESC.ToLower());
                        }

                        if (!string.IsNullOrEmpty(tab.MTRLCODE))
                        {
                            tab.MTRLCODE = tab.MTRLCODE.ToUpper();
                        }

                        if (tab.MTRLID > 0)
                        {
                            // Update existing
                            var existing = db.Database.SqlQuery<MaterialMaster>(
                                @"SELECT MTRLID, MTRLGID, MTRLDESC, MTRLCODE, UNITID, MTRLPRFT, RATE, HSNID, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE 
                                  FROM MATERIALMASTER WHERE MTRLID = @p0", tab.MTRLID
                            ).FirstOrDefault();

                            if (existing == null)
                            {
                                ViewBag.msg = "<div class='alert alert-danger'>Material not found!</div>";
                            }
                            else
                            {
                                // Preserve original CUSRID
                                var createdBy = existing.CUSRID;

                                db.Database.ExecuteSqlCommand(
                                    @"UPDATE MATERIALMASTER 
                                      SET MTRLGID = @p0, 
                                          MTRLDESC = @p1, 
                                          MTRLCODE = @p2, 
                                          UNITID = @p3, 
                                          MTRLPRFT = @p4, 
                                          RATE = @p5, 
                                          HSNID = @p6, 
                                          CUSRID = @p7, 
                                          LMUSRID = @p8, 
                                          DISPSTATUS = @p9, 
                                          PRCSDATE = @p10 
                                      WHERE MTRLID = @p11",
                                    tab.MTRLGID,
                                    tab.MTRLDESC,
                                    tab.MTRLCODE,
                                    tab.UNITID,
                                    tab.MTRLPRFT,
                                    tab.RATE,
                                    tab.HSNID,
                                    createdBy,
                                    currentUserName,
                                    tab.DISPSTATUS,
                                    now,
                                    tab.MTRLID
                                );

                                TempData["SuccessMessage"] = "Material updated successfully!";
                                return RedirectToAction("Index");
                            }
                        }
                        else
                        {
                            // Insert new
                            db.Database.ExecuteSqlCommand(
                                @"INSERT INTO MATERIALMASTER 
                                  (MTRLGID, MTRLDESC, MTRLCODE, UNITID, MTRLPRFT, RATE, HSNID, CUSRID, LMUSRID, DISPSTATUS, PRCSDATE) 
                                  VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10)",
                                tab.MTRLGID,
                                tab.MTRLDESC,
                                tab.MTRLCODE,
                                tab.UNITID,
                                tab.MTRLPRFT,
                                tab.RATE,
                                tab.HSNID,
                                currentUserName,
                                currentUserName,
                                tab.DISPSTATUS,
                                now
                            );

                            TempData["SuccessMessage"] = "Material added successfully!";
                            return RedirectToAction("Index");
                        }
                    }
                }
                else
                {
                    ViewBag.msg = "<div class='alert alert-danger'>Please correct the errors and try again.</div>";
                }
            }
            catch (Exception ex)
            {
                ViewBag.msg = "<div class='alert alert-danger'>Error: " + ex.Message + "</div>";
            }

            PopulateDropdowns(tab, tab.MTRLID > 0 ? tab.MTRLID : (int?)null);
            return View("Form", tab);
        }

        // AJAX data for DataTables
        public ActionResult GetAjaxData()
        {
            try
            {
                // Load index data via stored procedure for better performance
                var materials = db.Database.SqlQuery<MaterialMasterIndexRow>(
                    "EXEC SP_MATERIALMASTER_INDEX"
                ).ToList();

                var result = materials.Select(m => new
                {
                    m.MTRLID,
                    MTRLCODE = m.MTRLCODE ?? string.Empty,
                    MTRLDESC = m.MTRLDESC ?? string.Empty,
                    GroupName = m.GroupName ?? "N/A",
                    UnitName = m.UnitName ?? "N/A",
                    HSNName = m.HSNName ?? "N/A",
                    MTRLPRFT = m.MTRLPRFT,
                    DISPSTATUS = m.DISPSTATUS == 0 ? "Enabled" : "Disabled",
                    StatusBadge = m.DISPSTATUS == 0
                        ? "<span class='badge badge-success'>Enabled</span>"
                        : "<span class='badge badge-danger'>Disabled</span>"
                }).ToList();

                return Json(new { data = result }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private string GetMaterialGroupName(int mtrlgid)
        {
            try
            {
                var result = db.Database.SqlQuery<string>(
                    "SELECT MTRLGDESC FROM MATERIALGROUPMASTER WHERE MTRLGID = @p0", mtrlgid
                ).FirstOrDefault();

                return result ?? "N/A";
            }
            catch
            {
                return "N/A";
            }
        }

        private string GetUnitName(int unitid)
        {
            try
            {
                var result = db.Database.SqlQuery<string>(
                    "SELECT UNITDESC FROM UNITMASTER WHERE UNITID = @p0", unitid
                ).FirstOrDefault();

                return result ?? "N/A";
            }
            catch
            {
                return "N/A";
            }
        }

        private string GetHSNCodeName(int hsnid)
        {
            try
            {
                var result = db.Database.SqlQuery<string>(
                    "SELECT HSNDESC FROM HSNCODEMASTER WHERE HSNID = @p0", hsnid
                ).FirstOrDefault();

                return result ?? "N/A";
            }
            catch
            {
                return "N/A";
            }
        }

        // Remote validation for unique material code
        [HttpPost]
        public JsonResult ValidateMTRLCODE(string MTRLCODE, int MTRLID = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(MTRLCODE))
                {
                    return Json(true, JsonRequestBehavior.AllowGet);
                }

                var exists = db.Database.SqlQuery<int>(
                    @"SELECT COUNT(*) FROM MATERIALMASTER 
                      WHERE UPPER(MTRLCODE) = @p0 AND MTRLID != @p1",
                    MTRLCODE.ToUpper(), MTRLID
                ).FirstOrDefault();

                return Json(exists == 0 ? (object)true : "This material code is already used.", JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ValidateMTRLCODE error: " + ex.Message);
                return Json(true, JsonRequestBehavior.AllowGet);
            }
        }

        // Delete method for AJAX calls
        [HttpPost]
        public ActionResult Del(int id)
        {
            try
            {
                if (!User.IsInRole("MaterialMasterDelete"))
                {
                    return Json("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }

                // Check if material exists without mapping to full MaterialMaster (avoids data reader column mismatch)
                var materialCount = db.Database.SqlQuery<int>(
                    "SELECT COUNT(1) FROM MATERIALMASTER WHERE MTRLID = @p0", id
                ).FirstOrDefault();

                if (materialCount <= 0)
                {
                    return Json("Material not found");
                }

                db.Database.ExecuteSqlCommand("DELETE FROM MATERIALMASTER WHERE MTRLID = @p0", id);
                return Json("Successfully deleted");
            }
            catch (Exception ex)
            {
                return Json("Error: " + ex.Message);
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
