using Amazon;
using CoreLibrary.Models;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace PDFTextractPOAnalyzer
{
    public class ProcessPdf
    {
        private readonly IConfiguration _config;

        public ProcessPdf(IConfiguration config)
        {
            _config = config;
        }

        public async Task<Deal> ProcessPdfAsync(Email email)
        {            
            var region = RegionEndpoint.APSoutheast2;
         
            // Configure the logging file
            //Log.Logger = new LoggerConfiguration()
            //       .MinimumLevel.Debug()
            //       .WriteTo.File($"{_config["Logging:Path"]} - {DateTime.Now.ToString("yyyyMMdd_HHmmss")}.txt",
            //           rollingInterval: RollingInterval.Day,
            //           restrictedToMinimumLevel: LogEventLevel.Debug,
            //           shared: true)
            //       .CreateLogger();

            Log.Information("---PDFAnalyzer Started---");


            if (email == null)
            {
                Log.Error("Email object is null");
                return null;
            }

            string filePath = $"{email.FilePath}\\{email.FileName}";

            var textractFacade = new AwsTextractFacade(_config, region, email);

            try
            {
                Deal deal = await textractFacade.UploadPdfAndExtractExpensesAsync(filePath);

                return deal;
            }
            catch (Exception ex)
            {
                Log.Error($"Error: {ex.Message}");
            }
            finally
            {
                Log.Information("---PDFAnalyzer Ended---");
                // Close and flush the Serilog logger
                //Log.CloseAndFlush();
            }

            return null;
        }

    }
}
