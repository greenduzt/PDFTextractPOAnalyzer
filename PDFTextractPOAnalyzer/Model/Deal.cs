using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PDFTextractPOAnalyzer.Model
{
    public class Deal
    {
        public string DealName { get; set; }

        public string PurchaseOrderNo { get; set; }

        public Address DeliveryAddress { get; set; }

        public decimal SubTotal { get; set; }

        public decimal Tax { get; set; }

        public decimal Total { get; set; }

        public string DeliveryDate { get; set; }

        public Company Company { get; set; }

        public string OrderNotes { get; set; }

        public List<LineItems> LineItems { get; set; }
    }
}
