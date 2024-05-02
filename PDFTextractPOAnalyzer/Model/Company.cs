using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDFTextractPOAnalyzer.Model
{
    public class Company
    {
        public long CompanyID { get; set; }
        public string Name { get; set; }
        public string Domain { get; set; }
        public string CustomerType { get; set; }
        public string ABN { get; set; }
        public Address Address { get; set; }
    }
}
