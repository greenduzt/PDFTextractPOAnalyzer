using CoreLibrary.Models;
using Microsoft.Extensions.Configuration;
using PDFTextractPOAnalyzer;

public class Program
{

    // For testing purposes only
    public static async Task Main(string[] args)
    {
        


        // Get the config details
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddUserSecrets<Program>(true)
            .Build();

        ProcessPdf processPdf = new ProcessPdf(config);
        await processPdf.ProcessPdfAsync(new Email());
    }

    


}