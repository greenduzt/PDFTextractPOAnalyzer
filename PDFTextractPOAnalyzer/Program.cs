using Amazon;
using Microsoft.Extensions.Configuration;
using PDFTextractPOAnalyzer;
using PDFTextractPOAnalyzer.Model;
using Serilog.Events;
using Serilog;
using System.Text;

public class Program
{
    public static async Task Main(string[] args)
    {
        var region = RegionEndpoint.APSoutheast2;
        string filePath = @"D:\\Attachments\test_po1.pdf";

        //Set up the config to load the user secrets
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddUserSecrets<Program>(true)
            .Build();

        //Configure the logging file
        Log.Logger = new LoggerConfiguration()
               .MinimumLevel.Debug()
               .WriteTo.File($"{config["Logging:Path"]} - {DateTime.Now.ToString("yyyyMMdd_HHmmss")}.txt",
                   rollingInterval: RollingInterval.Day,
                   restrictedToMinimumLevel: LogEventLevel.Debug,
                   shared: true)
               .CreateLogger();

        Log.Information("---PDFAnalyzer Started---");

        var textractFacade = new AwsTextractFacade(config, region);

        try
        {
            Deal deal = await textractFacade.UploadPdfAndExtractExpensesAsync(filePath);         
        }
        catch (Exception ex)
        {
            Log.Error($"Error: {ex.Message}");
        }
        finally
        {
            Log.Information("---PDFAnalyzer Ended---");
            // Close and flush the Serilog logger
            Log.CloseAndFlush();
        }
    }
}