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
         
            Log.Information("---PDFAnalyzer Started---");


            if (email == null)
            {
                Log.Error("Email object is null");
                return null;
            }

            //string filePath = $"{email.FilePath}\\{email.FileName}";
            string filePath = "d:\\attachments\\Clever Move - Purchase Order PO0026.pdf";

            var textractFacade = new AwsTextractFacade(_config, region, email);

            try
            {
                Deal deal = await textractFacade.UploadPdfAndExtractDocumentAsync(filePath);

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
