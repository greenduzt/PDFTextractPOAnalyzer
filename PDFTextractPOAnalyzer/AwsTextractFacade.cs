using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using Microsoft.Extensions.Configuration;
using PDFTextractPOAnalyzer.Core;
using PDFTextractPOAnalyzer.Model;
using System.Text.RegularExpressions;
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
                    Deal deal = ProcessResponse(response); // Handle the response
                }
                else
                {
                    Console.WriteLine($"Textract job failed with status: {jobStatus}");
                }

                return jobId;
            }
        }
        private Deal ProcessResponse(GetExpenseAnalysisResponse response)
        {
            Deal deal = new Deal();
            deal.Company = new Company();
            deal.DeliveryAddress = new Address();
            deal.LineItems = new List<LineItems>();

            foreach (var itemR in response.ExpenseDocuments)
            {
                Console.WriteLine("***START OF ORDER***");
                Console.WriteLine("***SUMMARY FIELDS***");
                foreach (var item in itemR.SummaryFields)
                {
                    Console.WriteLine("[" + item.Type.Text + "] " + item.LabelDetection?.Text + " : " + item.ValueDetection.Text ?? "");
                    if (item.Type.Text.Equals("PO_NUMBER"))
                    {
                        deal.PurchaseOrderNo = item.ValueDetection.Text;
                    }
                    else if (item.Type.Text.Equals("VENDOR_NAME"))
                    {
                        deal.Company.Name = item.ValueDetection.Text;
                    }
                    else if (item.Type.Text.Equals("RECEIVER_ADDRESS"))
                    {
                        //Get the address details                        
                        AddressSplitter.SetAddress(item.ValueDetection.Text.Replace('\n', ' '));
                        deal.DeliveryAddress = AddressSplitter.GetAddress();
                    }
                    else if (item.Type.Text.Equals("SUBTOTAL"))
                    {
                        deal.SubTotal = decimal.Parse(item.ValueDetection.Text.Trim('$'));
                    }
                    else if (item.Type.Text.Equals("TAX"))
                    {
                        deal.Tax = decimal.Parse(item.ValueDetection.Text.Trim('$'));
                    }
                    else if (item.Type.Text.Equals("TOTAL"))
                    {
                        deal.Total = decimal.Parse(item.ValueDetection.Text.Trim('$'));
                    }
                    else if (item.Type.Text.Equals("DELIVERY_DATE"))
                    {
                        deal.DeliveryDate = item.ValueDetection.Text;
                    }
                    else if (item.Type.Text.Equals("VENDOR_ABN_NUMBER"))
                    {
                        //remove spaces from ABN
                        string a = Regex.Replace(item.ValueDetection.Text, @"\s+", "");
                        //check if the ABN is not A1Rubber ABN
                        if (!a.Equals("85663589062"))
                        {
                            deal.Company.ABN = a;
                        }
                    }
                    else if (item.Type.Text.Equals("VENDOR_URL"))
                    {
                        deal.Company.Domain = item.ValueDetection.Text;
                    }
                }

                Console.WriteLine("***END OF SUMMARY FIELDS***");
                Console.WriteLine("***LINE ITEMS***");

                foreach (var itemLIG in itemR.LineItemGroups)
                {
                    LineItems lineItem = new LineItems();

                    foreach (var itemLI in itemLIG.LineItems)
                    {
                        string skuToCheck = string.Empty, expenseRawToCheck = string.Empty;

                        foreach (var itemLIE in itemLI.LineItemExpenseFields)
                        {
                            if (itemLIE.Type.Text.Equals("PRODUCT_CODE"))
                            {
                                lineItem.SKU = itemLIE.ValueDetection.Text;
                            }
                            else if (itemLIE.Type.Text.Equals("ITEM"))
                            {
                                lineItem.Name = itemLIE.ValueDetection.Text;
                            }
                            else if (itemLIE.Type.Text.Equals("QUANTITY"))
                            {
                                lineItem.Quantity = Convert.ToInt16(itemLIE.ValueDetection.Text);
                            }
                            else if (itemLIE.Type.Text.Equals("UNIT_PRICE"))
                            {
                                lineItem.UnitPrice = decimal.Parse(itemLIE.ValueDetection.Text.Trim('$'));
                            }
                            else if (itemLIE.Type.Text.Equals("PRICE"))
                            {

                            }
                            else if (itemLIE.Type.Text.Equals("EXPENSE_ROW"))
                            {
                                lineItem.ExpenseRaw = itemLIE.ValueDetection.Text;
                            }
                            else
                            {
                                //Error
                                deal.OrderNotes += "Incorrect line items";
                            }
                        }
                    }
                    deal.LineItems.Add(lineItem);
                }
               
            }

            return deal;
        }
    }
}
