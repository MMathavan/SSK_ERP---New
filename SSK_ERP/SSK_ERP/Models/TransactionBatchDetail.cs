using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SSK_ERP.Models
{
    [Table("TRANSACTIONBATCHDETAIL")]
    public class TransactionBatchDetail
    {
        [Key]
        [Column("TRANBID")]
        public int TRANBID { get; set; }

        [Required]
        [Column("TRANDID")]
        public int TRANDID { get; set; }

        [Required]
        [Column("AMTRLID")]
        public int AMTRLID { get; set; }

        [Required]
        [Column("HSNID")]
        public int HSNID { get; set; }

        [Required]
        [Column("STKBID")]
        public int STKBID { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("TRANBDNO")]
        public string TRANBDNO { get; set; }

        [Required]
        [Column("TRANBEXPDATE")]
        public DateTime TRANBEXPDATE { get; set; }

        [Required]
        [Column("PACKMID")]
        public int PACKMID { get; set; }

        [Required]
        [Column("TRANPQTY")]
        public int TRANPQTY { get; set; }

        [Required]
        [Column("TRANBQTY")]
        public int TRANBQTY { get; set; }

        [Required]
        [Column("TRANBRATE", TypeName = "numeric")]
        public decimal TRANBRATE { get; set; }

        [Required]
        [Column("TRANBPTRRATE", TypeName = "numeric")]
        public decimal TRANBPTRRATE { get; set; }

        [Required]
        [Column("TRANBMRP", TypeName = "numeric")]
        public decimal TRANBMRP { get; set; }

        [Required]
        [Column("TRANBGAMT", TypeName = "numeric")]
        public decimal TRANBGAMT { get; set; }

        [Required]
        [Column("TRANBCGSTEXPRN", TypeName = "numeric")]
        public decimal TRANBCGSTEXPRN { get; set; }

        [Required]
        [Column("TRANBSGSTEXPRN", TypeName = "numeric")]
        public decimal TRANBSGSTEXPRN { get; set; }

        [Required]
        [Column("TRANBIGSTEXPRN", TypeName = "numeric")]
        public decimal TRANBIGSTEXPRN { get; set; }

        [Required]
        [Column("TRANBCGSTAMT", TypeName = "numeric")]
        public decimal TRANBCGSTAMT { get; set; }

        [Required]
        [Column("TRANBSGSTAMT", TypeName = "numeric")]
        public decimal TRANBSGSTAMT { get; set; }

        [Required]
        [Column("TRANBIGSTAMT", TypeName = "numeric")]
        public decimal TRANBIGSTAMT { get; set; }

        [Required]
        [Column("TRANBNAMT", TypeName = "numeric")]
        public decimal TRANBNAMT { get; set; }

        [Required]
        [Column("TRANBPID")]
        public int TRANBPID { get; set; }

        [Required]
        [Column("TRANDPID")]
        public int TRANDPID { get; set; }

        [Required]
        [Column("TRANPTQTY")]
        public int TRANPTQTY { get; set; }

        [Column("TRANBLMID")]
        public int? TRANBLMID { get; set; }

        [ForeignKey("TRANDID")]
        public virtual TransactionDetail TransactionDetail { get; set; }
    }
}
