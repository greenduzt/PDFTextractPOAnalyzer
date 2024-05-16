using Amazon;
using Microsoft.Extensions.Configuration;
using PDFTextractPOAnalyzer;

using Serilog.Events;
using Serilog;
using System.Text;

public class Program
{
    public static async Task Main(string[] args)
    {
        ProcessPdf processPdf = new ProcessPdf();
        await processPdf.ProcessPdfAsync();
    }
}