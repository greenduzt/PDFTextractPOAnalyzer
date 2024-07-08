using CoreLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDFTextractPOAnalyzer.Services
{
    public interface ILineItemProcessor
    {
        List<LineItems> FixQtyAndDescription(List<LineItems> lineItems);
    }
}
