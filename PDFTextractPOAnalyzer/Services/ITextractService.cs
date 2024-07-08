using CoreLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDFTextractPOAnalyzer.Services
{
    public interface ITextractService
    {
        Task<List<LineItems>> AnalyzeDocumentAsync(string bucketName, string fileName);
        Task<Deal> AnalyzeExpenseAsync(string bucketName, string fileName);
    }
}
