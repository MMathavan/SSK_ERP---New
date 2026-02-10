using System;

namespace SSK_ERP.Models
{
    public class SalesOrderDatewiseConsolidatedRow
    {
        public string SalesOrderNumber { get; set; }
        public DateTime SalesOrderDate { get; set; }
        public string CustomerName { get; set; }
        public string SalesRefNo { get; set; }
        public decimal GrossAmt { get; set; }
        public decimal Total { get; set; }
    }
}
