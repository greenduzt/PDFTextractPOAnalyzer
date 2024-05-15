using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using Microsoft.Extensions.Configuration;
using PDFTextractPOAnalyzer.Core;
using PDFTextractPOAnalyzer.Model;
using Serilog;
using System;
using System.Text;
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

        public async Task<Deal> UploadPdfAndExtractExpensesAsync(string filePath)
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
            try
            {
                var bucketName = _config["AWS:BucketName"];
                var fileName = Path.GetFileName(filePath);

                if (fileName != null)
                {
                    byte[] fileBytes;
                    try
                    {
                        fileBytes = File.ReadAllBytes(filePath);
                    }
                    catch(Exception ex)
                    {
                        Log.Error(ex, "Error occurred while reading the file: {FilePath}", filePath);
                        return; // Exiting the method if file reading fails
                    }

                    using (var memoryStream = new MemoryStream(fileBytes))
                    {
                        var putRequest = new PutObjectRequest
                        {
                            BucketName = bucketName,
                            Key = fileName,
                            InputStream = memoryStream,
                            ContentType = "application/pdf"
                        };
                        await s3Client.PutObjectAsync(putRequest);

                        Log.Information("PDF uploaded successfully to S3.");
                    }
                }
                else
                {
                    Log.Information("File not available");
                    return;
                }
            }
            catch (AmazonS3Exception ex)
            {
                Log.Error(ex, "Error occurred while uploading PDF to S3: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An unexpected error occurred while uploading PDF to S3.");
            }
        }

        private async Task<Deal> StartExpenseAnalysisAsync(string accessKey, string secretKey, string bucketName, string filePath)
        {
            try
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

                    Deal deal = new Deal();
                    var startResponse = await textractClient.StartExpenseAnalysisAsync(startRequest);
                    string jobId = startResponse.JobId;
                    Log.Information($"Textract job started with ID: {jobId}");

                    // Check Textract job status and wait until it's complete
                    StringBuilder stringBuilder = new StringBuilder();
                    stringBuilder.Append("Expense Analysis Started");
                    JobStatus jobStatus = JobStatus.IN_PROGRESS;
                    while (jobStatus == JobStatus.IN_PROGRESS)
                    {
                        var response = await textractClient.GetExpenseAnalysisAsync(new GetExpenseAnalysisRequest { JobId = jobId });
                        jobStatus = response.JobStatus;
                        await Task.Delay(TimeSpan.FromSeconds(5)); // Wait for 5 seconds before checking again
                        stringBuilder.Append("Checked at : " + DateTime.Now + " " + jobStatus.ToString());
                    }

                    Log.Information(stringBuilder.ToString());

                    if (jobStatus == JobStatus.SUCCEEDED)
                    {
                        Log.Information("Textract job succeeded. Retrieving results...");
                        // Processing the response to retrieve the extracted expenses
                        var response = await textractClient.GetExpenseAnalysisAsync(new GetExpenseAnalysisRequest { JobId = jobId });
                        deal = ProcessResponse(response); // Handling the response
                    }
                    else
                    {
                        Log.Error($"Textract job failed with status: {jobStatus}");
                    }

                    return deal;
                }
            }
            catch (AmazonTextractException ex)
            {
                Log.Error(ex, "Error occurred while communicating with Amazon Textract: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An unexpected error occurred during expense analysis.");
            }

            return null;
        }
        private Deal ProcessResponse(GetExpenseAnalysisResponse response)
        {
            Deal deal = new Deal();
            deal.Company = new Company();
            deal.DeliveryAddress = new Address();
            deal.Emails = new List<string>();
            deal.LineItems = new List<LineItems>();
            StringBuilder stringBuilder = new StringBuilder();

            try
            {
                foreach (var itemR in response.ExpenseDocuments)
                {
                    Log.Information("Start of the order");
                    Log.Information("Summary Fields");
                   
                    foreach (var item in itemR.SummaryFields)
                    {
                        // Collecting email addresses to allocate sales rep
                        if (item.ValueDetection.Text.Contains('@'))
                        {
                            deal.Emails.Add(item.ValueDetection.Text);
                        }
                        bool found = false;
                        // Handling each summary field type
                        switch (item.Type.Text)
                        {
                            case "PO_NUMBER":
                                deal.PurchaseOrderNo = item.ValueDetection.Text;                                
                                stringBuilder.Append($"PO No : {deal.PurchaseOrderNo}");
                                found = true;
                                break;
                            case "VENDOR_NAME":
                                deal.Company.Name = item.ValueDetection.Text;
                                stringBuilder.Append($"Company : {deal.Company.Name}");
                                found= true;
                                break;
                            case "RECEIVER_ADDRESS":
                                // Getting the address details                        
                                AddressSplitter.SetAddress(item.ValueDetection.Text.Replace('\n', ' '));
                                deal.DeliveryAddress = AddressSplitter.GetAddress();
                                stringBuilder.Append($"Delivery Address : {deal.DeliveryAddress}");
                                found = true;  
                                break;
                            case "SUBTOTAL":
                                deal.SubTotal = decimal.Parse(item.ValueDetection.Text.Trim('$'));
                                stringBuilder.Append($"SubTotal : {deal.SubTotal}");
                                found = true;
                                break;
                            case "TAX":
                                deal.Tax = decimal.Parse(item.ValueDetection.Text.Trim('$'));
                                stringBuilder.Append($"Tax : {deal.Tax}");
                                found = true;
                                break;
                            case "TOTAL":
                                deal.Total = decimal.Parse(item.ValueDetection.Text.Trim('$'));
                                stringBuilder.Append($"Total : {deal.Total}");
                                found = true;
                                break;
                            case "DELIVERY_DATE":
                                deal.DeliveryDate = item.ValueDetection.Text;
                                stringBuilder.Append($"Delivery Date : {deal.DeliveryDate}");
                                found = true;
                                break;
                            case "VENDOR_ABN_NUMBER":
                                // Removing spaces from ABN
                                string a = Regex.Replace(item.ValueDetection.Text, @"\s+", "");
                                // Check if the ABN is not A1Rubber ABN
                                if (!a.Equals("85663589062"))
                                {
                                    deal.Company.ABN = a;
                                    stringBuilder.Append($"ABN : {deal.Company.ABN}");
                                    found = true;
                                }
                                break;
                            case "VENDOR_URL":
                                deal.Company.Domain = item.ValueDetection.Text;
                                stringBuilder.Append($"Domain : {deal.Company.Domain}");
                                found = true;
                                break;
                            default:                                
                                break;
                        }
                        if (item != itemR.SummaryFields.Last() && found)
                        {
                            stringBuilder.Append("|");
                        }   
                    }

                    Log.Information(stringBuilder.ToString());

                    //Constructing deal name
                    deal.DealName = $" {deal.Company} - {deal.PurchaseOrderNo}";
                    
                    Log.Information("End Of Summary Fields");
                    Log.Information("Line Items");
                    bool itemFound = false;
                    stringBuilder.Clear();

                    foreach (var itemLIG in itemR.LineItemGroups)
                    {
                        LineItems lineItem = new LineItems();
                        stringBuilder.Clear();

                        foreach (var itemLI in itemLIG.LineItems)
                        {
                            stringBuilder.Clear();
                            //stringBuilder.AppendLine();
                            foreach (var itemLIE in itemLI.LineItemExpenseFields)
                            {
                                itemFound = false;
                                switch (itemLIE.Type.Text)
                                {
                                    case "PRODUCT_CODE":
                                        lineItem.SKU = itemLIE.ValueDetection.Text;
                                        stringBuilder.Append($"Product Code : {lineItem.SKU}");
                                        itemFound = true;
                                        break;
                                    case "ITEM":
                                        lineItem.Name = itemLIE.ValueDetection.Text;
                                        stringBuilder.Append($"Name : {lineItem.Name}");
                                        itemFound = true;
                                        break;
                                    case "QUANTITY":
                                        lineItem.Quantity = Convert.ToInt16(itemLIE.ValueDetection.Text);
                                        stringBuilder.Append($"Qty : {lineItem.Quantity}");
                                        itemFound = true;
                                        break;
                                    case "UNIT_PRICE":
                                        lineItem.UnitPrice = decimal.Parse(itemLIE.ValueDetection.Text.Trim('$'));
                                        stringBuilder.Append($"Price : {lineItem.UnitPrice}");
                                        itemFound = true;
                                        break;
                                    case "EXPENSE_ROW":
                                        lineItem.ExpenseRaw = itemLIE.ValueDetection.Text;
                                        stringBuilder.Append($"Expense Row : {lineItem.ExpenseRaw}");
                                        itemFound = true;
                                        break;
                                    default:
                                        break;
                                }
                                if(itemLIE != itemLI.LineItemExpenseFields.Last() && itemFound)
                                {
                                    stringBuilder.Append("|");
                                }
                            }
                            Log.Information(stringBuilder.ToString());
                        }
                        deal.LineItems.Add(lineItem);                        
                    }                    
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while processing the Textract response");
            }

            return deal;
        }

    }
}
