using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Web.Mvc;

namespace SSK_ERP.Models
{
    [Table("MATERIALMASTER")]
    public class MaterialMaster
    {
        [Key]
        public int MTRLID { get; set; }

        [DisplayName("Material Group")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select material group")]
        public int MTRLGID { get; set; }

        [DisplayName("Material Description")]
        [Required(ErrorMessage = "Please enter material description")]
        [MaxLength(100)]
        public string MTRLDESC { get; set; }

        [DisplayName("Code")]
        [Required(ErrorMessage = "Please enter material code")]
        [MaxLength(15)]
        [Remote("ValidateMTRLCODE", "MaterialMaster", AdditionalFields = "MTRLID", ErrorMessage = "This code is already used.")]
        public string MTRLCODE { get; set; }

        [DisplayName("Unit")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select unit")]
        public int UNITID { get; set; }

        [DisplayName("Profit")]
        [Range(typeof(decimal), "0", "9999999999999.99", ErrorMessage = "Please enter valid profit")]
        public decimal MTRLPRFT { get; set; }

        [DisplayName("Rate")]
        [Range(typeof(decimal), "0", "9999999999999.99", ErrorMessage = "Please enter valid rate")]
        public decimal RATE { get; set; }

        [DisplayName("HSN Code")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select HSN code")]
        public int HSNID { get; set; }

        [MaxLength(100)]
        public string CUSRID { get; set; }

        [MaxLength(100)]
        public string LMUSRID { get; set; }

        [DisplayName("Status")]
        public short DISPSTATUS { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime PRCSDATE { get; set; }
    }
}
