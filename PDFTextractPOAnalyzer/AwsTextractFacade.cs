using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using CoreLibrary.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using PDFTextractPOAnalyzer.Core;
using Serilog;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Formatting = Newtonsoft.Json.Formatting;
using S3Object = Amazon.Textract.Model.S3Object;

namespace PDFTextractPOAnalyzer
{
    public class AwsTextractFacade
    {
        private readonly IConfiguration _config;
        private readonly RegionEndpoint _region;
        private Email _email;
        public AwsTextractFacade(IConfiguration c, RegionEndpoint r, Email email)
        {
            _config = c;
            _region = r;
            _email = email;
        }

        public async Task<Deal> UploadPdfAndExtractDocumentAsync(string filePath)
        {
            var accessKey = _config["AWS:AccessKey"];
            var secretKey = _config["AWS:SecretKey"];
            var bucketName = _config["AWS:BucketName"];

            using (var s3Client = new AmazonS3Client(accessKey, secretKey, _region))
            {
                // Upload the PDF document to S3
                await UploadPdfToS3Async(s3Client, filePath);

                // Start a Document Analysis job to analyze the document
                var d = await StartDocumentAnalysisAsync(accessKey, secretKey, bucketName, filePath);

                return null; 
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
                    catch (Exception ex)
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

        //private async Task<Deal> ReadDocumentAsync(string accessKey, string secretKey)
        //{
        //    try
        //    {
        //        using (var comprehendClient = new AmazonComprehendClient(accessKey, secretKey, _region))
        //        {

        //        }
        //    }
        //    catch(AmazonTextractException ex)
        //    {

        //    }

        //}
        private async Task<string> StartDocumentAnalysisAsync(string accessKey, string secretKey, string bucketName, string filePath)
        {
            string analyzedString = "";
            try
            {
                using (var textractClient = new AmazonTextractClient(accessKey, secretKey, _region))
                {
                    var startRequest = new StartDocumentAnalysisRequest
                    {
                        DocumentLocation = new DocumentLocation
                        {
                            S3Object = new S3Object
                            {
                                Bucket = bucketName,
                                Name = Path.GetFileName(filePath)
                            }
                        },
                        FeatureTypes = new List<string> { "TABLES", "FORMS", "QUERIES" },
                        QueriesConfig = new QueriesConfig
                        {
                            Queries = new List<Query>
                    {
                        new Query { Text = "What is the purchase order number?", Alias = "PurchaseOrder" },
                        new Query { Text = "What are the product codes?", Alias = "ProductCode" },
                        new Query { Text = "What are the product names?", Alias = "ProductName" },
                        new Query { Text = "What are the quantities?", Alias = "Quantity" }
                    }
                        }
                    };

                    var startResponse = await textractClient.StartDocumentAnalysisAsync(startRequest);
                    string jobId = startResponse.JobId;
                    Log.Information($"Textract job started with ID: {jobId}");

                    // Check Textract job status and wait until it's complete
                    StringBuilder stringBuilder = new StringBuilder();
                    stringBuilder.Append("Document Analysis Started");
                    JobStatus jobStatus = JobStatus.IN_PROGRESS;
                    while (jobStatus == JobStatus.IN_PROGRESS)
                    {
                        var response = await textractClient.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest { JobId = jobId });
                        jobStatus = response.JobStatus;
                        await Task.Delay(TimeSpan.FromSeconds(5)); // Wait for 5 seconds before checking again
                        stringBuilder.Append("Checked at : " + DateTime.Now + " " + jobStatus.ToString());
                    }

                    Log.Information(stringBuilder.ToString());

                    if (jobStatus == JobStatus.SUCCEEDED)
                    {
                        Log.Information("Textract job succeeded. Retrieving results...");
                        // Fetch all pages of the response
                        var response = await textractClient.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest { JobId = jobId });
                        var allBlocks = new List<Block>();
                        allBlocks.AddRange(response.Blocks);

                        while (!string.IsNullOrEmpty(response.NextToken))
                        {
                            response = await textractClient.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest { JobId = jobId, NextToken = response.NextToken });
                            allBlocks.AddRange(response.Blocks);
                        }

                        analyzedString = ProcessDocumentAnalysisResponse(allBlocks); // Handling the response                        
                    }
                    else
                    {
                        Log.Error($"Textract job failed with status: {jobStatus}");
                    }

                    return analyzedString;
                }
            }
            catch (AmazonTextractException ex)
            {
                Log.Error(ex, "Error occurred while communicating with Amazon Textract: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An unexpected error occurred during document analysis.");
            }

            return null;
        }

        private string ProcessDocumentAnalysisResponse(List<Block> blocks)
        {
            StringBuilder resultBuilder = new StringBuilder();
            List<KeyValuePair<string, string>> headerMappings = RetrieveHeaderMappingsFromDatabase();
            // Find all table blocks
            var tableBlocks = blocks.Where(b => b.BlockType == "TABLE").ToList();

            foreach (var tableBlock in tableBlocks)
            {
                resultBuilder.AppendLine("Detected Table:");

                // Find all the rows in the table
                var rowMap = new Dictionary<int, Dictionary<int, string>>();

                // Get all child blocks (which are cells) of the table
                var tableRelationships = tableBlock.Relationships.Where(r => r.Type == "CHILD").SelectMany(r => r.Ids).ToList();
                var cellBlocks = blocks.Where(b => tableRelationships.Contains(b.Id) && b.BlockType == "CELL").ToList();

                foreach (var cellBlock in cellBlocks)
                {
                    var rowIndex = cellBlock.RowIndex;
                    var columnIndex = cellBlock.ColumnIndex;

                    if (!rowMap.ContainsKey(rowIndex))
                    {
                        rowMap[rowIndex] = new Dictionary<int, string>();
                    }

                    // Find the text associated with the cell
                    var cellText = string.Join(" ", cellBlock.Relationships
                        .Where(r => r.Type == "CHILD")
                        .SelectMany(r => r.Ids)
                        .Select(id => blocks.FirstOrDefault(b => b.Id == id && b.BlockType == "WORD"))
                        .Where(b => b != null)
                        .Select(b => b.Text));

                    rowMap[rowIndex][columnIndex] = cellText;
                }

                var lineItems = MapToLineItems(rowMap,headerMappings);

                // Now, map each row to column names
                foreach (var row in rowMap)
                {
                    resultBuilder.AppendLine($"Row {row.Key}:");
                    Console.WriteLine($"Row {row.Key}:");

                    foreach (var column in row.Value)
                    {
                        resultBuilder.AppendLine($"Column {column.Key}: {column.Value}");
                        Console.WriteLine($"Column {column.Key}: {column.Value}");
                    }

                                      
                }
            }

            return resultBuilder.ToString();
        }

        public static List<LineItems> MapToLineItems(Dictionary<int, Dictionary<int, string>> rowMap, List<KeyValuePair<string, string>> headerMappings)
        {
            var lineItems = new List<LineItems>();
            int headerRowKey = -1;
            var headerPositions = new Dictionary<string, int>();

            // Identify header row and positions of relevant headers
            foreach (var row in rowMap)
            {
                foreach (var cell in row.Value)
                {
                    if (IsHeader(cell.Value, headerMappings))
                    {
                        headerRowKey = row.Key;
                        foreach (var headerCell in row.Value)
                        {
                            string headerValue = headerCell.Value.ToLower();
                            if (!headerPositions.ContainsKey(headerValue))
                            {
                                headerPositions[headerValue] = headerCell.Key;
                            }
                        }
                        break;
                    }
                }

                if (headerRowKey != -1) break;
            }

            if (headerRowKey == -1)
            {
                Console.WriteLine("No header row found");
                return lineItems; // No header row found, return empty list
            }

            Console.WriteLine($"Header row found at row {headerRowKey}");
            Console.WriteLine("Header positions:");
            foreach (var header in headerPositions)
            {
                Console.WriteLine($"{header.Key}: {header.Value}");
            }

            // Define a dictionary to map header names to actions
            var headerActions = new Dictionary<string, Action<LineItems, string>>();

            // Populate header actions based on dynamic mappings
            foreach (var headerMapping in headerMappings)
            {
                string headerName = headerMapping.Key;
                string propertyName = headerMapping.Value;

                if (propertyName == "SKU")
                {
                    headerActions[headerName] = (lineItem, value) => 
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            lineItem.SKU = value;
                        }
                    };
                }
                else if (propertyName == "Name")
                {
                    headerActions[headerName] = (lineItem, value) =>
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            lineItem.Name = value;
                        }
                    };
                }
                else if (propertyName == "Quantity")
                {
                    headerActions[headerName] = (lineItem, value) =>
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            lineItem.Quantity = Convert.ToDecimal(RemoveNonNumeric(value));
                        }
                    };
                }
                else if (propertyName == "UnitPrice")
                {
                    headerActions[headerName] = (lineItem, value) =>
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            lineItem.UnitPrice = Convert.ToDecimal(RemoveNonNumeric(value));
                        }
                    };
                }
                else if (propertyName == "Discount")
                {
                    headerActions[headerName] = (lineItem, value) =>
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            lineItem.Discount = Convert.ToDecimal(RemoveNonNumeric(value));
                        }
                    };
                }
                else if (propertyName == "NetPrice")
                {
                    headerActions[headerName] = (lineItem, value) =>
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            lineItem.NetPrice = Convert.ToDecimal(RemoveNonNumeric(value));
                        }
                    };
                }
            }
            // Iterate through product rows (rows after the header row)
            foreach (var row in rowMap)
            {
                if (row.Key <= headerRowKey) continue; // Skip header row and rows before it

                // Check if the row contains enough cells to be considered a line item row
                if (row.Value.Count < headerPositions.Count) continue;

                var lineItem = new LineItems();

                foreach (var cell in row.Value)
                {
                    foreach (var header in headerPositions)
                    {
                        if (cell.Key == header.Value && headerActions.TryGetValue(header.Key, out var action))
                        {
                            if (!string.IsNullOrWhiteSpace(cell.Value))
                            {
                                lineItem.ExpenseRaw += cell.Value + " ";
                            }
                            action(lineItem, cell.Value);
                            break; // Once matched and processed, break out of inner loop
                        }
                    }
                }

                // Only add line item if SKU and Name are not null or empty
                if (!string.IsNullOrEmpty(lineItem.SKU) || !string.IsNullOrEmpty(lineItem.Name))
                {
                    lineItems.Add(lineItem);
                }
            }

            Console.WriteLine($"Total line items: {lineItems.Count}");
            return lineItems;
        }
        private static bool IsHeader(string value, List<KeyValuePair<string, string>> headerMappings)
        {
            string normalized = value.ToLower();
            return headerMappings.Any(x => x.Key.Equals(normalized));
        }

        public static List<KeyValuePair<string, string>> RetrieveHeaderMappingsFromDatabase()
        {
            var headerMappings = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("part #", "SKU" ),
            new KeyValuePair<string, string>("vendor code", "SKU" ),
            new KeyValuePair<string, string>("part id", "SKU"),
            new KeyValuePair<string, string>("product code", "SKU" ),
            new KeyValuePair<string, string>("budget code", "SKU" ),
            new KeyValuePair<string, string>("item", "SKU" ),
            
            //new KeyValuePair<string, string>("item", "Name" ),
            new KeyValuePair<string, string>("description", "Name" ),

            new KeyValuePair<string, string>("quantity", "Quantity" ),
            new KeyValuePair<string, string>("qty", "Quantity" ),
            new KeyValuePair<string, string>("amount ordered", "Quantity" ),
            new KeyValuePair<string, string>("quantity ordered", "Quantity" ),

            new KeyValuePair<string, string>("unit price", "UnitPrice" ),
            new KeyValuePair<string, string>("quoted unit price", "UnitPrice" ),
            new KeyValuePair<string, string>("unit cost", "UnitPrice" ),
            new KeyValuePair<string, string>("quoted price", "UnitPrice" ),

            new KeyValuePair<string, string>( "discount", "Discount" ),

            new KeyValuePair<string, string>("net price", "NetPrice" )

        };

            return headerMappings;
        }

        public static decimal RemoveNonNumeric(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return 0;
            }

            // Replace all non-numeric characters except for the first '.' and '-'
            string cleanedInput = Regex.Replace(input, @"[^\d.-]", "");

            // Handle cases with multiple periods or dashes
            int firstDotIndex = cleanedInput.IndexOf('.');
            int firstDashIndex = cleanedInput.IndexOf('-');

            // Remove additional periods
            if (firstDotIndex != -1)
            {
                cleanedInput = cleanedInput.Substring(0, firstDotIndex + 1) +
                               cleanedInput.Substring(firstDotIndex + 1).Replace(".", "");
            }

            // Remove additional dashes
            if (firstDashIndex != -1)
            {
                cleanedInput = cleanedInput.Substring(0, firstDashIndex + 1) +
                               cleanedInput.Substring(firstDashIndex + 1).Replace("-", "");
            }

            // Convert the cleaned string to decimal
            if (decimal.TryParse(cleanedInput, out decimal result))
            {
                return result;
            }

            return 0;
            //throw new FormatException($"Input string '{input}' was not in a correct format.");
        }


        //private string ProcessDocumentAnalysisResponse(List<Block> blocks)
        //{
        //    StringBuilder analyzedString = new StringBuilder();
        //    foreach (var block in blocks)
        //    {
        //        if (block.BlockType == "TABLE")
        //        {
        //            analyzedString.AppendLine("Detected Table:");
        //            foreach (var relationship in block.Relationships)
        //            {
        //                if (relationship.Type == "CHILD")
        //                {
        //                    foreach (var childId in relationship.Ids)
        //                    {
        //                        var cell = blocks.FirstOrDefault(b => b.Id == childId && b.BlockType == "CELL");
        //                        if (cell != null)
        //                        {
        //                            string cellText = GetTextFromCell(blocks, cell);
        //                            analyzedString.AppendLine($"Cell Text: {cellText}");
        //                            analyzedString.AppendLine($"Cell Confidence: {cell.Confidence}");
        //                            analyzedString.AppendLine($"Cell Geometry: {cell.Geometry}");
        //                            analyzedString.AppendLine($"Cell ID: {cell.Id}");

        //                            Console.WriteLine($"Cell Text: {cellText}");
        //                            Console.WriteLine($"Cell Confidence: {cell.Confidence}");
        //                            Console.WriteLine($"Cell Geometry: {cell.Geometry}");
        //                            Console.WriteLine($"Cell ID: {cell.Id}");
        //                        }
        //                        else
        //                        {
        //                            analyzedString.AppendLine($"Cell with ID {childId} not found or is not a CELL type block.");
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    return analyzedString.ToString();
        //}

        private string GetTextFromCell(List<Block> blocks, Block cell)
        {
            StringBuilder cellText = new StringBuilder();
            if (cell.Relationships != null)
            {
                foreach (var relationship in cell.Relationships)
                {
                    if (relationship.Type == "CHILD")
                    {
                        foreach (var childId in relationship.Ids)
                        {
                            var word = blocks.FirstOrDefault(b => b.Id == childId && b.BlockType == "WORD");
                            if (word != null)
                            {
                                cellText.Append(word.Text + " ");
                            }
                        }
                    }
                }
            }
            return cellText.ToString().Trim();
        }


        //private async Task<string> StartDocumentAnalysisAsync(string accessKey, string secretKey, string bucketName, string filePath)
        //{
        //    string analyzedString = "";
        //    try
        //    {
        //        using (var textractClient = new AmazonTextractClient(accessKey, secretKey, _region))
        //        {
        //            var startRequest = new StartDocumentAnalysisRequest
        //            {
        //                DocumentLocation = new DocumentLocation
        //                {
        //                    S3Object = new S3Object
        //                    {
        //                        Bucket = bucketName,
        //                        Name = Path.GetFileName(filePath)
        //                    }
        //                },
        //                FeatureTypes = new List<string> { "TABLES", "FORMS", "QUERIES" },
        //                QueriesConfig = new QueriesConfig
        //                {
        //                    Queries = new List<Query>
        //                    {
        //                        new Query { Text = "What is the purchase order number?", Alias = "PurchaseOrder" },
        //                        new Query { Text = "What are the product codes?", Alias = "ProductCode" },
        //                        new Query { Text = "What are the product names?", Alias = "ProductName" },
        //                        new Query { Text = "What are the quantities?", Alias = "Quantity" }
        //                    }
        //                }
        //            };

        //            var startResponse = await textractClient.StartDocumentAnalysisAsync(startRequest);
        //            string jobId = startResponse.JobId;
        //            Log.Information($"Textract job started with ID: {jobId}");

        //            // Check Textract job status and wait until it's complete
        //            StringBuilder stringBuilder = new StringBuilder();
        //            stringBuilder.Append("Document Analysis Started");
        //            JobStatus jobStatus = JobStatus.IN_PROGRESS;
        //            while (jobStatus == JobStatus.IN_PROGRESS)
        //            {
        //                var response = await textractClient.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest { JobId = jobId });
        //                jobStatus = response.JobStatus;
        //                await Task.Delay(TimeSpan.FromSeconds(5)); // Wait for 5 seconds before checking again
        //                stringBuilder.Append("Checked at : " + DateTime.Now + " " + jobStatus.ToString());
        //            }

        //            Log.Information(stringBuilder.ToString());

        //            if (jobStatus == JobStatus.SUCCEEDED)
        //            {
        //                Log.Information("Textract job succeeded. Retrieving results...");
        //                // Processing the response to retrieve the extracted data
        //                var response = await textractClient.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest { JobId = jobId });
        //                analyzedString = ProcessDocumentAnalysisResponse(response); // Handling the response                        
        //            }
        //            else
        //            {
        //                Log.Error($"Textract job failed with status: {jobStatus}");
        //            }

        //            return analyzedString;
        //        }
        //    }
        //    catch (AmazonTextractException ex)
        //    {
        //        Log.Error(ex, "Error occurred while communicating with Amazon Textract: {Message}", ex.Message);
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "An unexpected error occurred during document analysis.");
        //    }

        //    return null;
        //}

        //private string ProcessDocumentAnalysisResponse(GetDocumentAnalysisResponse response)
        //{
        //    var sb = new StringBuilder();


        //    foreach (var block in response.Blocks)
        //    {
        //        if (block.BlockType == "TABLE")
        //        {
        //            var table = block as Block;
        //            Console.WriteLine("Detected Table:");

        //            foreach (var relationship in table.Relationships)
        //            {
        //                if (relationship.Type == "CHILD")
        //                {
        //                    foreach (var childId in relationship.Ids)
        //                    {
        //                        var cell = response.Blocks.FirstOrDefault(b => b.Id == childId && b.BlockType == "CELL") as Block;
        //                        if (cell != null)
        //                        {
        //                            string cellText = cell.Text ?? ""; // Ensure cellText is not null
        //                            Console.WriteLine($"Cell Text: {cellText}");
        //                            Console.WriteLine($"Cell Confidence: {cell.Confidence}");
        //                            Console.WriteLine($"Cell Geometry: {cell.Geometry}");
        //                            Console.WriteLine($"Cell ID: {cell.Id}");
        //                        }
        //                        else
        //                        {
        //                            Console.WriteLine($"Cell with ID {childId} not found or is not a CELL type block.");
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }




        //    // Process TABLE blocks
        //    //var tableBlocks = response.Blocks.Where(b => b.BlockType == BlockType.TABLE).ToList();
        //    //foreach (var table in tableBlocks)
        //    //{
        //    //    sb.AppendLine("Table:");
        //    //    sb.AppendLine(ProcessTable(table, response.Blocks));
        //    //}

        //    // Process FORM blocks
        //    var formBlocks = response.Blocks.Where(b => b.BlockType == BlockType.KEY_VALUE_SET && b.EntityTypes.Contains("KEY")).ToList();
        //    foreach (var form in formBlocks)
        //    {
        //        sb.AppendLine("Form:");
        //        sb.AppendLine(ProcessForm(form, response.Blocks));
        //    }

        //    // Process QUERY blocks
        //    var queryBlocks = response.Blocks.Where(b => b.BlockType == BlockType.QUERY).ToList();
        //    foreach (var query in queryBlocks)
        //    {
        //        sb.AppendLine("Query:");
        //        sb.AppendLine(ProcessQuery(query, response.Blocks));
        //    }

        //    // Process LAYOUT blocks
        //    var layoutBlocks = response.Blocks.Where(b => b.BlockType == BlockType.PAGE).ToList();
        //    foreach (var layout in layoutBlocks)
        //    {
        //        sb.AppendLine("Layout:");
        //        sb.AppendLine(ProcessLayout(layout, response.Blocks));
        //    }

        //    return sb.ToString();
        //}

        private string ProcessTable(Block table, List<Block> blocks)
        {
            var sb = new StringBuilder();


            //var cellBlocks = blocks.Where(b => b.BlockType == BlockType.CELL && b.Relationships.Any(r => r.Ids.Contains(table.Id))).ToList();
            var cellBlocks = blocks.Where(b => b.BlockType == BlockType.CELL && b.Relationships != null && b.Relationships.Any(r => r.Ids.Contains(table.Id))).ToList();

            Console.WriteLine($"Cell blocks count: {cellBlocks.Count}");

            var rowGroups = cellBlocks.GroupBy(c => c.RowIndex);
            foreach (var rowGroup in rowGroups)
            {
                var row = new List<string>();
                foreach (var cell in rowGroup.OrderBy(c => c.ColumnIndex))
                {
                    var text = string.Join(" ", cell.Relationships?.SelectMany(r => r.Ids.Select(id => blocks.First(b => b.Id == id).Text)) ?? new List<string>());
                    row.Add(text);
                }
                sb.AppendLine(string.Join(", ", row));
            }
            return sb.ToString();
        }

        private string ProcessForm(Block form, List<Block> blocks)
        {
            var sb = new StringBuilder();
            var keyBlock = form;
            var valueBlock = blocks.FirstOrDefault(b => b.BlockType == BlockType.KEY_VALUE_SET && b.EntityTypes.Contains("VALUE") && keyBlock.Relationships?.FirstOrDefault()?.Ids.Contains(b.Id) == true);
            if (valueBlock != null)
            {
                var keyText = string.Join(" ", keyBlock.Relationships?.SelectMany(r => r.Ids.Select(id => blocks.First(b => b.Id == id).Text)) ?? new List<string>());
                var valueText = string.Join(" ", valueBlock.Relationships?.SelectMany(r => r.Ids.Select(id => blocks.First(b => b.Id == id).Text)) ?? new List<string>());
                sb.AppendLine($"{keyText}: {valueText}");
            }
            return sb.ToString();
        }

        private string ProcessQuery(Block query, List<Block> blocks)
        {
            var sb = new StringBuilder();
            var queryText = query.Query.Text;
            var queryAnswer = blocks.FirstOrDefault(b => b.BlockType == BlockType.QUERY_RESULT && query.Relationships?.FirstOrDefault()?.Ids.Contains(b.Id) == true)?.Text;
            sb.AppendLine($"{queryText}: {queryAnswer}");
            return sb.ToString();
        }

        private string ProcessLayout(Block layout, List<Block> blocks)
        {
            var sb = new StringBuilder();
            var lineBlocks = blocks.Where(b => b.BlockType == BlockType.LINE && b.Relationships.Any(r => r.Ids.Contains(layout.Id))).ToList();
            foreach (var line in lineBlocks)
            {
                sb.AppendLine(line.Text);
            }
            return sb.ToString();
        }


        private Deal ProcessResponse(Deal deal, GetDocumentAnalysisResponse response)
        {
            StringBuilder stringBuilder = new StringBuilder();
            deal.LineItems = new List<LineItems>();

            try
            {
                // Temporary storage for line items data
                LineItems currentItem = null;
                var extractedText = "";

                // Process each block to extract the necessary information
                foreach (var block in response.Blocks)
                {
                    if (!string.IsNullOrEmpty(block.Text))
                    {
                        extractedText += block.Text + "\n";
                        Console.WriteLine(block.Text);
                    }
                }

                Log.Information(stringBuilder.ToString());

                // Assigning the sales rep name and deal name
                deal.SalesRepName = _email.From;
                deal.DealName = string.IsNullOrWhiteSpace(deal.PurchaseOrderNo) ? _email.Subject : deal.PurchaseOrderNo;

                Log.Information("Document analysis completed.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while processing the Textract response");
            }

            if (!string.IsNullOrWhiteSpace(_email.Message))
            {
                deal.OrderNotes = ProcessEmailBody(_email.Message);
            }

            return deal;
        }


        private decimal ExtractDecimalOnly(string input)
        {
            string sanitizedInput = input.Replace(",", "");

            // Define a regular expression to capture the decimal value
            string pattern = @"(\d{1,3}(,\d{3})*|\d+)(\.\d{1,2})?\b";
            Regex regex = new Regex(pattern);
            Match match = regex.Match(sanitizedInput);

            if (match.Success)
            {
                // Convert the matched value to a decimal
                decimal result;
                if (decimal.TryParse(match.Value, out result))
                {
                    return result;
                }
            }

            Log.Error($"No decimal value found in the input string {input}");
            return 0;
        }

        private string ProcessEmailBody(string emailContent)
        {
            // Define a regex pattern to capture the email body up to specific keywords
            string pattern = @"\r\n\r\n\r\n(.*?)(Thanks|Regards|Best Wishes|Cheers|Thank You|From: )";
            Match match = Regex.Match(emailContent, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (match.Success)
            {
                // Extract and clean the body content
                string emailBody = match.Groups[1].Value.Trim();
                return System.Net.WebUtility.HtmlDecode(emailBody);
            }
            else
            {
                Log.Information("Could not extract the email body content.");
            }

            return "";
        }







        /**********************************************************************************************
         *
         * ************************************EXPENSE ANALYSIS****************************************
         *
         **********************************************************************************************/
        /*
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
                    deal.FilePath = filePath;
                    deal.FileName = startRequest.DocumentLocation.S3Object.Name;
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
                        deal = ProcessResponse(deal,response); // Handling the response                        
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
        private Deal ProcessResponse(Deal deal,GetExpenseAnalysisResponse response)
        {
            StringBuilder stringBuilder = new StringBuilder();
            deal.Company = new Company();
            deal.DeliveryAddress = new Address();
            deal.Emails = new List<string>();
            deal.LineItems = new List<LineItems>();            

            try
            {
                foreach (var itemR in response.ExpenseDocuments)
                {
                    Log.Information("Start of the order");
                    Log.Information("Summary Fields");
                   
                    foreach (var item in itemR.SummaryFields)
                    {
                        Console.WriteLine(item.Type.Text + " | " + item.ValueDetection.Text);

                        // Collecting email addresses to allocate sales rep
                        if (item.ValueDetection.Text.Contains('@'))
                        {
                            deal.Emails.Add(item.ValueDetection.Text);
                        }
                        Console.WriteLine(item.ValueDetection.Text);
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
                                deal.SubTotal = ExtractDecimalOnly(item.ValueDetection.Text);
                                stringBuilder.Append($"SubTotal : {deal.SubTotal}");
                                found = true;
                                break;
                            case "TAX":
                                deal.Tax = ExtractDecimalOnly(item.ValueDetection.Text);
                                stringBuilder.Append($"Tax : {deal.Tax}");
                                found = true;
                                break;
                            case "TOTAL":
                                deal.Total = ExtractDecimalOnly(item.ValueDetection.Text);
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
                                if (!a.Equals("85663589062") && string.IsNullOrWhiteSpace(deal.Company.ABN))
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
                    // Constructing the deal name
                    //string dn = string.IsNullOrWhiteSpace(deal.PurchaseOrderNo) ? _email.Subject : deal.PurchaseOrderNo;
                    //deal.DealName = $"TEST ORDER - {deal.Company.Name} - {dn}";
                    deal.DealName = string.IsNullOrWhiteSpace(deal.PurchaseOrderNo) ? _email.Subject : deal.PurchaseOrderNo;

                    // Assigning the sales rep name
                    deal.SalesRepName = _email.From;

                    Log.Information("End Of Summary Fields");
                    Log.Information("Line Items");
                    bool itemFound = false;
                    stringBuilder.Clear();

                    foreach (var itemLIG in itemR.LineItemGroups)
                    {                        
                        stringBuilder.Clear();

                        foreach (var itemLI in itemLIG.LineItems)
                        {
                            stringBuilder.Clear();
                            LineItems lineItem = new LineItems();
                            foreach (var itemLIE in itemLI.LineItemExpenseFields)
                            {
                                Console.WriteLine(itemLIE.ValueDetection.Text);
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
                                        lineItem.Quantity = ExtractDecimalOnly(itemLIE.ValueDetection.Text);
                                        stringBuilder.Append($"Qty : {lineItem.Quantity}");
                                        itemFound = true;
                                        break;
                                    case "UNIT_PRICE":
                                        lineItem.UnitPrice = ExtractDecimalOnly(itemLIE.ValueDetection.Text.Trim('$'));
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
                            deal.LineItems.Add(lineItem);
                        }                                               
                    }                    
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while processing the Textract response");
            }

            if(!string.IsNullOrWhiteSpace(_email.Message))
            {
                deal.OrderNotes= ProcessEmailBody(_email.Message);
            }

            return deal;
        }

        private decimal ExtractDecimalOnly(string input)
        {
            string sanitizedInput = input.Replace(",", "");

            // Define a regular expression to capture the decimal value
            string pattern = @"(\d{1,3}(,\d{3})*|\d+)(\.\d{1,2})?\b";
            Regex regex = new Regex(pattern);
            Match match = regex.Match(sanitizedInput);

            if (match.Success)
            {
                // Convert the matched value to a decimal
                decimal result;
                if (decimal.TryParse(match.Value, out result))
                {
                    return result;
                }
            }

            Log.Error($"No decimal value found in the input string {input}");
            return 0;
        }

        private string ProcessEmailBody(string emailContent)
        {
            // Define a regex pattern to capture the email body up to specific keywords
            string pattern = @"\r\n\r\n\r\n(.*?)(Thanks|Regards|Best Wishes|Cheers|Thank You|From: )";
            Match match = Regex.Match(emailContent, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (match.Success)
            {
                // Extract and clean the body content
                string emailBody = match.Groups[1].Value.Trim();
                return System.Net.WebUtility.HtmlDecode(emailBody);
            }
            else
            {
                Log.Information("Could not extract the email body content.");
            }

            return "";
        }
        */
    }
}
