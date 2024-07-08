using CoreLibrary.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PDFTextractPOAnalyzer;
using PDFTextractPOAnalyzer.Services;
using Serilog;
using Amazon;

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

        //ProcessPdf processPdf = new ProcessPdf(config);
        //await processPdf.ProcessPdfAsync(new Email());

        var accessKey = config["AWS:AccessKey"];
        var secretKey = config["AWS:SecretKey"];
        var region = RegionEndpoint.APSoutheast2;

        // Set up dependency injection
        var serviceProvider = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton<IS3Service>(sp => new S3Service(config, accessKey, secretKey, region))
            .AddSingleton<ITextractService>(sp => new TextractService(accessKey, secretKey, region))
            .AddSingleton<ILineItemProcessor, LineItemProcessor>()
            .AddSingleton<AwsTextractFacade>()
            .AddSingleton<ProcessPdf>()
            .BuildServiceProvider();

        try
        {
            // Get the ProcessPdf service from the service provider
            var processPdf = serviceProvider.GetService<ProcessPdf>();

            // Create an email instance with necessary details
            var email = new Email
            {
                FilePath = "d:\\attachments\\",
                FileName = "PURCHASE ORDER-58260.pdf"
            };

            // Process the PDF
            await processPdf.ProcessPdfAsync(email);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while processing the PDF.");
        }
        finally
        {
            // Ensure to flush and close the logger
            Log.CloseAndFlush();
        }
    }

    


}