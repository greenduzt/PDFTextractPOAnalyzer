
# PDF Textract PO Analyzer

## Description:
The PDF Textract PO Analyzer is a .NET application designed to extract purchase order information from PDF documents using Amazon Textract service. It uploads PDF documents to Amazon S3, starts an expense analysis job using Amazon Textract, and processes the extracted data to create a structured representation of purchase orders.

## Features:
- Uploads PDF documents to Amazon S3.
- Initiates an expense analysis job using Amazon Textract.
- Extracts purchase order information such as PO number, vendor name, delivery address, subtotal, tax, total, delivery date, vendor ABN, vendor URL, line items, etc.
- Outputs the structured representation of purchase orders.

## Installation:
1. Clone the repository to your local machine.
2. Install the necessary dependencies by running `dotnet restore` in the project directory.

## Configuration:
1. Set up your AWS credentials and configure the bucket name in the `appsettings.json` or use user secrets.
2. Ensure that the required AWS permissions are granted to access S3 and Textract services.

## Usage:
1. Replace the sample PDF file path in the `Main` method of `Program.cs` with your PDF file path.
2. Run the application.
3. The application will upload the PDF document to S3, initiate an expense analysis job using Textract, and process the extracted data.
4. Check the console output and log files for the extracted purchase order information.

## Dependencies:
- Amazon.AWS SDK
- Microsoft.Extensions.Configuration
- Serilog
- PDFTextractPOAnalyzer.Core

## Contributing:
Contributions are welcome! If you have any suggestions, improvements, or bug fixes, feel free to open an issue or submit a pull request.

## License:
This project is licensed under the [MIT License](LICENSE). Feel free to use, modify, and distribute the code for personal or commercial purposes.

## Author:
Chamara Walaliyadde

## Acknowledgments:
- This project was inspired by the need to automate the extraction of purchase order information from PDF documents.
- Special thanks to the developers and contributors of the AWS SDK and Serilog for their excellent libraries and tools.
