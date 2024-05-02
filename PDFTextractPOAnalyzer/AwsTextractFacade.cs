using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using Microsoft.Extensions.Configuration;
using S3Object = Amazon.Textract.Model.S3Object;

namespace PDFTextractPOAnalyzer
{
    public class AwsTextractFacade
    {
        private readonly IConfiguration _config;
        private readonly RegionEndpoint _region;
        public AwsTextractFacade(IConfiguration c, RegionEndpoint r)
        {
            _config = c;          
            _region = r;
        }

        public async Task<string> UploadPdfAndExtractExpensesAsync(string filePath)
        {
            var accessKey = _config["AWS:AccessKey"];
            var secretKey = _config["AWS:SecretKey"];
            var bucketName = _config["AWS:BucketName"];

            using (var s3Client = new AmazonS3Client(accessKey, secretKey, _region))
            {
                // Upload the PDF document to S3
                await UploadPdfToS3Async(s3Client, filePath);

                // Start an Expense Analysis job to analyze the document
                return await StartExpenseAnalysisAsync(accessKey, secretKey, bucketName, filePath);
            }
        }

        private async Task UploadPdfToS3Async(AmazonS3Client s3Client, string filePath)
        {
            var bucketName = _config["AWS:BucketName"];
            var fileName = Path.GetFileName(filePath);
            using (var memoryStream = new MemoryStream(File.ReadAllBytes(filePath)))
            {
                var putRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = fileName,
                    InputStream = memoryStream,
                    ContentType = "application/pdf"
                };
                await s3Client.PutObjectAsync(putRequest);
            }
        }

        private async Task<string> StartExpenseAnalysisAsync(string accessKey, string secretKey, string bucketName, string filePath)
        {
            using (var textractClient = new AmazonTextractClient(accessKey, secretKey, _region))
            {
                var startRequest = new StartExpenseAnalysisRequest
                {
                    DocumentLocation = new DocumentLocation
                    {
                        S3Object = new S3Object
                        {
                            Bucket = bucketName,
                            Name = Path.GetFileName(filePath)
                        }
                    }
                };

                var startResponse = await textractClient.StartExpenseAnalysisAsync(startRequest);
                string jobId = startResponse.JobId;
                Console.WriteLine($"Textract job started with ID: {jobId}");

                // Check Textract job status and wait until it's complete
                JobStatus jobStatus = JobStatus.IN_PROGRESS;
                while (jobStatus == JobStatus.IN_PROGRESS)
                {
                    var response = await textractClient.GetExpenseAnalysisAsync(new GetExpenseAnalysisRequest { JobId = jobId });
                    jobStatus = response.JobStatus;
                    await Task.Delay(TimeSpan.FromSeconds(5)); // Wait for 5 seconds before checking again
                    Console.WriteLine("Checked at : " + DateTime.Now + " " + jobStatus.ToString());
                }

                if (jobStatus == JobStatus.SUCCEEDED)
                {
                    Console.WriteLine("Textract job succeeded. Retrieving results...");
                    // Process the response to retrieve the extracted expenses
                    var response = await textractClient.GetExpenseAnalysisAsync(new GetExpenseAnalysisRequest { JobId = jobId });
                    ProcessResponse(response); // Handle the response
                }
                else
                {
                    Console.WriteLine($"Textract job failed with status: {jobStatus}");
                }

                return jobId;
            }
        }
        private void ProcessResponse(GetExpenseAnalysisResponse response)
        {
            foreach (var itemR in response.ExpenseDocuments)
            {
                Console.WriteLine("***SUMMARY FIELDS***");
                foreach (var item in itemR.SummaryFields)
                {
                    if (item.Type.Text.Equals("RECEIVER_ADDRESS"))
                    {
                        // Handle RECEIVER_ADDRESS
                    }
                    Console.WriteLine("[" + item.Type.Text + "] " + item.LabelDetection?.Text + " : " + item.ValueDetection.Text ?? "");
                }
                Console.WriteLine("***END OF SUMMARY FIELDS***");

                foreach (var itemLIG in itemR.LineItemGroups)
                {
                    Console.WriteLine("***LINE ITEMS***");
                    foreach (var itemLI in itemLIG.LineItems)
                    {
                        foreach (var itemLIE in itemLI.LineItemExpenseFields)
                        {
                            Console.WriteLine("[" + itemLIE.Type.Text + "] " + itemLIE.LabelDetection?.Text + " : " + itemLIE.ValueDetection.Text ?? "");
                        }

                    }
                    Console.WriteLine("***END OF LINE ITEMS***");
                }
            }
        }
    }
}
