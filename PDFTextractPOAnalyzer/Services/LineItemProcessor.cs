using CoreLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDFTextractPOAnalyzer.Services
{
    public class LineItemProcessor : ILineItemProcessor
    {
        public List<LineItems> FixQtyAndDescription(List<LineItems> lineItems)
        {
            for (var i = 0; i < lineItems.Count; i++)
            {
                bool xFound = lineItems.Any(x => x.Quantity == 0 && (x.SKU != null && x.SKU.ToLower().Contains("x") || x.Name != null && x.Name.ToLower().Contains("x")));
                if (xFound)
                {
                    if (lineItems[i].Name != null && lineItems[i].Name.ToLower().Contains("x"))
                    {
                        lineItems[i] = ProcessSplitValues(lineItems[i], lineItems[i].Name.ToLower().Split('x'));
                    }
                    else if (lineItems[i].SKU != null && lineItems[i].SKU.ToLower().Contains("x"))
                    {
                        lineItems[i] = ProcessSplitValues(lineItems[i], lineItems[i].SKU.ToLower().Split('x'));
                    }
                }
            }

            return lineItems;
        }

        private static LineItems ProcessSplitValues(LineItems item, string[] splitValues)
        {
            LineItems lineItems = new LineItems();
            foreach (var value in splitValues)
            {
                if (IsNumber(value))
                {
                    lineItems.Quantity = Convert.ToDecimal(value);
                }
                else
                {
                    lineItems.Name = value.Trim();
                }
            }

            return lineItems;
        }

        public static bool IsNumber(string input)
        {
            return decimal.TryParse(input, out _);
        }
    }

}
