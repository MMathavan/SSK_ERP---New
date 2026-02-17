using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSK_ERP.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Mvc;
using static SSK_ERP.Models.EInvoice;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesEInvoiceController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesInvoiceRegisterId = 20;

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

        private static string GetDateString(IDataRecord reader, string columnName, string format, string defaultValue = "")
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

            return Convert.ToDateTime(value).Date.ToString(format);
        }

        private class SalesEInvoiceListRow
        {
            public int TRANMID { get; set; }
            public DateTime TRANDATE { get; set; }
            public int TRANNO { get; set; }
            public string TRANDNO { get; set; }
            public string TRANREFNO { get; set; }
            public string TRANTAXBILLNO { get; set; }
            public string TRANREFNAME { get; set; }
            public decimal TRANNAMT { get; set; }
            public short DISPSTATUS { get; set; }
            public string ACKNO { get; set; }
        }

        [Authorize(Roles = "SalesEInvoiceIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "SalesEInvoiceIndex")]
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

                try
                {
                    var sql =
                        "SELECT TRANMID, TRANDATE, TRANNO, TRANDNO, TRANREFNO, TRANTAXBILLNO, TRANREFNAME, TRANNAMT, DISPSTATUS, ACKNO " +
                        "FROM TRANSACTIONMASTER " +
                        "WHERE REGSTRID = @p0 " +
                        "AND (@p1 IS NULL OR TRANDATE >= @p1) " +
                        "AND (@p2 IS NULL OR TRANDATE < @p2)";

                    List<SalesEInvoiceListRow> masters = db.Database
                        .SqlQuery<SalesEInvoiceListRow>(
                            sql,
                            SalesInvoiceRegisterId,
                            (object)fd ?? DBNull.Value,
                            (object)exclusiveTo ?? DBNull.Value)
                        .ToList();

                    var data = masters
                        .OrderByDescending(t => t.TRANDATE)
                        .ThenByDescending(t => t.TRANMID)
                        .Select(t => new
                        {
                            t.TRANMID,
                            t.TRANDATE,
                            t.TRANNO,
                            TRANDNO = string.IsNullOrWhiteSpace(t.TRANDNO) ? "0000" : t.TRANDNO,
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
                catch
                {
                    var query = db.TransactionMasters.Where(t => t.REGSTRID == SalesInvoiceRegisterId);

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
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [Authorize(Roles = "SalesEInvoiceUpload")]
        public ActionResult Upload(int id)
        {
            TempData["ErrorMessage"] = "Upload is not implemented yet.";
            return RedirectToAction("Index");
        }

        [Authorize(Roles = "SalesEInvoicePrint")]
        public ActionResult Print(int id)
        {
            return RedirectToAction("Print", "SalesInvoice", new { id });
        }

        public async Task<ActionResult> CInvoice(int id = 0)/*10rs.reminder*/
        {
            SqlConnection myConnection = null;
            try
            {
                var showJson = string.Equals(Request.QueryString["showjson"], "1", StringComparison.OrdinalIgnoreCase);
                SqlDataReader reader = null;
                //SqlDataReader Sreader = null;
                var connSettings = ConfigurationManager.ConnectionStrings["DefaultConnection"]
                    ?? ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"];
                if (connSettings == null || string.IsNullOrWhiteSpace(connSettings.ConnectionString))
                {
                    return Content("Connection string not found. Please configure 'DefaultConnection' or 'SSK_DefaultConnection' in Web.config.");
                }

                string _connStr = connSettings.ConnectionString;
                myConnection = new SqlConnection(_connStr);

                var tranmid = id;// Convert.ToInt32(Request.Form.Get("id"));// Convert.ToInt32(ids);

                SqlCommand sqlCmd = new SqlCommand();
                sqlCmd.CommandType = CommandType.Text;
                sqlCmd.CommandText = "Select * from Z_SALES_EINVOICE_DETAILS Where TRANMID = " + tranmid;
                sqlCmd.Connection = myConnection;
                myConnection.Open();
                reader = sqlCmd.ExecuteReader();

                int custgid = 0;
                string suptyp = "";
                string stringjson = "";

                decimal taxblamt = 0;
                decimal discamt = 0;
                decimal roff_amt = 0;

                decimal cgst_amt = 0;
                decimal sgst_amt = 0;
                decimal igst_amt = 0;

                while (reader.Read())
                {
                    taxblamt = GetDecimal(reader, "TRANGAMT");

                    cgst_amt = GetDecimal(reader, "TRANCGSTAMT");
                    sgst_amt = GetDecimal(reader, "TRANSGSTAMT");
                    igst_amt = GetDecimal(reader, "TRANIGSTAMT");

                    discamt = GetDecimal(reader, "DISCAMT");
                    roff_amt = GetDecimal(reader, "TRANROAMT");

                    custgid = GetInt32(reader, "CUSTGID");
                    switch (custgid)
                    {
                        case 6:
                            suptyp = "SEZWP";
                            break;
                        default:
                            suptyp = "B2B";
                            break;
                    }

                    var response = new Response()
                    {
                        Version = "1.1",

                        TranDtls = new TranDtls()
                        {
                            TaxSch = "GST",
                            SupTyp = suptyp,//"B2B",
                            RegRev = "N",
                            EcmGstin = null,
                            IgstOnIntra = "N"
                        },

                        DocDtls = new DocDtls()
                        {
                            Typ = "INV",
                            No = GetString(reader, "TRANDNO"),
                            Dt = GetDateString(reader, "TRANDATE", "dd/MM/yyyy")
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
                            Gstin = GetString(reader, "CATEBGSTNO"),
                            LglNm = GetString(reader, "TRANREFNAME"),
                            Pos = GetString(reader, "STATECODE"),
                            Addr1 = GetString(reader, "TRAN_CUST_ADDR1"),
                            Addr2 = GetString(reader, "TRAN_CUST_ADDR2"),
                            Loc = GetString(reader, "TRAN_CUST_LOCTDESC"),
                            Pin = GetInt32(reader, "TRAN_CUST_PINCODE"),
                            Stcd = GetString(reader, "STATECODE"),
                            Ph = GetString(reader, "CATECPHN1"),
                            Em = null// reader["CATEMAIL"].ToString()
                        },

                        ValDtls = new ValDtls()
                        {
                            AssVal = taxblamt,// Convert.ToDecimal(reader["HANDL_TAXABLE_AMT"]),
                            CesVal = 0,
                            CgstVal = cgst_amt,// Convert.ToDecimal(reader["HANDL_CGST_AMT"]),
                            IgstVal = igst_amt,// Convert.ToDecimal(reader["HANDL_IGST_AMT"]),
                            OthChrg = 0,
                            SgstVal = sgst_amt,// Convert.ToDecimal(reader["HANDL_sGST_AMT"]),
                            Discount = discamt,
                            StCesVal = 0,
                            RndOffAmt = roff_amt,
                            TotInvVal = GetDecimal(reader, "TRANNAMT"),
                            TotItemValSum = taxblamt,//Convert.ToDecimal(reader["TOTALITEMVAL"])
                        },

                        ItemList = GetItemList(tranmid),

                    };

                    stringjson = JsonConvert.SerializeObject(response);
                }

                string msg = "";
                string portalResponseRaw = "";
                int portalHttpStatus = 0;
                string portalHttpReason = "";

                using (var httpClient = new HttpClient())
                {
                    using (var request = new HttpRequestMessage(new HttpMethod("POST"), "https://my.gstzen.in/~gstzen/a/post-einvoice-data/einvoice-json/"))
                    {
                        request.Headers.TryAddWithoutValidation("Token", "3a3b2ee0-677a-4cb7-b8d4-5e26adf35dce");

                        request.Content = new StringContent(stringjson);
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                        var response = await httpClient.SendAsync(request);

                        if (response != null)
                        {
                            portalHttpStatus = (int)response.StatusCode;
                            portalHttpReason = response.ReasonPhrase;
                            var jsonString = await response.Content.ReadAsStringAsync();
                            portalResponseRaw = jsonString;
                            JObject data = null;
                            try
                            {
                                data = (JObject)JsonConvert.DeserializeObject(jsonString);
                            }
                            catch
                            {
                                data = null;
                            }

                            if (data == null)
                            {
                                msg = string.IsNullOrWhiteSpace(jsonString) ? "Empty response from portal." : jsonString;
                            }
                            else
                            {

                                var status = 0;
                                string zirnno = "";// param[2].ToString();
                                string zackdt = "";//param[3].ToString();
                                string zackno = "";//param[4].ToString();
                                string imgUrl = "";

                                msg = data["message"] != null ? data["message"].Value<string>() : "";
                                status = data["status"] != null ? data["status"].Value<int>() : 0;

                                if (status == 1)
                                {
                                    msg = data["message"] != null ? data["message"].Value<string>() : msg;
                                    zirnno = data["Irn"] != null ? data["Irn"].Value<string>() : "";
                                    zackdt = data["AckDt"] != null ? data["AckDt"].Value<string>() : "";
                                    zackno = data["AckNo"] != null ? data["AckNo"].Value<string>() : "";
                                    imgUrl = data["SignedQrCodeImgUrl"] != null ? data["SignedQrCodeImgUrl"].Value<string>() : "";

                                    var imageFileUrl = "";
                                    var newimageurl = "";

                                    if (imgUrl != "")
                                    {
                                        imageFileUrl = imgUrl;
                                        newimageurl = "https://my.gstzen.in" + imageFileUrl;
                                    }

                                    SqlConnection GmyConnection = new SqlConnection(_connStr);
                                    SqlCommand cmd = new SqlCommand("pr_IRN_Transaction_Update_Assgn_N01", GmyConnection);
                                    cmd.CommandType = CommandType.StoredProcedure;
                                    cmd.Parameters.AddWithValue("@PTranMID", tranmid);
                                    cmd.Parameters.AddWithValue("@PIRNNO", zirnno);
                                    cmd.Parameters.AddWithValue("@PACKNO", zackno);
                                    cmd.Parameters.AddWithValue("@PACKDT", Convert.ToDateTime(zackdt));
                                    cmd.Parameters.AddWithValue("@PCUSRID", Session["CUSRID"].ToString());
                                    cmd.Parameters.AddWithValue("@PSignedQRCode", imageFileUrl);
                                    cmd.Parameters.AddWithValue("@PSignedQRCodeURL", newimageurl);
                                    GmyConnection.Open();
                                    cmd.ExecuteNonQuery();
                                    GmyConnection.Close();

                                    string localFileName = tranmid.ToString() + ".png";
                                    string path = Server.MapPath("~/QrCode");

                                    WebClient webClient = new WebClient();
                                    webClient.DownloadFile(newimageurl, path + "\\" + localFileName);

                                    SqlConnection XmyConnection = new SqlConnection(_connStr);
                                    SqlCommand Xcmd = new SqlCommand("pr_Transaction_QrCode_Path_Update_Assgn", XmyConnection);
                                    Xcmd.CommandType = CommandType.StoredProcedure;
                                    Xcmd.Parameters.AddWithValue("@PTranMID", tranmid);
                                    Xcmd.Parameters.AddWithValue("@PPath", path + "\\" + localFileName);
                                    XmyConnection.Open();
                                    Xcmd.ExecuteNonQuery();
                                    XmyConnection.Close();

                                    msg = "Uploaded Succesfully";
                                }
                            }
                        }
                    }

                    if (showJson)
                    {
                        var payload = new
                        {
                            message = msg,
                            requestJson = stringjson,
                            responseJson = portalResponseRaw,
                            portalHttpStatus = portalHttpStatus,
                            portalHttpReason = portalHttpReason
                        };
                        return Content(JsonConvert.SerializeObject(payload), "application/json");
                    }

                    return Content(msg);
                }
            }
            catch (Exception ex)
            {
                var showJson = string.Equals(Request.QueryString["showjson"], "1", StringComparison.OrdinalIgnoreCase);
                if (showJson)
                {
                    var payload = new
                    {
                        message = ex.Message,
                        requestJson = "",
                        responseJson = ""
                    };
                    return Content(JsonConvert.SerializeObject(payload), "application/json");
                }

                return Content(ex.Message);
            }
            finally
            {
                if (myConnection != null)
                {
                    try { myConnection.Close(); } catch { }
                }
            }
        }

        private List<Models.EInvoice.ItemList> GetItemList(int id)
        {
            SqlDataReader reader = null;
            var connSettings = ConfigurationManager.ConnectionStrings["DefaultConnection"]
                ?? ConfigurationManager.ConnectionStrings["SSK_DefaultConnection"];
            if (connSettings == null || string.IsNullOrWhiteSpace(connSettings.ConnectionString))
            {
                return new List<Models.EInvoice.ItemList>();
            }

            string _connStr = connSettings.ConnectionString;
            SqlConnection myConnection = new SqlConnection(_connStr);

            SqlCommand sqlCmd = new SqlCommand("pr_EInvoice_Sales_Transaction_Detail_Assgn", myConnection);
            sqlCmd.CommandType = CommandType.StoredProcedure;
            sqlCmd.Parameters.AddWithValue("@PTranMID", id);
            sqlCmd.Connection = myConnection;
            myConnection.Open();
            reader = sqlCmd.ExecuteReader();

            List<Models.EInvoice.ItemList> ItemList = new List<Models.EInvoice.ItemList>();

            while (reader.Read())
            {

                ItemList.Add(new Models.EInvoice.ItemList
                {
                    SlNo = 1,
                    PrdDesc = GetString(reader, "PrdDesc"),
                    IsServc = "Y",
                    HsnCd = GetString(reader, "HsnCd"),
                    Barcde = "123456",
                    Qty = GetDecimal(reader, "Qty"),
                    FreeQty = 0,
                    Unit = GetString(reader, "UnitCode"),
                    UnitPrice = GetDecimal(reader, "UnitPrice"),
                    TotAmt = GetDecimal(reader, "TotAmt"),
                    Discount = GetDecimal(reader, "DiscAmt"),
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

