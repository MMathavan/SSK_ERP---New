using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace SSK_ERP.Models
{
    [JsonObject(Title = "user")]
    public class EInvoice
    {
        public class TranDtls
        {
            public string TaxSch { get; set; }
            public string SupTyp { get; set; } //= "B2B";
            public string RegRev { get; set; } //= "Y";
            public string EcmGstin { get; set; } //= null;
            public string IgstOnIntra { get; set; } //= "N";
        }

        public class DocDtls
        {
            //public TranDtls TranDtls;
            public string Typ { get; set; }
            public string No { get; set; }
            public string Dt { get; set; }
        }

        public class SellerDtls
        {
            //public TranDtls TranDtls;
            public string Gstin { get; set; }
            public string LglNm { get; set; }
            public string TrdNm { get; set; }
            public string Addr1 { get; set; }
            public string Addr2 { get; set; }
            public string Loc { get; set; }
            public int Pin { get; set; }
            public string Stcd { get; set; }
            public string Ph { get; set; }
            public string Em { get; set; }

        }

        public class BuyerDtls
        {
            public string Gstin { get; set; }
            public string LglNm { get; set; }
            public string TrdNm { get; set; }
            public string Pos { get; set; }
            public string Addr1 { get; set; }
            public string Addr2 { get; set; }
            public string Loc { get; set; }
            public int Pin { get; set; }
            public string Stcd { get; set; }
            public string Ph { get; set; }
            public string Em { get; set; }

        }

        public class DispDtls
        {
            public string Nm { get; set; }
            public string Addr1 { get; set; }
            public string Addr2 { get; set; }
            public string Loc { get; set; }
            public string Pin { get; set; }
            public string Stcd { get; set; }

        }

        public class ShipDtls
        {
            public string Gstin { get; set; }
            public string LglNm { get; set; }
            public string TrdNm { get; set; }
            public string Addr1 { get; set; }
            public string Addr2 { get; set; }
            public string Loc { get; set; }
            public string Pin { get; set; }
            public string Stcd { get; set; }

        }

        public class ItemList
        {
            public int SlNo { get; set; }
            public string PrdDesc { get; set; }
            public string IsServc { get; set; }
            public string HsnCd { get; set; }
            public string Barcde { get; set; }
            public decimal Qty { get; set; }
            public decimal FreeQty { get; set; }
            public string Unit { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal TotAmt { get; set; }
            public decimal Discount { get; set; }
            public decimal PreTaxVal { get; set; }
            public decimal AssAmt { get; set; }
            public decimal GstRt { get; set; }
            public decimal IgstAmt { get; set; }
            public decimal CgstAmt { get; set; }
            public decimal SgstAmt { get; set; }
            public decimal CesRt { get; set; }
            public decimal CesAmt { get; set; }
            public decimal CesNonAdvlAmt { get; set; }
            public decimal StateCesRt { get; set; }
            public decimal StateCesAmt { get; set; }
            public decimal StateCesNonAdvlAmt { get; set; }
            public decimal OthChrg { get; set; }
            public decimal TotItemVal { get; set; }
            //public string OrdLineRef { get; set; }
            //public string OrgCntry { get; set; }
            //public string PrdSlNo { get; set; }
            // public List<BchDtls> BchDtls { get; set; }
            // public List<AttribDtls> AttribDtls { get; set; }
        }
        public class BchDtls
        {
            public string Nm { get; set; }
            public string ExpDt { get; set; }
            public string WrDt { get; set; }

        }

        public class AttribDtls
        {
            public string Nm { get; set; }
            public decimal Val { get; set; }


        }

        public class ValDtls
        {
            public decimal AssVal { get; set; }
            public decimal CesVal { get; set; }
            public decimal CgstVal { get; set; }
            public decimal IgstVal { get; set; }
            public decimal OthChrg { get; set; }
            public decimal SgstVal { get; set; }
            public decimal Discount { get; set; }
            public decimal StCesVal { get; set; }
            public decimal RndOffAmt { get; set; }
            public decimal TotInvVal { get; set; }
            public decimal TotItemValSum { get; set; }

        }

        public class PayDtls
        {
            public string Nm { get; set; }
            public string AccDet { get; set; }
            public string Mode { get; set; }
            public string FinInsBr { get; set; }
            public int PayTerm { get; set; }
            public string PayInstr { get; set; }
            public string CrTrn { get; set; }
            public string DirDr { get; set; }
            public int CrDay { get; set; }
            public decimal PaidAmt { get; set; }
            public decimal PaymtDue { get; set; }




        }

        public class RefDtls
        {
            public string InvRm { get; set; }
            //public List<DocPerdDtls> DocPerdDtls { get; set; }
        }

        public class DocPerdDtls
        {
            public string InvStDt { get; set; }
            public string InvEndDt { get; set; }

        }

        public class PrecDocDtls
        {
            public string InvNo { get; set; }
            public string InvDt { get; set; }
            public string OthRefNo { get; set; }
        }

        public class ContrDtls
        {
            public string RecAdvRefr { get; set; }
            public string RecAdvDt { get; set; }
            public string TendRefr { get; set; }
            public string ContrRefr { get; set; }
            public string ExtRefr { get; set; }
            public string ProjRefr { get; set; }
            public string PORefr { get; set; }
            public string PORefDt { get; set; }

        }

        public class AddlDocDtls
        {
            public string Url { get; set; }
            public string Docs { get; set; }
            public string Info { get; set; }
        }

        public class ExpDtls
        {
            public string ShipBNo { get; set; }
            public string ShipBDt { get; set; }
            public string Port { get; set; }
            public string RefClm { get; set; }
            public string ForCur { get; set; }
            public string CntCode { get; set; }
            public string ExpDuty { get; set; }

        }

        public class EwbDtls
        {
            public string TransId { get; set; }
            public string TransName { get; set; }
            public int Distance { get; set; }
            public string TransDocNo { get; set; }
            public string TransDocDt { get; set; }
            public string VehNo { get; set; }
            public string VehType { get; set; }
            public string TransMode { get; set; }

        }


        public class Response
        {
            public string Version { get; set; }
            public TranDtls TranDtls { get; set; }
            public DocDtls DocDtls { get; set; }
            public SellerDtls SellerDtls { get; set; }
            public BuyerDtls BuyerDtls { get; set; }
            public ValDtls ValDtls { get; set; }
            //public EwbDtls EwbDtls { get; set; }
            public RefDtls RefDtls { get; set; }
            public List<ItemList> ItemList { get; set; }

            // public List<UserModel> users { get; set; }
            // public List<TranDtls> zTranDtls { get; set; }
            //public List<DocDtls> DocDtls { get; set; }
            //public List<SellerDtls> SellerDtls { get; set; }
            //public List<BuyerDtls> BuyerDtls { get; set; }
            //public List<DispDtls> DispDtls { get; set; }
            //public List<ShipDtls> ShipDtls { get; set; }
            //public List<ItemList> ItemList { get; set; }

            //public List<ValDtls> ValDtls { get; set; }

            //public List<PayDtls> PayDtls { get; set; }
            ////public List<RefDtls> RefDtls { get; set; }
            //public List<PrecDocDtls> PrecDocDtls { get; set; }
            //public List<ContrDtls> ContrDtls { get; set; }
            //public List<AddlDocDtls> AddlDocDtls { get; set; }
            //public List<ExpDtls> ExpDtls { get; set; }
            //public List<EwbDtls> EwbDtls { get; set; }
        }

        public class Response2
        {
            public List<ItemList> ItemList { get; set; }

            public List<ValDtls> ValDtls { get; set; }

            public List<PayDtls> PayDtls { get; set; }
            public List<RefDtls> RefDtls { get; set; }
            public List<PrecDocDtls> PrecDocDtls { get; set; }
            public List<ContrDtls> ContrDtls { get; set; }
            public List<AddlDocDtls> AddlDocDtls { get; set; }
            public List<ExpDtls> ExpDtls { get; set; }
            public List<EwbDtls> EwbDtls { get; set; }


        }
    }
}