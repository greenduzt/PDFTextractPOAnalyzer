using CoreLibrary.Models;
using PDFTextractPOAnalyzer;

public class Program
{
    public static async Task Main(string[] args)
    {
        ProcessPdf processPdf = new ProcessPdf();
        await processPdf.ProcessPdfAsync(new Email());
    }
}