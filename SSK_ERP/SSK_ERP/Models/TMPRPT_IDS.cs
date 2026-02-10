using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Web;

namespace SSK_ERP.Models
{
    [Table("TMPRPT_IDS")]
    public class TMPRPT_IDS
    {
        [Key]
        public string KUSRID { get; set; }
        public string OPTNSTR { get; set; }
        public int RPTID { get; set; }
    }
}