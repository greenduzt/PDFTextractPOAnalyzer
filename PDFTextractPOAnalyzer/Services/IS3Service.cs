using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDFTextractPOAnalyzer.Services
{
    public interface IS3Service
    {
        Task UploadPdfAsync(string filePath);
    }
}
