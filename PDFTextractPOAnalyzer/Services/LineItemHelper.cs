using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PDFTextractPOAnalyzer.Services
{
    public static class LineItemHelper
    {
        public static decimal RemoveNonNumeric(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return 0;
            }

            string cleanedInput = Regex.Replace(input, @"[^\d.-]", "");

            int firstDotIndex = cleanedInput.IndexOf('.');
            int firstDashIndex = cleanedInput.IndexOf('-');

            if (firstDotIndex != -1)
            {
                cleanedInput = cleanedInput.Substring(0, firstDotIndex + 1) +
                               cleanedInput.Substring(firstDotIndex + 1).Replace(".", "");
            }

            if (firstDashIndex != -1)
            {
                cleanedInput = cleanedInput.Substring(0, firstDashIndex + 1) +
                               cleanedInput.Substring(firstDashIndex + 1).Replace("-", "");
            }

            if (decimal.TryParse(cleanedInput, out decimal result))
            {
                return result;
            }

            return 0;
        }
    }
}
