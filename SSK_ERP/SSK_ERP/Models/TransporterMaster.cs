using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Web.Mvc;

namespace SSK_ERP.Models
{
    [Table("TRANSPORTERMASTER")]
    public class TransporterMaster
    {
        [Key]
        public int CATEID { get; set; }

        [DisplayName("Transporter Type")]
        public int CATETID { get; set; } = 1; // Default value 1

        [DisplayName("Transporter Name")]
        [Required(ErrorMessage = "Please enter transporter name")]
        [Remote("ValidateCATENAME", "Common", AdditionalFields = "i_CATENAME", ErrorMessage = "This is already used.")]
        public string CATENAME { get; set; }

        [DisplayName("Address Line 1")]
        public string CATEADDR1 { get; set; }

        [DisplayName("Address Line 2")]
        public string CATEADDR2 { get; set; }

        [DisplayName("Address Line 3")]
        public string CATEADDR3 { get; set; }

        [DisplayName("Address Line 4")]
        public string CATEADDR4 { get; set; }

        [DisplayName("Phone 1")]
        [MaxLength(25)]
        public string CATEPHN1 { get; set; }

        [DisplayName("Phone 2")]
        [MaxLength(25)]
        public string CATEPHN2 { get; set; }

        [DisplayName("Phone 3")]
        [MaxLength(25)]
        public string CATEPHN3 { get; set; }

        [DisplayName("Phone 4")]
        [MaxLength(25)]
        public string CATEPHN4 { get; set; }

        [DisplayName("Contact Person Name")]
        [MaxLength(100)]
        public string CATECPNAME { get; set; }

        [DisplayName("Contact Person Email")]
        [MaxLength(100)]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        public string CATEEMAIL { get; set; }

        [DisplayName("Transporter Code")]
        [Required(ErrorMessage = "Please enter transporter code")]
        [MaxLength(15)]
        [Remote("ValidateCATECODE", "Common", AdditionalFields = "i_CATECODE", ErrorMessage = "This is already used.")]
        public string CATECODE { get; set; }

        // Created user id (username)
        public string CUSRID { get; set; }

        // Last modified user id (int)
        public int LMUSRID { get; set; }

        [DisplayName("Status")]
        public short DISPSTATUS { get; set; }

        [DataType(DataType.Date)]
        public DateTime PRCSDATE { get; set; }

        [DisplayName("Transporter Tracking Link")]
        public string CATE_TRACKING_LINK { get; set; }
    }
}
