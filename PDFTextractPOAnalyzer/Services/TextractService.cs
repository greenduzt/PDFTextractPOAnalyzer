using Amazon.Textract.Model;
using Amazon.Textract;
using CoreLibrary.Models;
using Microsoft.Extensions.Configuration;
using Serilog;
using Amazon;
using PDFTextractPOAnalyzer.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace PDFTextractPOAnalyzer.Services
{
    public class TextractService : ITextractService
    {
        private readonly AmazonTextractClient _textractClient;

        public TextractService(string accessKey, string secretKey, RegionEndpoint region)
        {
            _textractClient = new AmazonTextractClient(accessKey, secretKey, region);
        }

        public async Task<List<LineItems>> AnalyzeDocumentAsync(string bucketName, string fileName)
        {
            var startRequest = new StartDocumentAnalysisRequest
            {
                DocumentLocation = new DocumentLocation
                {
                    S3Object = new S3Object
                    {
                        Bucket = bucketName,
                        Name = fileName
                    }
                },
                FeatureTypes = new List<string> { "TABLES", "FORMS" }
            };

            var startResponse = await _textractClient.StartDocumentAnalysisAsync(startRequest);
            string jobId = startResponse.JobId;
            Log.Information($"Textract job started with ID: {jobId}");

            return await GetDocumentAnalysisResultsAsync(jobId);
        }

        private async Task<List<LineItems>> GetDocumentAnalysisResultsAsync(string jobId)
        {
            List<LineItems> processedLineItems = new List<LineItems>();
            JobStatus docAnalysisJobStatus = JobStatus.IN_PROGRESS;

            while (docAnalysisJobStatus == JobStatus.IN_PROGRESS)
            {
                var docAnalysisResponse = await _textractClient.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest { JobId = jobId });
                docAnalysisJobStatus = docAnalysisResponse.JobStatus;
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            if (docAnalysisJobStatus == JobStatus.SUCCEEDED)
            {
                var allBlocks = new List<Block>();
                var response = await _textractClient.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest { JobId = jobId });
                allBlocks.AddRange(response.Blocks);

                while (!string.IsNullOrEmpty(response.NextToken))
                {
                    response = await _textractClient.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest { JobId = jobId, NextToken = response.NextToken });
                    allBlocks.AddRange(response.Blocks);
                }

                processedLineItems = ProcessDocumentAnalysisResponse(allBlocks);
            }
            else
            {
                Log.Error($"Textract job failed with status: {docAnalysisJobStatus}");
            }

            return processedLineItems;
        }

        public async Task<Deal> AnalyzeExpenseAsync(string bucketName, string fileName)
        {
            var startRequest = new StartExpenseAnalysisRequest
            {
                DocumentLocation = new DocumentLocation
                {
                    S3Object = new S3Object
                    {
                        Bucket = bucketName,
                        Name = fileName
                    }
                }
            };

            var startResponse = await _textractClient.StartExpenseAnalysisAsync(startRequest);
            string jobId = startResponse.JobId;
            Log.Information($"Textract job started with ID: {jobId}");

            return await GetExpenseAnalysisResultsAsync(jobId);
        }

        private async Task<Deal> GetExpenseAnalysisResultsAsync(string jobId)
        {
            JobStatus jobStatus = JobStatus.IN_PROGRESS;
            Deal deal = new Deal();

            while (jobStatus == JobStatus.IN_PROGRESS)
            {
                var response = await _textractClient.GetExpenseAnalysisAsync(new GetExpenseAnalysisRequest { JobId = jobId });
                jobStatus = response.JobStatus;
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            if (jobStatus == JobStatus.SUCCEEDED)
            {
                var response = await _textractClient.GetExpenseAnalysisAsync(new GetExpenseAnalysisRequest { JobId = jobId });
                deal = ProcessExpenseAnalysisResponse(response);
            }
            else
            {
                Log.Error($"Textract job failed with status: {jobStatus}");
            }

            return deal;
        }

        private List<LineItems> ProcessDocumentAnalysisResponse(List<Block> blocks)
        {
            List<KeyValuePair<string, string>> headerMappings = RetrieveHeaderMappingsFromDatabase();
            List<LineItems> processedLineItems = new List<LineItems>();

            // Find all table blocks
            var tableBlocks = blocks.Where(b => b.BlockType == "TABLE").ToList();
            if (tableBlocks.Count == 0)
            {
                Log.Warning("No tables found in the document.");
                return processedLineItems;
            }

            // Process each table block
            foreach (var tableBlock in tableBlocks)
            {
                var cellBlocks = GetCellBlocks(tableBlock, blocks);
                var rowMap = GetRowMap(cellBlocks, blocks);

                if (rowMap.Count > 0)
                {
                    processedLineItems.AddRange(MapToLineItems(rowMap, headerMappings));
                }
            }

            return processedLineItems;
        }

        private List<Block> GetCellBlocks(Block tableBlock, List<Block> blocks)
        {
            var tableRelationships = tableBlock.Relationships?.Where(r => r.Type == "CHILD").SelectMany(r => r.Ids).ToList();
            if (tableRelationships == null) return new List<Block>();

            return blocks.Where(b => tableRelationships.Contains(b.Id) && b.BlockType == "CELL").ToList();
        }

        private Dictionary<int, Dictionary<int, string>> GetRowMap(List<Block> cellBlocks, List<Block> blocks)
        {
            var rowMap = new Dictionary<int, Dictionary<int, string>>();

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
                    ?.Where(r => r.Type == "CHILD")
                    .SelectMany(r => r.Ids)
                    .Select(id => blocks.FirstOrDefault(b => b.Id == id && b.BlockType == "WORD"))
                    .Where(b => b != null)
                    .Select(b => b.Text));

                rowMap[rowIndex][columnIndex] = cellText;
            }

            return rowMap;
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
                Log.Information("No header row found");
                return lineItems; // No header row found, return empty list
            }

            Log.Information($"Header row found at row {headerRowKey}");
            Log.Information("Header positions:");
            foreach (var header in headerPositions)
            {
                Log.Information($"{header.Key}: {header.Value}");
            }

            var headerActions = new Dictionary<string, Action<LineItems, string>>();

            foreach (var headerMapping in headerMappings)
            {
                string headerName = headerMapping.Key;
                string propertyName = headerMapping.Value;

                if (propertyName == "SKU")
                {
                    headerActions[headerName] = (lineItem, value) => lineItem.SKU = value;
                }
                else if (propertyName == "Name")
                {
                    headerActions[headerName] = (lineItem, value) => lineItem.Name = value;
                }
                else if (propertyName == "Quantity")
                {
                    headerActions[headerName] = (lineItem, value) => lineItem.Quantity = LineItemHelper.RemoveNonNumeric(value);
                }
                else if (propertyName == "UnitPrice")
                {
                    headerActions[headerName] = (lineItem, value) => lineItem.UnitPrice = LineItemHelper.RemoveNonNumeric(value);
                }
                else if (propertyName == "Discount")
                {
                    headerActions[headerName] = (lineItem, value) => lineItem.Discount = LineItemHelper.RemoveNonNumeric(value);
                }
                else if (propertyName == "NetPrice")
                {
                    headerActions[headerName] = (lineItem, value) => lineItem.NetPrice = LineItemHelper.RemoveNonNumeric(value);
                }
            }

            foreach (var row in rowMap)
            {
                if (row.Key <= headerRowKey) continue;

                if (row.Value.Count < headerPositions.Count) continue;

                var lineItem = new LineItems();

                foreach (var cell in row.Value)
                {
                    foreach (var header in headerPositions)
                    {
                        if (cell.Key == header.Value && headerActions.TryGetValue(header.Key, out var action))
                        {
                            lineItem.ExpenseRaw += cell.Value + " ";
                            action(lineItem, cell.Value);
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(lineItem.SKU) || !string.IsNullOrEmpty(lineItem.Name))
                {
                    lineItems.Add(lineItem);
                }
            }

            return lineItems;
        }

        private static bool IsHeader(string cellValue, List<KeyValuePair<string, string>> headerMappings)
        {
            return headerMappings.Any(hm => cellValue.Contains(hm.Key, StringComparison.OrdinalIgnoreCase));
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
                new KeyValuePair<string, string>("item code", "SKU" ),
                new KeyValuePair<string, string>("supplier item code", "SKU" ),
                new KeyValuePair<string, string>("item no", "SKU" ),
                new KeyValuePair<string, string>("item no.", "SKU" ),

                    //new KeyValuePair<string, string>("item", "Name" ),
                    new KeyValuePair<string, string>("description", "Name" ),
                    new KeyValuePair<string, string>("description of goods or services", "Name" ),
                    new KeyValuePair<string, string>("item description", "Name" ),

                    new KeyValuePair<string, string>("quantity", "Quantity" ),
                    new KeyValuePair<string, string>("qty", "Quantity" ),
                    new KeyValuePair<string, string>("qty.", "Quantity" ),
                    new KeyValuePair<string, string>("amount ordered", "Quantity" ),
                    new KeyValuePair<string, string>("quantity ordered", "Quantity" ),

                    new KeyValuePair<string, string>("unit price", "UnitPrice" ),
                    new KeyValuePair<string, string>("quoted unit price", "UnitPrice" ),
                    new KeyValuePair<string, string>("unit cost", "UnitPrice" ),
                    new KeyValuePair<string, string>("quoted price", "UnitPrice" ),
                    new KeyValuePair<string, string>("exec unit price", "UnitPrice" ),
                    new KeyValuePair<string, string>("ex price", "UnitPrice" ),

                    new KeyValuePair<string, string>( "discount", "Discount" ),
                    new KeyValuePair<string, string>( "disc %", "Discount" ),

                    new KeyValuePair<string, string>("net price", "NetPrice" ),
                    new KeyValuePair<string, string>("amount aud", "NetPrice" )

                };

            return headerMappings;
        }


        /**********************************************************************************************
        *
        * ************************************EXPENSE ANALYSIS****************************************
        *
        **********************************************************************************************/

        private Deal ProcessExpenseAnalysisResponse(GetExpenseAnalysisResponse response)
        {
            var deal = new Deal
            {
                Company = new Company(),
                DeliveryAddress = new Address(),
                Emails = new List<string>(),
                LineItems = new List<LineItems>()
            };

            try
            {
                foreach (var expenseDocument in response.ExpenseDocuments)
                {
                    Log.Information("Processing expense document");

                    // Extract summary fields
                    foreach (var field in expenseDocument.SummaryFields)
                    {
                        switch (field.Type.Text)
                        {
                            case "PO_NUMBER":
                                deal.PurchaseOrderNo = field.ValueDetection.Text;
                                break;
                            case "VENDOR_NAME":
                                deal.Company.Name = field.ValueDetection.Text;
                                break;
                            case "RECEIVER_ADDRESS":
                                // Assuming a function AddressSplitter to parse and assign address
                                AddressSplitter.SetAddress(field.ValueDetection.Text.Replace('\n', ' '));
                                deal.DeliveryAddress = AddressSplitter.GetAddress();
                                break;
                            case "SUBTOTAL":
                                deal.SubTotal = ExtractDecimalOnly(field.ValueDetection.Text);
                                break;
                            case "TAX":
                                deal.Tax = ExtractDecimalOnly(field.ValueDetection.Text);
                                break;
                            case "TOTAL":
                                deal.Total = ExtractDecimalOnly(field.ValueDetection.Text);
                                break;
                            case "DELIVERY_DATE":
                                deal.DeliveryDate = field.ValueDetection.Text;
                                break;
                            case "VENDOR_ABN_NUMBER":
                                string abn = Regex.Replace(field.ValueDetection.Text, @"\s+", "");
                                if (!abn.Equals("85663589062") && string.IsNullOrWhiteSpace(deal.Company.ABN))
                                {
                                    deal.Company.ABN = abn;
                                }
                                break;
                            case "VENDOR_URL":
                                deal.Company.Domain = field.ValueDetection.Text;
                                break;
                            default:
                                break;
                        }

                        // Collecting email addresses to allocate sales rep
                        if (field.ValueDetection.Text.Contains('@'))
                        {
                            deal.Emails.Add(field.ValueDetection.Text);
                        }
                    }

                    // Constructing the deal name
                    deal.DealName = string.IsNullOrWhiteSpace(deal.PurchaseOrderNo) ? "Default Deal Name" : deal.PurchaseOrderNo;

                    // Assigning the sales rep name
                    deal.SalesRepName = "Default Sales Rep"; // Replace with actual sales rep logic

                    // Extract line items
                    //foreach (var lineItemGroup in expenseDocument.LineItemGroups)
                    //{
                    //    foreach (var lineItem in lineItemGroup.LineItems)
                    //    {
                    //        var lineItemDetail = new LineItems();
                    //        foreach (var lineItemField in lineItem.LineItemExpenseFields)
                    //        {
                    //            switch (lineItemField.Type.Text)
                    //            {
                    //                case "PRODUCT_CODE":
                    //                    lineItemDetail.SKU = lineItemField.ValueDetection.Text;
                    //                    break;
                    //                case "ITEM":
                    //                    lineItemDetail.Name = lineItemField.ValueDetection.Text;
                    //                    break;
                    //                case "QUANTITY":
                    //                    lineItemDetail.Quantity = ExtractDecimalOnly(lineItemField.ValueDetection.Text);
                    //                    break;
                    //                case "UNIT_PRICE":
                    //                    lineItemDetail.UnitPrice = ExtractDecimalOnly(lineItemField.ValueDetection.Text.Trim('$'));
                    //                    break;
                    //                case "EXPENSE_ROW":
                    //                    lineItemDetail.ExpenseRaw = lineItemField.ValueDetection.Text;
                    //                    break;
                    //                default:
                    //                    break;
                    //            }
                    //        }
                    //        deal.LineItems.Add(lineItemDetail);
                    //    }
                    //}
                }

                // Optional: Process order notes from email or other sources
                // deal.OrderNotes = ProcessEmailBody(emailContent);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while processing the Textract expense analysis response.");
            }

            return deal;
        }

        private decimal ExtractDecimalOnly(string input)
        {
            string sanitizedInput = input.Replace(",", "");

            string pattern = @"(\d{1,3}(,\d{3})*|\d+)(\.\d{1,2})?\b";
            Regex regex = new Regex(pattern);
            Match match = regex.Match(sanitizedInput);

            if (match.Success)
            {
                if (decimal.TryParse(match.Value, out decimal result))
                {
                    return result;
                }
            }

            Log.Error($"No decimal value found in the input string {input}");
            return 0;
        }
    }



}
