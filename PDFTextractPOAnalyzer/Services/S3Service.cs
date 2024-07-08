using Amazon;
using Amazon.S3.Model;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDFTextractPOAnalyzer.Services
{
    public class S3Service : IS3Service
    {
        private readonly IConfiguration _config;
        private readonly AmazonS3Client _s3Client;

        public S3Service(IConfiguration config, string accessKey, string secretKey, RegionEndpoint region)
        {
            _config = config;
            _s3Client = new AmazonS3Client(accessKey, secretKey, region);
        }

        public async Task UploadPdfAsync(string filePath)
        {
            try
            {
                var bucketName = _config["AWS:BucketName"];
                var fileName = Path.GetFileName(filePath);

                if (fileName != null)
                {
                    byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                    using (var memoryStream = new MemoryStream(fileBytes))
                    {
                        var putRequest = new PutObjectRequest
                        {
                            BucketName = bucketName,
                            Key = fileName,
                            InputStream = memoryStream,
                            ContentType = "application/pdf"
                        };
                        await _s3Client.PutObjectAsync(putRequest);
                        Log.Information("PDF uploaded successfully to S3.");
                    }
                }
                else
                {
                    Log.Information("File not available");
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
    }



}
