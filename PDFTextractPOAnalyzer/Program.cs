using Amazon;
using Microsoft.Extensions.Configuration;
using PDFTextractPOAnalyzer;
using PDFTextractPOAnalyzer.Model;

public class Program
{
    public static async Task Main(string[] args)
    {
        var region = RegionEndpoint.APSoutheast2;
        string filePath = @"D:\\Attachments\Purchase_Order_No_42363.pdf";

        //Set up the config to load the user secrets
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddUserSecrets<Program>(true)
            .Build();

        var textractFacade = new AwsTextractFacade(config, region);

        try
        {
            Deal deal = await textractFacade.UploadPdfAndExtractExpensesAsync(filePath);
            Console.WriteLine($"Textract job started with ID: {deal}");

            // Monitor job status and retrieve results...
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}