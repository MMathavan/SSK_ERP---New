using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Data.Entity.Validation;
using System.Diagnostics;
using SSK_ERP.Models;
using SSK_ERP.Data;
using System.Configuration;
using SSK_ERP;

namespace SSK_ERP.Controllers.Masters
{
    [SSK_ERP.SessionExpire]
    public class StateMasterController : Controller
    {
        // GET: StateMaster
        ApplicationDbContext context = new ApplicationDbContext();

        [Authorize(Roles = "StateMasterIndex")]
        public ActionResult Index()
        {
            return View(context.StateMasters.ToList());//Loading Grid
        }
        public JsonResult GetAjaxData(JQueryDataTableParamModel param)
        {
            using (var e = new SSK_ERPEntities())
            {
                var totalRowsCount = new System.Data.Entity.Core.Objects.ObjectParameter("TotalRowsCount", typeof(int));
                var filteredRowsCount = new System.Data.Entity.Core.Objects.ObjectParameter("FilteredRowsCount", typeof(int));
                var data = e.pr_SearchStateMaster(param.sSearch,
                                                Convert.ToInt32(Request["iSortCol_0"]),
                                                Request["sSortDir_0"],
                                                param.iDisplayStart,
                                                param.iDisplayStart + param.iDisplayLength,
                                                totalRowsCount,
                                                filteredRowsCount).ToList();

                // Build Region lookup so we can show Region name in the grid
                var stateIds = data
                    .Where(d => d.STATEID.HasValue)
                    .Select(d => d.STATEID.Value)
                    .Distinct()
                    .ToList();

                var regionJoin = context.StateMasters
                    .Where(s => stateIds.Contains(s.STATEID))
                    .Join(context.RegionMasters,
                          s => s.SREGNID,
                          r => r.REGNID,
                          (s, r) => new { s.STATEID, r.REGNDESC })
                    .ToList();

                var regionLookup = regionJoin
                    .GroupBy(x => x.STATEID)
                    .ToDictionary(
                        g => g.Key,
                        g => g.FirstOrDefault() != null ? g.FirstOrDefault().REGNDESC : string.Empty
                    );

                //var aaData = data.Select(d => new string[] { d.STATECODE, d.STATEDESC, d.STATETYPE, d.DISPSTATUS, d.STATEID.ToString() }).ToArray();
                var aaData = data.Select(d => new
                {
                    STATECODE = d.STATECODE,
                    STATEDESC = d.STATEDESC,
                    REGION = d.STATEID.HasValue && regionLookup.ContainsKey(d.STATEID.Value)
                                ? regionLookup[d.STATEID.Value]
                                : string.Empty,
                    STATETYPE = d.STATETYPE ?? "0",
                    DISPSTATUS = d.DISPSTATUS,
                    STATEID = d.STATEID.HasValue ? d.STATEID.Value.ToString() : "0"
                }).ToArray();
                return Json(new
                {
                    //sEcho = param.sEcho,
                    data = aaData
                    //iTotalRecords = Convert.ToInt32(totalRowsCount.Value),
                    //iTotalDisplayRecords = Convert.ToInt32(filteredRowsCount.Value)
                }, JsonRequestBehavior.AllowGet);
            }
        }
        //----------------------Initializing Form--------------------------//
        [Authorize(Roles = "StateMasterCreate")]
        public ActionResult Form(int? id = 0)
        {
            StateMaster tab = new StateMaster();
            tab.STATEID = 0;
            tab.STATETYPE = 0;

            // Define dropdown lists at method level so they're accessible everywhere
            var statusList = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Enabled" },
                new SelectListItem { Value = "1", Text = "Disabled" }
            };

            var stateTypeList = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Local" },
                new SelectListItem { Value = "1", Text = "InterState" }
            };

            ViewBag.REGNID = new SelectList(context.RegionMasters, "REGNID", "REGNDESC");

            // IMP
            if (id == -1)
                ViewBag.msg = "<div class='msg'>Record Successfully Saved</div>";
            
            if (id != null && id > 0)  // Edit mode (same condition as CustomerMaster fix)
            {
                tab = context.StateMasters.Find(id);

                // CRITICAL: Clear ModelState for both dropdowns to ensure proper selection in edit mode
                ModelState.Remove("DISPSTATUS");
                ModelState.Remove("STATETYPE");

                // Debug: Show what values are being read from database
                System.Diagnostics.Debug.WriteLine($"StateMaster Edit - Raw values from DB:");
                System.Diagnostics.Debug.WriteLine($"  DISPSTATUS: '{tab.DISPSTATUS}' (Type: {tab.DISPSTATUS.GetType()})");
                System.Diagnostics.Debug.WriteLine($"  STATETYPE: '{tab.STATETYPE}' (Type: {tab.STATETYPE.GetType()})");
                
                // Status dropdown selection
                var selectedStatusValue = tab.DISPSTATUS.ToString();
                var selectedStateTypeValue = tab.STATETYPE.ToString();
                
                // Handle case where STATETYPE might be stored as text instead of numeric
                if (selectedStateTypeValue == "Local" || selectedStateTypeValue == "local")
                    selectedStateTypeValue = "0";
                else if (selectedStateTypeValue == "InterState" || selectedStateTypeValue == "Interstate" || selectedStateTypeValue == "Inter State")
                    selectedStateTypeValue = "1";
                else if (string.IsNullOrEmpty(selectedStateTypeValue) || selectedStateTypeValue == "0")
                    selectedStateTypeValue = "0"; // Default to Local
                else if (selectedStateTypeValue == "1")
                    selectedStateTypeValue = "1"; // Keep as Interstate
                
                System.Diagnostics.Debug.WriteLine($"  Selected values: DISPSTATUS='{selectedStatusValue}', STATETYPE='{selectedStateTypeValue}' (Original: {tab.STATETYPE})");

                // Ensure model property reflects the selected value so DropDownListFor can bind correctly
                short parsedStateType;
                if (short.TryParse(selectedStateTypeValue, out parsedStateType))
                {
                    tab.STATETYPE = parsedStateType;
                }

                // Create dropdowns using SelectList for proper selection
                ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", selectedStatusValue);
                ViewBag.STATETYPE = new SelectList(stateTypeList, "Value", "Text", selectedStateTypeValue);
                
                // Debug: Show what values are being selected
                System.Diagnostics.Debug.WriteLine($"Dropdowns - DISPSTATUS: {selectedStatusValue}, STATETYPE: {selectedStateTypeValue}");
            }
            else  // New record mode
            {
                // Set dropdowns with default values
                ViewBag.DISPSTATUS = new SelectList(statusList, "Value", "Text", "0");
                ViewBag.STATETYPE = new SelectList(stateTypeList, "Value", "Text", "0");
            }
            return View(tab);
        }//End of Form

        //--------------------------Insert or Modify data------------------------//
        [HttpPost]
        [Authorize(Roles = "StateMasterCreate,StateMasterEdit")]
        public void savedata(StateMaster tab)
        {
            tab.CUSRID = Session["CUSRID"].ToString();
            tab.LMUSRID = 1;
            tab.PRCSDATE = DateTime.Now;
            var s = tab.STATEDESC;//...ProperCase
            s = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(s.ToLower());//end
            tab.STATEDESC = s;
            if ((tab.STATEID).ToString() != "0")
            {
                context.Entry(tab).State = System.Data.Entity.EntityState.Modified;
                context.SaveChanges();
            }
            else
            {
                context.StateMasters.Add(tab);
                context.SaveChanges();
            }

            // IMP
            if (Request.Form.Get("continue") == null)
            {
                Response.Redirect("index");
            }
            else
            {
                Response.Redirect("Form/-1");
            }
        }

        //------------------------Delete Record----------//
        [HttpPost]
        public ActionResult deletedata(int id)
        {
            try
            {
                // Check if user has delete role
                if (!User.IsInRole("StateMasterDelete"))
                {
                    Response.StatusCode = 403;
                    return Content("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }
                
                context.Database.ExecuteSqlCommand("DELETE FROM STATEMASTER WHERE STATEID = {0}", id);
                return Content("Deleted Successfully ...");
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Content("Error deleting state: " + ex.Message);
            }
        }

    }
}
