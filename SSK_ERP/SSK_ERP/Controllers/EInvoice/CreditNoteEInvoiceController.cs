using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSK_ERP.Models;
using static SSK_ERP.Models.EInvoice;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class CreditNoteEInvoiceController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int CreditNoteRegisterId = 23;

        private class PortalUploadResult
        {
            public string Message { get; set; }
            public string RequestJson { get; set; }
            public string ResponseJson { get; set; }
            public int PortalHttpStatus { get; set; }
            public string PortalHttpReason { get; set; }
        }

        private static bool HasColumn(IDataRecord reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetString(IDataRecord reader, string columnName, string defaultValue = "")
        {
            if (!HasColumn(reader, columnName))
            {
                return defaultValue;
            }

            var value = reader[columnName];
            if (value == null || value == DBNull.Value)
            {
                return defaultValue;
            }

            return Convert.ToString(value);
        }

        private static int GetInt32(IDataRecord reader, string columnName, int defaultValue = 0)
        {
            if (!HasColumn(reader, columnName))
            {
                return defaultValue;
            }

            var value = reader[columnName];
            if (value == null || value == DBNull.Value)
            {
                return defaultValue;
            }

            return Convert.ToInt32(value);
        }

        private static decimal GetDecimal(IDataRecord reader, string columnName, decimal defaultValue = 0)
        {
            if (!HasColumn(reader, columnName))
            {
                return defaultValue;
            }

            var value = reader[columnName];
            if (value == null || value == DBNull.Value)
            {
                return defaultValue;
            }

            return Convert.ToDecimal(value);
        }

        private static string NormalizeGstStateCode(string stateCodeOrAbbr)
        {
            if (string.IsNullOrWhiteSpace(stateCodeOrAbbr))
            {
                return string.Empty;
            }

            var raw = stateCodeOrAbbr.Trim();

            if (raw.All(char.IsDigit))
            {
                return raw.PadLeft(2, '0');
            }

            var abbr = raw.ToUpperInvariant();
            switch (abbr)
            {
                case "TN": return "33";
                case "PY": return "34";
                case "KA": return "29";
                case "KL": return "32";
                case "AP": return "37";
                case "TS": return "36";
                default: return string.Empty;
            }
        }

        [Authorize(Roles = "CreditNoteEInvoiceIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "CreditNoteEInvoiceIndex")]
        public JsonResult GetAjaxData(JQueryDataTableParamModel param, string fromDate = null, string toDate = null)
        {
            try
            {
                DateTime? fd = null;
                if (!string.IsNullOrWhiteSpace(fromDate))
                {
                    DateTime parsed;
                    if (DateTime.TryParse(fromDate, out parsed))
                    {
                        fd = parsed.Date;
                    }
                }

                DateTime? exclusiveTo = null;
                if (!string.IsNullOrWhiteSpace(toDate))
                {
                    DateTime parsed;
                    if (DateTime.TryParse(toDate, out parsed))
                    {
                        exclusiveTo = parsed.Date.AddDays(1);
                    }
                }

                var query = db.TransactionMasters.Where(t => t.REGSTRID == CreditNoteRegisterId);

                if (fd.HasValue)
                {
                    query = query.Where(t => t.TRANDATE >= fd.Value);
                }

                if (exclusiveTo.HasValue)
                {
                    query = query.Where(t => t.TRANDATE < exclusiveTo.Value);
                }

                var masters = query
                    .OrderByDescending(t => t.TRANDATE)
                    .ThenByDescending(t => t.TRANMID)
                    .ToList();

                var data = masters
                    .Select(t => new
                    {
                        t.TRANMID,
                        t.TRANDATE,
                        t.TRANNO,
                        TRANDNO = t.TRANDNO ?? "0000",
                        TRANREFNO = !string.IsNullOrWhiteSpace(t.TRANTAXBILLNO)
                            ? t.TRANTAXBILLNO
                            : (t.TRANREFNO ?? "-"),
                        CustomerName = t.TRANREFNAME ?? string.Empty,
                        Amount = t.TRANNAMT,
                        AckNo = t.ACKNO ?? string.Empty,
                        Status = t.DISPSTATUS == 0 ? "Enabled" : "Disabled"
                    })
                    .ToList();

                return Json(new { data }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [Authorize(Roles = "CreditNoteEInvoiceUpload")]
        public async Task<ActionResult> Upload(int id)
        {
            var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == CreditNoteRegisterId);
            if (master != null && !string.IsNullOrWhiteSpace(master.ACKNO))
            {
                TempData["ErrorMessage"] = "Acknowledge Number already present. Upload is not allowed.";
                return RedirectToAction("Index");
            }

            var result = await UploadCreditNoteEInvoiceAsync(id);
            if (string.Equals(result.Message, "Uploaded Succesfully", StringComparison.OrdinalIgnoreCase))
            {
                TempData["SuccessMessage"] = "Credit Note Uploaded Successfully !!";
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction("Index");
        }

        [Authorize(Roles = "CreditNoteEInvoicePrint")]
        public ActionResult Print(int id)
        {
            var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == CreditNoteRegisterId);
            if (master == null)
            {
                TempData["ErrorMessage"] = "Credit Note not found.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(master.ACKNO))
            {
                TempData["ErrorMessage"] = "Acknowledge Number is not updated. Print is allowed only after upload.";
                return RedirectToAction("Index");
            }

            ViewBag.IrnNo = master.IRNNO;
            ViewBag.AckNo = master.ACKNO;
            ViewBag.AckDate = master.ACKDT;

            var qrPath = master.QRCODEPATH;
            if (!string.IsNullOrWhiteSpace(qrPath))
            {
                qrPath = qrPath.Trim();
                var isPhysicalFilePath = qrPath.Contains(":\\") || qrPath.Contains(":/");
                if (!qrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !qrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    && !qrPath.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    && !qrPath.StartsWith("~")
                    && !isPhysicalFilePath)
                {
                    qrPath = qrPath.StartsWith("/")
                        ? ("https://my.gstzen.in" + qrPath)
                        : ("https://" + qrPath);
                }
            }
            ViewBag.QrCodePath = qrPath;

            var salesReturnController = new SalesReturnController();
            var result = salesReturnController.Print(id) as ViewResult;
            if (result != null)
            {
                return View("~/Views/CreditNoteEInvoice/Print.cshtml", result.Model);
            }

            TempData["ErrorMessage"] = "Credit Note EInvoice print data could not be loaded.";
            return RedirectToAction("Index");
        }

        public async Task<ActionResult> CInvoice(int id = 0)/*10rs.reminder*/
        {
            var showJson = string.Equals(Request.QueryString["showjson"], "1", StringComparison.OrdinalIgnoreCase);
            var master = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == CreditNoteRegisterId);
            if (master != null && !string.IsNullOrWhiteSpace(master.ACKNO))
            {
                var duplicateMsg = "Acknowledge Number already present. Upload is not allowed.";
                if (showJson)
                {
                    return Content(JsonConvert.SerializeObject(new
                    {
                        message = duplicateMsg,
                        requestJson = "",
                        responseJson = "",
                        portalHttpStatus = 409,
                        portalHttpReason = "Conflict"
                    }), "application/json");
                }

                return Content(duplicateMsg);
            }

            var uploadResult = await UploadCreditNoteEInvoiceAsync(id);
            if (showJson)
            {
                return Content(JsonConvert.SerializeObject(new
                {
                    message = uploadResult.Message,
                    requestJson = uploadResult.RequestJson,
                    responseJson = uploadResult.ResponseJson,
                    portalHttpStatus = uploadResult.PortalHttpStatus,
                    portalHttpReason = uploadResult.PortalHttpReason
                }), "application/json");
            }

            return Content(uploadResult.Message);
        }

        public ActionResult Json(int id = 0)
        {

            SqlDataReader reader = null;
            //SqlDataReader Sreader = null;
            var connSettings = ConfigurationManager.ConnectionStrings["SCFSERP"]
                ?? ConfigurationManager.ConnectionStrings["DefaultConnection"]
                ?? ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"];
            string _connStr = connSettings.ConnectionString;
            SqlConnection myConnection = new SqlConnection(_connStr);

            string _SconnStr = connSettings.ConnectionString;
            SqlConnection SmyConnection = new SqlConnection(_SconnStr);

            var tranmid = id;// Convert.ToInt32(Request.Form.Get("id"));// Convert.ToInt32(ids);

            SqlCommand sqlCmd = new SqlCommand();
            sqlCmd.CommandType = CommandType.Text;
            sqlCmd.CommandText = "Select * from Z_CREDITNOTE_EINVOICE_DETAILS Where TRANMID = " + tranmid;
            sqlCmd.Connection = myConnection;
            myConnection.Open();
            reader = sqlCmd.ExecuteReader();

            string stringjson = "";

            decimal strgamt = 0;
            decimal strg_cgst_amt = 0;
            decimal strg_sgst_amt = 0;
            decimal strg_igst_amt = 0;

            decimal handlamt = 0;
            decimal handl_cgst_amt = 0;
            decimal handl_sgst_amt = 0;
            decimal handl_igst_amt = 0;

            decimal cgst_amt = 0;
            decimal sgst_amt = 0;
            decimal igst_amt = 0;


            while (reader.Read())
            {
                strgamt = GetDecimal(reader, "STRG_TAXABLE_AMT");
                strg_cgst_amt = GetDecimal(reader, "STRG_CGST_AMT");
                strg_sgst_amt = GetDecimal(reader, "STRG_SGST_AMT");
                strg_igst_amt = GetDecimal(reader, "STRG_IGST_AMT");

                handlamt = GetDecimal(reader, "HANDL_TAXABLE_AMT");
                handl_cgst_amt = GetDecimal(reader, "HANDL_CGST_AMT");
                handl_sgst_amt = GetDecimal(reader, "HANDL_SGST_AMT");
                handl_igst_amt = GetDecimal(reader, "HANDL_IGST_AMT");

                cgst_amt = GetDecimal(reader, "CGST_AMT", GetDecimal(reader, "TRANCGSTAMT"));
                sgst_amt = GetDecimal(reader, "SGST_AMT", GetDecimal(reader, "TRANSGSTAMT"));
                igst_amt = GetDecimal(reader, "IGST_AMT", GetDecimal(reader, "TRANIGSTAMT"));

                var taxableAmount = strgamt + handlamt;
                if (taxableAmount == 0)
                {
                    taxableAmount = GetDecimal(reader, "TRANGAMT");
                }

                var response = new Response()
                {
                    Version = "1.1",

                    TranDtls = new TranDtls()
                    {
                        TaxSch = "GST",
                        SupTyp = "B2B",
                        RegRev = "N",
                        EcmGstin = null,
                        IgstOnIntra = "N"
                    },

                    DocDtls = new DocDtls()
                    {
                        Typ = "CRN",
                        No = GetString(reader, "TRANBILLREFNO", GetString(reader, "TRANDNO")),
                        Dt = Convert.ToDateTime(reader["TRANDATE"]).Date.ToString("dd'/'MM'/'yyyy")
                    },

                    SellerDtls = new SellerDtls()
                    {
                        Gstin = GetString(reader, "COMPGSTNO"),
                        LglNm = GetString(reader, "COMPNAME"),
                        Addr1 = GetString(reader, "COMPADDR1"),
                        Addr2 = GetString(reader, "COMPADDR2"),
                        Loc = GetString(reader, "COMPLOCTDESC"),
                        Pin = GetInt32(reader, "COMPPINCODE"),
                        Stcd = GetString(reader, "COMPSTATECODE"),
                        Ph = GetString(reader, "COMPPHN1"),
                        Em = GetString(reader, "COMPMAIL")
                    },

                    BuyerDtls = new BuyerDtls()
                    {
                        Gstin = GetString(reader, "CATEBGSTNO", GetString(reader, "CATE_GST_NO")),
                        LglNm = GetString(reader, "TRANREFNAME"),
                        Pos = NormalizeGstStateCode(GetString(reader, "STATECODE")),
                        Addr1 = GetString(reader, "TRANIMPADDR1", GetString(reader, "CATEADDR1")),
                        Addr2 = GetString(reader, "TRANIMPADDR2", GetString(reader, "CATEADDR2")),
                        Loc = GetString(reader, "TRANIMPADDR3", GetString(reader, "LOCTDESC")),
                        Pin = GetInt32(reader, "TRANIMPADDR4", GetInt32(reader, "CATEADDR5")),
                        Stcd = NormalizeGstStateCode(GetString(reader, "STATECODE")),
                        Ph = GetString(reader, "CATEPHN1", GetString(reader, "CATEPHN3")),
                        Em = null// reader["CATEMAIL"].ToString()
                    },

                    ValDtls = new ValDtls()
                    {
                        AssVal = taxableAmount,// Convert.ToDecimal(reader["HANDL_TAXABLE_AMT"]),
                        CesVal = 0,
                        CgstVal = cgst_amt,// Convert.ToDecimal(reader["HANDL_CGST_AMT"]),
                        IgstVal = igst_amt,// Convert.ToDecimal(reader["HANDL_IGST_AMT"]),
                        OthChrg = 0,
                        SgstVal = sgst_amt,// Convert.ToDecimal(reader["HANDL_sGST_AMT"]),
                        Discount = 0,
                        StCesVal = 0,
                        RndOffAmt = 0,
                        TotInvVal = GetDecimal(reader, "TRANNAMT"),
                        TotItemValSum = taxableAmount,//Convert.ToDecimal(reader["TOTALITEMVAL"])
                    },

                    ItemList = GetItemList(tranmid),


                };

                stringjson = JsonConvert.SerializeObject(response);
            }

            SmyConnection.Close();
            myConnection.Close();

            return Content(stringjson);

        }

        private string BuildCreditNoteEInvoiceJson(int id, out string connStr)
        {
            SqlDataReader reader = null;
            var connSettings = ConfigurationManager.ConnectionStrings["SCFSERP"]
                ?? ConfigurationManager.ConnectionStrings["DefaultConnection"]
                ?? ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"];
            connStr = connSettings.ConnectionString;
            SqlConnection myConnection = new SqlConnection(connStr);

            var tranmid = id;

            SqlCommand sqlCmd = new SqlCommand();
            sqlCmd.CommandType = CommandType.Text;
            sqlCmd.CommandText = "Select * from Z_CREDITNOTE_EINVOICE_DETAILS Where TRANMID = " + tranmid;
            sqlCmd.Connection = myConnection;
            myConnection.Open();
            reader = sqlCmd.ExecuteReader();

            string stringjson = "";

            decimal strgamt = 0;
            decimal strg_cgst_amt = 0;
            decimal strg_sgst_amt = 0;
            decimal strg_igst_amt = 0;

            decimal handlamt = 0;
            decimal handl_cgst_amt = 0;
            decimal handl_sgst_amt = 0;
            decimal handl_igst_amt = 0;

            decimal cgst_amt = 0;
            decimal sgst_amt = 0;
            decimal igst_amt = 0;

            while (reader.Read())
            {
                strgamt = GetDecimal(reader, "STRG_TAXABLE_AMT");
                strg_cgst_amt = GetDecimal(reader, "STRG_CGST_AMT");
                strg_sgst_amt = GetDecimal(reader, "STRG_SGST_AMT");
                strg_igst_amt = GetDecimal(reader, "STRG_IGST_AMT");

                handlamt = GetDecimal(reader, "HANDL_TAXABLE_AMT");
                handl_cgst_amt = GetDecimal(reader, "HANDL_CGST_AMT");
                handl_sgst_amt = GetDecimal(reader, "HANDL_SGST_AMT");
                handl_igst_amt = GetDecimal(reader, "HANDL_IGST_AMT");

                cgst_amt = GetDecimal(reader, "CGST_AMT", GetDecimal(reader, "TRANCGSTAMT"));
                sgst_amt = GetDecimal(reader, "SGST_AMT", GetDecimal(reader, "TRANSGSTAMT"));
                igst_amt = GetDecimal(reader, "IGST_AMT", GetDecimal(reader, "TRANIGSTAMT"));

                var taxableAmount = strgamt + handlamt;
                if (taxableAmount == 0)
                {
                    taxableAmount = GetDecimal(reader, "TRANGAMT");
                }

                var response = new Response()
                {
                    Version = "1.1",

                    TranDtls = new TranDtls()
                    {
                        TaxSch = "GST",
                        SupTyp = "B2B",
                        RegRev = "N",
                        EcmGstin = null,
                        IgstOnIntra = "N"
                    },

                    DocDtls = new DocDtls()
                    {
                        Typ = "CRN",
                        No = GetString(reader, "TRANBILLREFNO", GetString(reader, "TRANDNO")),
                        Dt = Convert.ToDateTime(reader["TRANDATE"]).Date.ToString("dd'/'MM'/'yyyy")
                    },

                    SellerDtls = new SellerDtls()
                    {
                        Gstin = GetString(reader, "COMPGSTNO"),
                        LglNm = GetString(reader, "COMPNAME"),
                        Addr1 = GetString(reader, "COMPADDR1"),
                        Addr2 = GetString(reader, "COMPADDR2"),
                        Loc = GetString(reader, "COMPLOCTDESC"),
                        Pin = GetInt32(reader, "COMPPINCODE"),
                        Stcd = GetString(reader, "COMPSTATECODE"),
                        Ph = GetString(reader, "COMPPHN1"),
                        Em = GetString(reader, "COMPMAIL")
                    },

                    BuyerDtls = new BuyerDtls()
                    {
                        Gstin = GetString(reader, "CATEBGSTNO", GetString(reader, "CATE_GST_NO")),
                        LglNm = GetString(reader, "TRANREFNAME"),
                        Pos = NormalizeGstStateCode(GetString(reader, "STATECODE")),
                        Addr1 = GetString(reader, "TRANIMPADDR1", GetString(reader, "CATEADDR1")),
                        Addr2 = GetString(reader, "TRANIMPADDR2", GetString(reader, "CATEADDR2")),
                        Loc = GetString(reader, "TRANIMPADDR3", GetString(reader, "LOCTDESC")),
                        Pin = GetInt32(reader, "TRANIMPADDR4", GetInt32(reader, "CATEADDR5")),
                        Stcd = NormalizeGstStateCode(GetString(reader, "STATECODE")),
                        Ph = GetString(reader, "CATEPHN1", GetString(reader, "CATEPHN3")),
                        Em = null
                    },

                    ValDtls = new ValDtls()
                    {
                        AssVal = taxableAmount,
                        CesVal = 0,
                        CgstVal = cgst_amt,
                        IgstVal = igst_amt,
                        OthChrg = 0,
                        SgstVal = sgst_amt,
                        Discount = 0,
                        StCesVal = 0,
                        RndOffAmt = 0,
                        TotInvVal = GetDecimal(reader, "TRANNAMT"),
                        TotItemValSum = taxableAmount,
                    },

                    ItemList = GetItemList(tranmid),
                };

                stringjson = JsonConvert.SerializeObject(response);
            }

            myConnection.Close();
            return stringjson;
        }

        private async Task<PortalUploadResult> UploadCreditNoteEInvoiceAsync(int id)
        {
            string connStr;
            var stringjson = BuildCreditNoteEInvoiceJson(id, out connStr);
            var result = new PortalUploadResult
            {
                RequestJson = stringjson,
                ResponseJson = "",
                PortalHttpStatus = 0,
                PortalHttpReason = ""
            };

            if (string.IsNullOrWhiteSpace(stringjson))
            {
                result.Message = "EInvoice JSON not generated. Source data not found for this credit note. TRANMID=" + id + ".";
                return result;
            }

            string msg = "";
            using (var httpClient = new HttpClient())
            {
                var tokenFromConfig = (ConfigurationManager.AppSettings["GSTZEN_TOKEN"] ?? string.Empty).Trim();
                var userIdFromConfig = (ConfigurationManager.AppSettings["GSTZEN_USERID"] ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(tokenFromConfig))
                {
                    result.Message = "GSTZen token is not configured. Please set appSettings key GSTZEN_TOKEN in Web.config.";
                    return result;
                }

                var candidateUserIds = new[] { userIdFromConfig, "API_SSK_ERP", "dinesh@fusiontec.com" }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (candidateUserIds.Count == 0)
                {
                    candidateUserIds.Add(string.Empty);
                }

                JObject data = null;
                string portalResponseRaw = "";

                foreach (var currentUserId in candidateUserIds)
                {
                    using (var request = new HttpRequestMessage(new HttpMethod("POST"), "https://my.gstzen.in/~gstzen/a/post-einvoice-data/einvoice-json/"))
                    {
                        request.Headers.TryAddWithoutValidation("Token", tokenFromConfig);
                        if (!string.IsNullOrWhiteSpace(currentUserId))
                        {
                            request.Headers.TryAddWithoutValidation("UserId", currentUserId);
                            request.Headers.TryAddWithoutValidation("username", currentUserId);
                        }

                        request.Content = new StringContent(stringjson, System.Text.Encoding.UTF8, "application/json");

                        var response = await httpClient.SendAsync(request);
                        if (response == null)
                        {
                            continue;
                        }

                        result.PortalHttpStatus = (int)response.StatusCode;
                        result.PortalHttpReason = response.ReasonPhrase;
                        portalResponseRaw = await response.Content.ReadAsStringAsync();
                        result.ResponseJson = portalResponseRaw;
                        try
                        {
                            data = (JObject)JsonConvert.DeserializeObject(portalResponseRaw);
                        }
                        catch
                        {
                            data = null;
                        }

                        var shouldRetryWithAnotherUserId = false;
                        try
                        {
                            var statusAlt = data != null && data["Status"] != null ? data["Status"].Value<int>() : -1;
                            if (statusAlt == 0 && data["ErrorDetails"] != null && data["ErrorDetails"].Type == JTokenType.Array)
                            {
                                var firstErr = data["ErrorDetails"].First;
                                var errCode = firstErr != null && firstErr["ErrorCode"] != null ? firstErr["ErrorCode"].Value<string>() : string.Empty;
                                shouldRetryWithAnotherUserId = string.Equals(errCode, "1017", StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(currentUserId, candidateUserIds.Last(), StringComparison.OrdinalIgnoreCase);
                            }
                        }
                        catch
                        {
                            shouldRetryWithAnotherUserId = false;
                        }

                        if (!shouldRetryWithAnotherUserId)
                        {
                            break;
                        }
                    }
                }

                if (data == null)
                {
                    result.Message = string.IsNullOrWhiteSpace(portalResponseRaw) ? "Empty response from portal." : portalResponseRaw;
                    return result;
                }

                var status = data["status"] != null ? data["status"].Value<int>() : 0;
                msg = data["message"] != null ? data["message"].Value<string>() : "";

                if (status == 0 && data["Status"] != null && data["ErrorDetails"] != null && data["ErrorDetails"].Type == JTokenType.Array)
                {
                    var firstErr = data["ErrorDetails"].First;
                    if (firstErr != null && firstErr["ErrorMessage"] != null)
                    {
                        msg = firstErr["ErrorMessage"].Value<string>();
                    }
                }

                if (status == 1)
                {
                    var zirnno = data["Irn"] != null ? data["Irn"].Value<string>() : "";
                    var zackdt = data["AckDt"] != null ? data["AckDt"].Value<string>() : "";
                    var zackno = data["AckNo"] != null ? data["AckNo"].Value<string>() : "";
                    var imgUrl = data["SignedQrCodeImgUrl"] != null ? data["SignedQrCodeImgUrl"].Value<string>() : "";

                    var imageFileUrl = "";
                    var newimageurl = "";

                    if (imgUrl != "")
                    {
                        imageFileUrl = imgUrl;
                        newimageurl = "https://my.gstzen.in" + imageFileUrl;
                    }

                    using (SqlConnection GmyConnection = new SqlConnection(connStr))
                    using (SqlCommand cmd = new SqlCommand("pr_IRN_Transaction_Update_Assgn_N01", GmyConnection))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@PTranMID", id);
                        cmd.Parameters.AddWithValue("@PIRNNO", zirnno);
                        cmd.Parameters.AddWithValue("@PACKNO", zackno);
                        DateTime ackDtParsed;
                        if (!DateTime.TryParse(zackdt, out ackDtParsed))
                        {
                            ackDtParsed = DateTime.Now;
                        }
                        cmd.Parameters.AddWithValue("@PACKDT", ackDtParsed);
                        cmd.Parameters.AddWithValue("@PCUSRID", Session["CUSRID"].ToString());
                        cmd.Parameters.AddWithValue("@PSignedQRCode", imageFileUrl);
                        cmd.Parameters.AddWithValue("@PSignedQRCodeURL", newimageurl);
                        GmyConnection.Open();
                        cmd.ExecuteNonQuery();
                    }

                    string localFileName = id.ToString() + ".png";
                    string path = Server.MapPath("~/QrCode");
                    try
                    {
                        if (!System.IO.Directory.Exists(path))
                        {
                            System.IO.Directory.CreateDirectory(path);
                        }

                        if (!string.IsNullOrWhiteSpace(newimageurl))
                        {
                            using (WebClient webClient = new WebClient())
                            {
                                webClient.DownloadFile(newimageurl, path + "\\" + localFileName);
                            }
                        }
                    }
                    catch
                    {
                    }

                    using (SqlConnection XmyConnection = new SqlConnection(connStr))
                    using (SqlCommand Xcmd = new SqlCommand("pr_Transaction_QrCode_Path_Update_Assgn", XmyConnection))
                    {
                        Xcmd.CommandType = CommandType.StoredProcedure;
                        Xcmd.Parameters.AddWithValue("@PTranMID", id);
                        Xcmd.Parameters.AddWithValue("@PPath", path + "\\" + localFileName);
                        XmyConnection.Open();
                        Xcmd.ExecuteNonQuery();
                    }

                    msg = "Uploaded Succesfully";
                }
            }

            result.Message = msg;
            return result;
        }

        private List<ItemList> GetItemList(int id)
        {
            SqlDataReader reader = null;
            var connSettings = ConfigurationManager.ConnectionStrings["SCFSERP"]
                ?? ConfigurationManager.ConnectionStrings["DefaultConnection"]
                ?? ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"];
            string _connStr = connSettings.ConnectionString;
            SqlConnection myConnection = new SqlConnection(_connStr);

            SqlCommand sqlCmd = new SqlCommand("pr_EInvoice_CreditNote_Transaction_Detail_Assgn", myConnection);
            sqlCmd.CommandType = CommandType.StoredProcedure;
            sqlCmd.Parameters.AddWithValue("@PTranMID", id);
            sqlCmd.Connection = myConnection;
            myConnection.Open();
            reader = sqlCmd.ExecuteReader();

            List<ItemList> ItemList = new List<ItemList>();

            while (reader.Read())
            {

                ItemList.Add(new ItemList
                {
                    SlNo = 1,
                    PrdDesc = GetString(reader, "PrdDesc"),
                    IsServc = "Y",
                    HsnCd = GetString(reader, "HsnCd"),
                    Barcde = "123456",
                    Qty = 1,
                    FreeQty = 0,
                    Unit = GetString(reader, "UnitCode"),
                    UnitPrice = GetDecimal(reader, "UnitPrice"),
                    TotAmt = GetDecimal(reader, "TotAmt"),
                    Discount = 0,
                    PreTaxVal = 1,
                    AssAmt = GetDecimal(reader, "AssAmt"),
                    GstRt = GetDecimal(reader, "GstRt"),
                    IgstAmt = GetDecimal(reader, "IgstAmt"),
                    CgstAmt = GetDecimal(reader, "CgstAmt"),
                    SgstAmt = GetDecimal(reader, "SgstAmt"),
                    CesRt = 0,
                    CesAmt = 0,
                    CesNonAdvlAmt = 0,
                    StateCesRt = 0,
                    StateCesAmt = 0,
                    StateCesNonAdvlAmt = 0,
                    OthChrg = 0,
                    TotItemVal = GetDecimal(reader, "TotItemVal")
                    //OrdLineRef = "",
                    //OrgCntry = "",
                    //PrdSlNo = ""
                });
            }


            return ItemList;
        }
    }
}
