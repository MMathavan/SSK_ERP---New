using System;

namespace SSK_ERP.Models
{
    public class SalesOrderDatewiseDetailedRow
    {
        public DateTime SalesOrderDate { get; set; }
        public string SalesOrderNumber { get; set; }
        public string CustomerName { get; set; }
        public string SalesRefNo { get; set; }
        public string ItemName { get; set; }
        public decimal Qty { get; set; }
    }
}
