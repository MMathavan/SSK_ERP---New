using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSK_ERP.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
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

            var dt = Convert.ToDateTime(value).Date;
            if (string.Equals(format, "dd/MM/yyyy", StringComparison.OrdinalIgnoreCase))
            {
                return dt.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture);
            }

            return dt.ToString(format, CultureInfo.InvariantCulture);
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

        private static string NormalizePhoneDigits(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return string.Empty;
            }

            return new string(phone.Where(char.IsDigit).ToArray());
        }

        private static string NormalizeNameKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return Regex.Replace(name.ToUpperInvariant(), "[^A-Z0-9]", string.Empty);
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
                sqlCmd.CommandText = @"
SELECT
    TM.TRANMID,
    TM.REGSTRID,
    TM.TRANDATE,
    TM.TRANNO,
    RIGHT(TM.TRANDNO, 15) AS TRANDNO,
    TM.TRANREFID,

    CM.COMPGSTNO,
    CM.COMPDNAME AS COMPNAME,
    CM.COMPADDR1,
    CM.COMPADDR2,
    CM.COMPLOCTDESC,
    CM.COMPPINCODE,
    CM.COMPSTATECODE,
    CM.COMPPHN1,
    CM.COMPMAIL,

    CUS.CATE_GST_NO,
    TM.TRANREFNAME,
    CUS.CATEADDR1,
    CUS.CATEADDR2,
    CUS.CATEADDR3,
    CUS.CATEADDR4,
    CUS.CATEADDR5,
    LOC.LOCTDESC,
    ST.STATEDESC,
    ST.STATECODE,
    CUS.CATEPHN3,

    (TM.TRANGAMT - ISNULL(Z1.DEDVALUE, 0)) AS TRANGAMT,
    TM.TRANNAMT,
    ISNULL(Z2.CGSTAMT, 0) AS TRANCGSTAMT,
    ISNULL(Z2.SGSTAMT, 0) AS TRANSGSTAMT,
    ISNULL(Z2.IGSTAMT, 0) AS TRANIGSTAMT,
    ISNULL(Z2.ROAMT, 0) AS TRANROAMT,
    ISNULL(Z1.DEDVALUE, 0) AS DISCAMT,

    0 AS CUSTGID
FROM TRANSACTIONMASTER TM
INNER JOIN COMPANYACCOUNTINGDETAIL CAD ON TM.COMPYID = CAD.COMPYID
INNER JOIN COMPANYMASTER CM ON CAD.COMPID = CM.COMPID
LEFT JOIN CUSTOMERMASTER CUS ON TM.TRANREFID = CUS.CATEID
LEFT JOIN LOCATIONMASTER LOC ON CUS.LOCTID = LOC.LOCTID
LEFT JOIN STATEMASTER ST ON CUS.STATEID = ST.STATEID
LEFT JOIN (
    SELECT
        TRANMID,
        SUM(CASE CFID WHEN 15 THEN DEDVALUE ELSE 0 END) AS CGSTAMT,
        SUM(CASE CFID WHEN 16 THEN DEDVALUE ELSE 0 END) AS SGSTAMT,
        SUM(CASE CFID WHEN 17 THEN DEDVALUE ELSE 0 END) AS IGSTAMT,
        SUM(CASE CFID WHEN 1 THEN DEDVALUE ELSE 0 END) AS ROAMT
    FROM TRANSACTIONMASTERFACTOR
    GROUP BY TRANMID
) Z2 ON Z2.TRANMID = TM.TRANMID
LEFT JOIN Z_SALES_EINVOICE_DETAILS_01 Z1 ON Z1.TRANMID = TM.TRANMID
WHERE TM.TRANMID = @tranmid";
                sqlCmd.Parameters.AddWithValue("@tranmid", tranmid);
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

                    int? tranRefId = null;
                    try
                    {
                        tranRefId = db.TransactionMasters
                            .Where(t => t.TRANMID == tranmid)
                            .Select(t => (int?)t.TRANREFID)
                            .FirstOrDefault();
                    }
                    catch
                    {
                        tranRefId = null;
                    }

                    var tranRefIdFromSql = GetInt32(reader, "TRANREFID");
                    var resolvedTranRefId = tranRefIdFromSql > 0 ? tranRefIdFromSql : (tranRefId ?? 0);

                    if (resolvedTranRefId <= 0)
                    {
                        var tranRefNameFromSql = GetString(reader, "TRANREFNAME");
                        if (!string.IsNullOrWhiteSpace(tranRefNameFromSql))
                        {
                            try
                            {
                                var targetKey = NormalizeNameKey(tranRefNameFromSql);
                                var words = Regex.Matches(tranRefNameFromSql, "[A-Za-z0-9]+");
                                var tokens = words
                                    .Cast<Match>()
                                    .Select(m => m.Value)
                                    .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length > 2)
                                    .Take(4)
                                    .ToList();

                                IQueryable<CustomerMaster> q = db.CustomerMasters;
                                foreach (var token in tokens)
                                {
                                    var t = token;
                                    q = q.Where(c => c.CATENAME.Contains(t) || c.CATEDNAME.Contains(t));
                                }

                                var candidates = q
                                    .Select(c => new { c.CATEID, c.CATENAME, c.CATEDNAME })
                                    .Take(25)
                                    .ToList();

                                var matchedIds = candidates
                                    .Where(c => NormalizeNameKey(c.CATENAME) == targetKey || NormalizeNameKey(c.CATEDNAME) == targetKey)
                                    .Select(c => c.CATEID)
                                    .Distinct()
                                    .Take(2)
                                    .ToList();

                                if (matchedIds.Count == 1)
                                {
                                    resolvedTranRefId = matchedIds[0];
                                }
                            }
                            catch
                            {
                                // ignore fallback lookup failures
                            }
                        }
                    }

                    if (resolvedTranRefId <= 0)
                    {
                        var msgMissingBuyer = "Customer reference not found for this invoice. TRANMID=" + tranmid + ", TRANREFNAME=\"" + GetString(reader, "TRANREFNAME") + "\".";
                        if (showJson)
                        {
                            object candidatesPayload = null;
                            try
                            {
                                var tranRefName = GetString(reader, "TRANREFNAME");
                                var words = Regex.Matches(tranRefName ?? string.Empty, "[A-Za-z0-9]+");
                                var tokens = words
                                    .Cast<Match>()
                                    .Select(m => m.Value)
                                    .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length > 2)
                                    .Take(4)
                                    .ToList();

                                IQueryable<CustomerMaster> q = db.CustomerMasters;
                                foreach (var token in tokens)
                                {
                                    var t = token;
                                    q = q.Where(c => c.CATENAME.Contains(t) || c.CATEDNAME.Contains(t));
                                }

                                var candidates = q
                                    .Select(c => new { c.CATEID, c.CATENAME, c.CATEDNAME, c.CATE_GST_NO })
                                    .Take(10)
                                    .ToList();

                                candidatesPayload = candidates;
                                msgMissingBuyer = msgMissingBuyer + " CandidateCustomers=" + JsonConvert.SerializeObject(candidates);
                            }
                            catch
                            {
                                candidatesPayload = null;
                            }

                            var payloadEarly = new
                            {
                                message = msgMissingBuyer,
                                requestJson = "",
                                responseJson = "",
                                candidateCustomers = candidatesPayload,
                                portalHttpStatus = 0,
                                portalHttpReason = ""
                            };
                            return Content(JsonConvert.SerializeObject(payloadEarly), "application/json");
                        }

                        return Content(msgMissingBuyer);
                    }

                    CustomerMaster buyerMaster = null;
                    if (resolvedTranRefId > 0)
                    {
                        try
                        {
                            buyerMaster = db.CustomerMasters.FirstOrDefault(c => c.CATEID == resolvedTranRefId);
                        }
                        catch
                        {
                            buyerMaster = null;
                        }
                    }

                    if (buyerMaster == null)
                    {
                        var msgMissingBuyer = "Customer master not found for this invoice. TRANMID=" + tranmid + ", TRANREFID=" + resolvedTranRefId + ".";
                        if (showJson)
                        {
                            var payloadEarly = new
                            {
                                message = msgMissingBuyer,
                                requestJson = "",
                                responseJson = "",
                                portalHttpStatus = 0,
                                portalHttpReason = ""
                            };
                            return Content(JsonConvert.SerializeObject(payloadEarly), "application/json");
                        }

                        return Content(msgMissingBuyer);
                    }

                    string buyerGstin = GetString(reader, "CATE_GST_NO");
                    if (string.IsNullOrWhiteSpace(buyerGstin) && buyerMaster != null)
                    {
                        buyerGstin = buyerMaster.CATE_GST_NO;
                    }

                    int buyerPin = 0;
                    var buyerPinRaw = NormalizePhoneDigits(GetString(reader, "CATEADDR5"));
                    if (!string.IsNullOrWhiteSpace(buyerPinRaw))
                    {
                        int.TryParse(buyerPinRaw, out buyerPin);
                    }

                    if ((buyerPin < 100000 || buyerPin > 999999) && buyerMaster != null)
                    {
                        if (int.TryParse(NormalizePhoneDigits(buyerMaster.CATEADDR5), out var pinParsed))
                        {
                            buyerPin = pinParsed;
                        }
                    }

                    string buyerPhone = NormalizePhoneDigits(GetString(reader, "CATEPHN3"));
                    if (string.IsNullOrWhiteSpace(buyerPhone) && buyerMaster != null)
                    {
                        buyerPhone = NormalizePhoneDigits(buyerMaster.CATEPHN3);
                        if (string.IsNullOrWhiteSpace(buyerPhone)) buyerPhone = NormalizePhoneDigits(buyerMaster.CATEPHN4);
                        if (string.IsNullOrWhiteSpace(buyerPhone)) buyerPhone = NormalizePhoneDigits(buyerMaster.CATEPHN1);
                        if (string.IsNullOrWhiteSpace(buyerPhone)) buyerPhone = NormalizePhoneDigits(buyerMaster.CATEPHN2);
                    }

                    if (string.IsNullOrWhiteSpace(buyerGstin))
                    {
                        buyerGstin = "URP";
                        if (!string.Equals(suptyp, "SEZWP", StringComparison.OrdinalIgnoreCase))
                        {
                            suptyp = "B2C";
                        }
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
                            Gstin = buyerGstin,
                            LglNm = GetString(reader, "TRANREFNAME"),
                            Pos = NormalizeGstStateCode(GetString(reader, "STATECODE")),
                            Addr1 = GetString(reader, "CATEADDR1"),
                            Addr2 = GetString(reader, "CATEADDR2"),
                            Loc = GetString(reader, "LOCTDESC"),
                            Pin = buyerPin,
                            Stcd = NormalizeGstStateCode(GetString(reader, "STATECODE")),
                            Ph = buyerPhone,
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

                    stringjson = JsonConvert.SerializeObject(
                        response,
                        new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore
                        });
                }

                if (string.IsNullOrWhiteSpace(stringjson))
                {
                    var msgNoJson = "EInvoice JSON not generated. Source data not found for this invoice. TRANMID=" + id + ".";
                    if (showJson)
                    {
                        try
                        {
                            using (var diagConn = new SqlConnection(_connStr))
                            {
                                diagConn.Open();

                                var diag = new Dictionary<string, object>();

                                using (var cmd = new SqlCommand(@"
SELECT
    CASE WHEN EXISTS(SELECT 1 FROM TRANSACTIONMASTER WHERE TRANMID=@id) THEN 1 ELSE 0 END AS HasTM,
    CASE WHEN EXISTS(
        SELECT 1
        FROM TRANSACTIONMASTER TM
        INNER JOIN COMPANYACCOUNTINGDETAIL CAD ON TM.COMPYID = CAD.COMPYID
        WHERE TM.TRANMID=@id
    ) THEN 1 ELSE 0 END AS HasCAD,
    CASE WHEN EXISTS(
        SELECT 1
        FROM TRANSACTIONMASTER TM
        INNER JOIN COMPANYACCOUNTINGDETAIL CAD ON TM.COMPYID = CAD.COMPYID
        INNER JOIN COMPANYMASTER CM ON CAD.COMPID = CM.COMPID
        WHERE TM.TRANMID=@id
    ) THEN 1 ELSE 0 END AS HasCompany,
    CASE WHEN EXISTS(
        SELECT 1
        FROM TRANSACTIONMASTER TM
        INNER JOIN CUSTOMERMASTER CUS ON TM.TRANREFID = CUS.CATEID
        WHERE TM.TRANMID=@id
    ) THEN 1 ELSE 0 END AS HasCustomer,
    CASE WHEN EXISTS(
        SELECT 1
        FROM TRANSACTIONMASTER TM
        INNER JOIN CUSTOMERMASTER CUS ON TM.TRANREFID = CUS.CATEID
        INNER JOIN LOCATIONMASTER LOC ON CUS.LOCTID = LOC.LOCTID
        WHERE TM.TRANMID=@id
    ) THEN 1 ELSE 0 END AS HasLocation,
    CASE WHEN EXISTS(
        SELECT 1
        FROM TRANSACTIONMASTER TM
        INNER JOIN CUSTOMERMASTER CUS ON TM.TRANREFID = CUS.CATEID
        INNER JOIN STATEMASTER ST ON CUS.STATEID = ST.STATEID
        WHERE TM.TRANMID=@id
    ) THEN 1 ELSE 0 END AS HasState,
    CASE WHEN EXISTS(
        SELECT 1
        FROM TRANSACTIONMASTERFACTOR
        WHERE TRANMID=@id
    ) THEN 1 ELSE 0 END AS HasFactors
", diagConn))
                                {
                                    cmd.Parameters.AddWithValue("@id", id);
                                    using (var r = cmd.ExecuteReader())
                                    {
                                        if (r.Read())
                                        {
                                            diag["HasTM"] = r["HasTM"];
                                            diag["HasCAD"] = r["HasCAD"];
                                            diag["HasCompany"] = r["HasCompany"];
                                            diag["HasCustomer"] = r["HasCustomer"];
                                            diag["HasLocation"] = r["HasLocation"];
                                            diag["HasState"] = r["HasState"];
                                            diag["HasFactors"] = r["HasFactors"];
                                        }
                                    }
                                }

                                msgNoJson = msgNoJson + " Diagnostics=" + JsonConvert.SerializeObject(diag);
                            }
                        }
                        catch
                        {
                            // ignore diagnostics failure
                        }

                        var payload = new
                        {
                            message = msgNoJson,
                            requestJson = "",
                            responseJson = "",
                            portalHttpStatus = 0,
                            portalHttpReason = ""
                        };
                        return Content(JsonConvert.SerializeObject(payload), "application/json");
                    }

                    return Content(msgNoJson);
                }

                string msg = "";
                string portalResponseRaw = "";
                int portalHttpStatus = 0;
                string portalHttpReason = "";

                using (var httpClient = new HttpClient())
                {
                    using (var request = new HttpRequestMessage(new HttpMethod("POST"), "https://my.gstzen.in/~gstzen/a/post-einvoice-data/einvoice-json/"))
                    {
                        var tokenFromConfig = (ConfigurationManager.AppSettings["GSTZEN_TOKEN"] ?? string.Empty).Trim();
                        var userIdFromConfig = (ConfigurationManager.AppSettings["GSTZEN_USERID"] ?? string.Empty).Trim();

                        string tokenMasked = string.Empty;
                        if (!string.IsNullOrWhiteSpace(tokenFromConfig))
                        {
                            tokenMasked = tokenFromConfig.Length <= 8
                                ? new string('*', tokenFromConfig.Length)
                                : tokenFromConfig.Substring(0, 4) + new string('*', tokenFromConfig.Length - 8) + tokenFromConfig.Substring(tokenFromConfig.Length - 4);
                        }

                        if (string.IsNullOrWhiteSpace(tokenFromConfig))
                        {
                            var missingMsg = "GSTZen token is not configured. Please set appSettings key GSTZEN_TOKEN in Web.config.";
                            if (showJson)
                            {
                                var payload = new
                                {
                                    message = missingMsg,
                                    requestJson = stringjson,
                                    responseJson = "",
                                    portalHttpStatus = 0,
                                    portalHttpReason = ""
                                };
                                return Content(JsonConvert.SerializeObject(payload), "application/json");
                            }

                            return Content(missingMsg);
                        }

                        if (string.IsNullOrWhiteSpace(userIdFromConfig))
                        {
                            var missingMsg = "GSTZen user id is not configured. Please set appSettings key GSTZEN_USERID in Web.config.";
                            if (showJson)
                            {
                                var payload = new
                                {
                                    message = missingMsg,
                                    requestJson = stringjson,
                                    responseJson = "",
                                    portalHttpStatus = 0,
                                    portalHttpReason = ""
                                };
                                return Content(JsonConvert.SerializeObject(payload), "application/json");
                            }

                            return Content(missingMsg);
                        }

                        // GSTZen sometimes expects different header keys depending on gateway/proxy.
                        request.Headers.TryAddWithoutValidation("Token", tokenFromConfig);
                        request.Headers.TryAddWithoutValidation("token", tokenFromConfig);
                        request.Headers.TryAddWithoutValidation("TOKEN", tokenFromConfig);

                        request.Headers.TryAddWithoutValidation("UserId", userIdFromConfig);
                        request.Headers.TryAddWithoutValidation("UserID", userIdFromConfig);
                        request.Headers.TryAddWithoutValidation("userid", userIdFromConfig);
                        request.Headers.TryAddWithoutValidation("user_id", userIdFromConfig);

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

                                if (status == 0 && data["Status"] != null)
                                {
                                    try
                                    {
                                        var statusAlt = data["Status"].Value<int>();
                                        if (statusAlt == 0 && data["ErrorDetails"] != null && data["ErrorDetails"].Type == JTokenType.Array)
                                        {
                                            var firstErr = data["ErrorDetails"].First;
                                            if (firstErr != null && firstErr["ErrorMessage"] != null)
                                            {
                                                msg = firstErr["ErrorMessage"].Value<string>();

                                                if (msg != null && msg.IndexOf("Incorrect user id", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    var tokenConfigured = !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["GSTZEN_TOKEN"]);
                                                    var userIdConfigured = !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["GSTZEN_USERID"]);
                                                    msg = msg + " (Config check: GSTZEN_TOKEN=" + (tokenConfigured ? "SET" : "MISSING") + ", GSTZEN_USERID=" + (userIdConfigured ? "SET" : "MISSING") + ")";

                                                    if (showJson && msg != null && (msg.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("incorrect user", StringComparison.OrdinalIgnoreCase) >= 0))
                                                    {
                                                        msg = msg + " (SentCredentials: UserId='" + userIdFromConfig + "', Token='" + tokenMasked + "', HttpStatus=" + portalHttpStatus + ")";
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // keep existing msg
                                    }
                                }

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
                                    GmyConnection.Close();

                                    string localFileName = tranmid.ToString() + ".png";
                                    string path = Server.MapPath("~/QrCode");

                                    WebClient webClient = new WebClient();
                                    try
                                    {
                                        if (!System.IO.Directory.Exists(path))
                                        {
                                            System.IO.Directory.CreateDirectory(path);
                                        }

                                        webClient.DownloadFile(newimageurl, path + "\\" + localFileName);
                                    }
                                    catch
                                    {
                                        // ignore QR download failures; IRN is already updated
                                    }

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

