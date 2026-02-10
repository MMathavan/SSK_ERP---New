using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SSK_ERP.Models
{
    [Table("TRANSACTIONDETAIL")]
    public class TransactionDetail
    {
        [Key]
        [Column("TRANDID")]
        public int TRANDID { get; set; }

        [Required]
        [Column("TRANMID")]
        public int TRANMID { get; set; }

        [Required]
        [Column("TRANDREFID")]
        public int TRANDREFID { get; set; }

        [Required]
        [MaxLength(15)]
        [Column("TRANDREFNO")]
        public string TRANDREFNO { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("TRANDREFNAME")]
        public string TRANDREFNAME { get; set; }

        [Required]
        [Column("TRANDMTRLPRFT", TypeName = "numeric")]
        public decimal TRANDMTRLPRFT { get; set; }

        [Required]
        [Column("HSNID")]
        public int HSNID { get; set; }

        [Required]
        [Column("PACKMID")]
        public int PACKMID { get; set; }

        [Required]
        [Column("TRANDQTY", TypeName = "numeric")]
        public decimal TRANDQTY { get; set; }

        [Required]
        [Column("TRANDRATE", TypeName = "numeric")]
        public decimal TRANDRATE { get; set; }

        [Required]
        [Column("TRANDARATE", TypeName = "numeric")]
        public decimal TRANDARATE { get; set; }

        [Required]
        [Column("TRANDGAMT", TypeName = "numeric")]
        public decimal TRANDGAMT { get; set; }

        [Required]
        [Column("TRANDCGSTAMT", TypeName = "numeric")]
        public decimal TRANDCGSTAMT { get; set; }

        [Required]
        [Column("TRANDSGSTAMT", TypeName = "numeric")]
        public decimal TRANDSGSTAMT { get; set; }

        [Required]
        [Column("TRANDIGSTAMT", TypeName = "numeric")]
        public decimal TRANDIGSTAMT { get; set; }

        [Required]
        [Column("TRANDNAMT", TypeName = "numeric")]
        public decimal TRANDNAMT { get; set; }

        [Required]
        [Column("TRANDAID")]
        public int TRANDAID { get; set; }

        [MaxLength(250)]
        [Column("TRANDNARTN")]
        public string TRANDNARTN { get; set; }

        [MaxLength(100)]
        [Column("TRANDRMKS")]
        public string TRANDRMKS { get; set; }

        [ForeignKey("TRANMID")]
        public virtual TransactionMaster TransactionMaster { get; set; }
    }
}
