📄 Genesys Cloud Conversation Transcript Exporter
This is a .NET console application that fetches conversation message transcripts from Genesys Cloud using its public API and exports the results to a structured CSV file. It supports retry logic with exponential backoff for robustness and is designed for batch processing large sets of conversation IDs.

🚀 Features
✅ Reads conversation IDs from an input CSV file

✅ Fetches conversation message data using Genesys Cloud API

✅ Implements retry with exponential backoff on HTTP 502/503/504

✅ Exports conversation data to CSV including:

Conversation ID

Start Time & End Time

Message DateTime

Purpose

Message Text

✅ Writes results incrementally for every 10 successful conversations

📁 Project Structure
bash
Copy
Edit
GenesysTranscriptExporter/
│
├── AppConfig.json          # Configuration (API token, input/output file paths)
├── Program.cs              # Main application logic
├── Models/
│   └── MessageConversation.cs  # JSON mapping classes
├── Utils/
│   └── CsvHelper.cs            # CSV writing logic
│   └── RetryHandler.cs         # Retry/backoff logic
└── README.md
⚙ Configuration
Add a file called AppConfig.json in the root directory:

json
Copy
Edit
{
  "inputCsvPath": "input.csv",
  "outputCsvPath": "output.csv",
  "bearerToken": "YOUR_GENESYS_CLOUD_ACCESS_TOKEN",
  "baseApiUrl": "https://api.mypurecloud.com"
}
📥 Input Format
The input CSV file should contain a list of conversation IDs, one per line. Example:

Copy
Edit
12345-abcde-67890
54321-edcba-09876
📤 Output Format
The exported CSV will look like this:

ConversationId	StartTime	EndTime	messageDateTime	messagePurpose	MessageText
12345...	2025-07-22T...	2025-07-22T...	2025-07-22T...	agent	Hello!
2025-07-22T...	customer	I need help

Messages are grouped under their conversations with the ID shown only once.

🧠 Retry Logic
The app retries API requests on transient errors (502, 503, 504) using an exponential backoff strategy:

Elapsed Time	Delay Before Retry
< 5 min	3 seconds
< 10 min	9 seconds
> 10 min	27 seconds

🧪 Running the App
bash
Copy
Edit
dotnet run
Or if compiled:

bash
Copy
Edit
GenesysTranscriptExporter.exe
📦 Requirements
.NET 6.0 or newer

Access token for Genesys Cloud (OAuth 2.0 Bearer Token)

Appropriate permissions to access /conversations/{conversationId}/messages endpoint

🔐 Security
Never commit your access token or sensitive conversation IDs to source control. Use .gitignore to exclude AppConfig.json if needed.

🧑‍💻 Author
Developed by Mohamed Salah
Senior Genesys Engineer @ Rayacx

📝 License
This project is open-source and available under the MIT License.