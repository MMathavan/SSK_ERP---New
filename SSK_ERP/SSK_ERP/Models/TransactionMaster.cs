using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SSK_ERP.Models
{
    [Table("TRANSACTIONMASTER")]
    public class TransactionMaster
    {
        [Key]
        [Column("TRANMID")]
        public int TRANMID { get; set; }

        [Required]
        [Column("COMPYID")]
        public int COMPYID { get; set; }

        [Required]
        [Column("SDPTID")]
        public int SDPTID { get; set; }

        [Required]
        [Column("REGSTRID")]
        public int REGSTRID { get; set; }

        [Required]
        [Column("TRANBTYPE")]
        public short TRANBTYPE { get; set; }

        [Required]
        [Column("TRANDATE")]
        [DataType(DataType.Date)]
        public DateTime TRANDATE { get; set; }

        [Required]
        [Column("TRANTIME")]
        [DataType(DataType.DateTime)]
        public DateTime TRANTIME { get; set; }

        [Required]
        [Column("TRANNO")]
        public int TRANNO { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("TRANDNO")]
        public string TRANDNO { get; set; }

        [Required]
        [Column("TRANREFID")]
        public int TRANREFID { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("TRANREFNAME")]
        public string TRANREFNAME { get; set; }

        [Required]
        [Column("TRANSTATETYPE")]
        public short TRANSTATETYPE { get; set; }

        [MaxLength(25)]
        [Column("TRANREFNO")]
        public string TRANREFNO { get; set; }

        [MaxLength(50)]
        [Column("TRANPONUM")]
        public string TRANPONUM { get; set; }

        [MaxLength(50)]
        [Column("TRANTAXBILLNO")]
        public string TRANTAXBILLNO { get; set; }

        [MaxLength(100)]
        [Column("IRNNO")]
        public string IRNNO { get; set; }

        [MaxLength(100)]
        [Column("ACKNO")]
        public string ACKNO { get; set; }

        [Column("ACKDT")]
        public DateTime? ACKDT { get; set; }

        [Column("QRCODEPATH")]
        public string QRCODEPATH { get; set; }

        [MaxLength(100)]
        [Column("EINV_UPLOADEDBY")]
        public string EINV_UPLOADEDBY { get; set; }

        [Column("EINV_UPLOADED_DATETIME")]
        public DateTime? EINV_UPLOADED_DATETIME { get; set; }

        [MaxLength(100)]
        [Column("SignedInvoice")]
        public string SignedInvoice { get; set; }

        [MaxLength(100)]
        [Column("SignedQRCode")]
        public string SignedQRCode { get; set; }

        [Column("SignedInvoiceURL")]
        public string SignedInvoiceURL { get; set; }

        [Column("SignedQRCodeURL")]
        public string SignedQRCodeURL { get; set; }

        [Required]
        [Column("TRAN_CRDPRDT")]
        public int TRAN_CRDPRDT { get; set; }

        [Required]
        [Column("TRANGAMT", TypeName = "numeric")]
        public decimal TRANGAMT { get; set; }

        [Required]
        [Column("TRANCGSTAMT", TypeName = "numeric")]
        public decimal TRANCGSTAMT { get; set; }

        [Required]
        [Column("TRANSGSTAMT", TypeName = "numeric")]
        public decimal TRANSGSTAMT { get; set; }

        [Required]
        [Column("TRANIGSTAMT", TypeName = "numeric")]
        public decimal TRANIGSTAMT { get; set; }

        [Required]
        [Column("TRANNAMT", TypeName = "numeric")]
        public decimal TRANNAMT { get; set; }

        [MaxLength(250)]
        [Column("TRANAMTWRDS")]
        public string TRANAMTWRDS { get; set; }

        [Required]
        [Column("TRANLMID")]
        public int TRANLMID { get; set; }

        [Required]
        [Column("TRANPCOUNT")]
        public int TRANPCOUNT { get; set; }

        [MaxLength(250)]
        [Column("TRANNARTN")]
        public string TRANNARTN { get; set; }

        [MaxLength(100)]
        [Column("TRANRMKS")]
        public string TRANRMKS { get; set; }

        [Required]
        [Column("EXPRTSTATUS")]
        public int EXPRTSTATUS { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("CUSRID")]
        public string CUSRID { get; set; }

        [Required]
        [MaxLength(100)] 
        [Column("LMUSRID")]
        public string LMUSRID { get; set; }

        [Required]
        [Column("DISPSTATUS")]
        public short DISPSTATUS { get; set; }

        [Required]
        [Column("PRCSDATE")]
        public DateTime PRCSDATE { get; set; }

        public virtual ICollection<TransactionDetail> Details { get; set; }
    }
}
