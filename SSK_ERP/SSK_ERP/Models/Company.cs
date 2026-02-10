using SSK_ERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace SSK_ERP.Models
{
    public class Company
    {
        public List<SSK_ERP.Models.CompanyMaster> masterdata { get; set; }
        public List<SSK_ERP.Models.CompanyDetail> detaildata { get; set; }
        //public List<pr_CompanyDetail_Flx_Assgn_Result> queryresultdata { get; set; }
    }
}