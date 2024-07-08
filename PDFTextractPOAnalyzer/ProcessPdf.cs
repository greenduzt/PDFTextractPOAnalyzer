using Amazon;
using CoreLibrary.Models;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace PDFTextractPOAnalyzer
{
    public class ProcessPdf
    {
        private readonly AwsTextractFacade _textractFacade;
        private readonly string _bucketName;

        public ProcessPdf(AwsTextractFacade textractFacade, IConfiguration config)
        {
            _textractFacade = textractFacade;
            _bucketName = config["AWS:BucketName"];
        }

        public async Task<Deal> ProcessPdfAsync(Email email)
        {
            Log.Information("---PDFAnalyzer Started---");

            if (email == null)
            {
                Log.Error("Email object is null");
                return null;
            }

            string filePath = Path.Combine(email.FilePath, email.FileName);

            try
            {
                Deal deal = await _textractFacade.UploadPdfAndExtractPOAsync(filePath, _bucketName);
                return deal;
            }
            catch (Exception ex)
            {
                Log.Error($"Error: {ex.Message}");
            }
            finally
            {
                Log.Information("---PDFAnalyzer Ended---");
            }

            return null;
        }
    }


}
