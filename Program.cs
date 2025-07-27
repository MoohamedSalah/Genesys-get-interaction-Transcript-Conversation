using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Program;

class Program
{
    private static JsonElement config;
    private static readonly HttpClient httpClient = new();
    private static SemaphoreSlim semaphore = new(5); // Parallel limit
    public static int count = 1;

    static async Task Main()
    {
        try
        {
            LoadConfig();
            string inputPath = config.GetProperty("inputCsvPath").GetString();
            string outputPath = config.GetProperty("outputCsvPath").GetString();
            string token = config.GetProperty("bearerToken").GetString();
            string baseUrl = config.GetProperty("baseApiUrl").GetString();

            var conversationIds = ReadConversationIdsFromCsv(inputPath);
            var successfulBatch = new ConcurrentBag<(string id, string responseJson)>();

            var tasks = conversationIds.Select(async id =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var status = await GetRecordingInfo(token, baseUrl, id);

                    if (!status.StartsWith("ERROR"))
                    {
                        successfulBatch.Add((id, status));

                        // Write every 10 successes
                        if (successfulBatch.Count >= 10)
                        {
                            lock (successfulBatch) // Ensure only one thread writes at a time
                            {
                                var toWrite = successfulBatch.ToList();
                                successfulBatch.Clear();
                                WriteResultsToCsv(outputPath, toWrite);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ {id} failed: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Flush any remaining responses
            if (!successfulBatch.IsEmpty)
            {
                lock (successfulBatch)
                {
                    WriteResultsToCsv(outputPath, successfulBatch.ToList());
                }
            }

            Console.WriteLine("✅ All done.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Fatal error: {ex.Message}");
        }
    }


    static void LoadConfig()
    {
        var json = File.ReadAllText("config.json");
        config = JsonSerializer.Deserialize<JsonDocument>(json).RootElement;
    }

    static List<string> ReadConversationIdsFromCsv(string path)
    {
        var lines = File.ReadAllLines(path).Skip(1); // Skip header
        return lines.Select(l => l.Split(',')[0].Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
    }

    public static async Task<string> GetRecordingInfo(string token, string baseUrl, string conversationId)
    {
        var url = $"{baseUrl}/api/v2/conversations/{conversationId}/recordings";
        int maxRetries = 10;
        int retryCount = 0;
        DateTime firstRetryTime = DateTime.UtcNow;

        while (true)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = null;
            string content = "";

            try
            {
                response = await httpClient.SendAsync(request);
                content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ {count} - {conversationId} - Success");
                    count++;
                    return content;
                }

                // Handle retryable status codes
                if ((int)response.StatusCode == 502 || (int)response.StatusCode == 503 || (int)response.StatusCode == 504 || (int)response.StatusCode == 429)
                {
                    retryCount++;
                    if (retryCount > maxRetries)
                    {
                        Console.WriteLine($"❌ {conversationId} - Max retries reached.");
                        return $"ERROR: Max retries reached - {response.StatusCode}";
                    }

                    // Check Retry-After header if exists
                    if (response.Headers.TryGetValues("Retry-After", out var values))
                    {
                        if (int.TryParse(values.First(), out int retryAfterSeconds))
                        {
                            Console.WriteLine($"⏳ {conversationId} - Retry-After {retryAfterSeconds}s");
                            await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds));
                            continue;
                        }
                    }

                    // Use backoff logic if Retry-After is not provided
                    TimeSpan elapsed = DateTime.UtcNow - firstRetryTime;
                    int delaySeconds = elapsed.TotalMinutes switch
                    {
                        < 5 => 3,
                        < 10 => 9,
                        _ => 27
                    };

                    Console.WriteLine($"🔁 {conversationId} - Retry {retryCount}/{maxRetries} in {delaySeconds}s ({response.StatusCode})");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    continue;
                }

                // Non-retryable error
                Console.WriteLine($"❌ {conversationId} - Failed ({response.StatusCode})");
                return $"ERROR: {response.StatusCode} - {content}";
            }
            catch (Exception ex)
            {
                retryCount++;
                if (retryCount > maxRetries)
                {
                    Console.WriteLine($"❌ {conversationId} - Exception: {ex.Message}");
                    return $"ERROR: Exception - {ex.Message}";
                }

                TimeSpan elapsed = DateTime.UtcNow - firstRetryTime;
                int delaySeconds = elapsed.TotalMinutes switch
                {
                    < 5 => 3,
                    < 10 => 9,
                    _ => 27
                };

                Console.WriteLine($"⚠️  {conversationId} - Exception: {ex.Message}. Retrying in {delaySeconds}s");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
        }
    }



    public static void WriteResultsToCsv(string path, IEnumerable<(string id, string responseJson)> results, bool append = true)
    {
        bool fileExists = File.Exists(path);

        using var writer = new StreamWriter(path, append: append);

        // Write header only if file doesn't exist or we're not appending
        if (!fileExists || !append)
        {
            writer.WriteLine("ConversationId,StartTime,EndTime,messageDateTime,messagePurpose,MessageText");
        }

        foreach (var (id, json) in results)
        {
            if (string.IsNullOrWhiteSpace(json) || json.StartsWith("ERROR")) continue;

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var conversations = JsonSerializer.Deserialize<Class1[]>(json, options);

                if (conversations == null) continue;

                foreach (var convo in conversations)
                {
                    string conversationId = convo.conversationId ?? "";
                    string startTime = convo.startTime.ToString("o");
                    string endTime = convo.endTime.ToString("o");

                    if (convo.messagingTranscript == null || convo.messagingTranscript.Length == 0)
                        continue;

                    bool isFirstMessage = true;

                    foreach (var message in convo.messagingTranscript)
                    {
                        string timestamp = message.timestamp.ToString("o") ?? "";
                        string purpose = message.purpose ?? "";
                        string messageText = message.messageText?.Replace("\"", "\"\"") ?? "";

                        if (isFirstMessage)
                        {
                            writer.WriteLine($"\"{conversationId}\",\"{startTime}\",\"{endTime}\",\"{timestamp}\",\"{purpose}\",\"{messageText}\"");
                            isFirstMessage = false;
                        }
                        else
                        {
                            writer.WriteLine($"\"\",\"\",\"\",\"{timestamp}\",\"{purpose}\",\"{messageText}\"");
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                writer.WriteLine($"\"{id}\",\"ERROR\",\"{ex.Message}\"");
            }

        }

        Console.WriteLine("10 Row saved");
    }





    private static string FormatDate(string isoDate)
    {
        if (DateTime.TryParse(isoDate, out var date))
            return date.ToString("dddd, MMMM d, yyyy h:mm:ss tt (UTC+03:00)");
        return isoDate;
    }


    public class Rootobject
    {
        public Class1[] Property1 { get; set; }
    }

    public class Class1
    {
        public string id { get; set; }
        public string conversationId { get; set; }
        public DateTime startTime { get; set; }
        public DateTime endTime { get; set; }
        public string media { get; set; }
        public object[] annotations { get; set; }
        public Messagingtranscript[] messagingTranscript { get; set; }
        public string fileState { get; set; }
        public int estimatedTranscodeTimeMs { get; set; }
        public int actualTranscodeTimeMs { get; set; }
        public int maxAllowedRestorationsForOrg { get; set; }
        public int remainingRestorationsAllowedForOrg { get; set; }
        public string sessionId { get; set; }
        public User[] users { get; set; }
        public Externalcontact[] externalContacts { get; set; }
        public DateTime creationTime { get; set; }
        public string selfUri { get; set; }
    }

    public class Messagingtranscript
    {
        public string from { get; set; }
        public Fromexternalcontact fromExternalContact { get; set; }
        public string to { get; set; }
        public DateTime timestamp { get; set; }
        public string id { get; set; }
        public string purpose { get; set; }
        public string participantId { get; set; }
        public Queue queue { get; set; }
        public Messagemediaattachment[] messageMediaAttachments { get; set; }
        public object[] messageStickerAttachments { get; set; }
        public object[] quickReplies { get; set; }
        public object[] buttonResponses { get; set; }
        public object[] genericTemplates { get; set; }
        public object[] cards { get; set; }
        public string contentType { get; set; }
        public string socialVisibility { get; set; }
        public string messageText { get; set; }
        public Fromuser fromUser { get; set; }
    }

    public class Fromexternalcontact
    {
        public string id { get; set; }
        public DateTime modifyDate { get; set; }
        public DateTime createDate { get; set; }
        public string selfUri { get; set; }
    }

    public class Queue
    {
        public string id { get; set; }
        public string selfUri { get; set; }
    }

    public class Fromuser
    {
        public string id { get; set; }
        public string name { get; set; }
        public string username { get; set; }
        public string selfUri { get; set; }
    }

    public class Messagemediaattachment
    {
        public string url { get; set; }
        public string mediaType { get; set; }
        public int contentLength { get; set; }
        public string name { get; set; }
        public string id { get; set; }
    }

    public class User
    {
        public string id { get; set; }
        public string name { get; set; }
        public string username { get; set; }
        public string selfUri { get; set; }
    }

    public class Externalcontact
    {
        public string id { get; set; }
        public string selfUri { get; set; }
    }






}
