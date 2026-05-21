using System.Text.Json;
using OpenAI.Chat;
using core.Interfaces;
using core.Entities;
using Microsoft.Extensions.Configuration;
using System.Collections.Frozen;
using Microsoft.Extensions.Logging;

namespace infrastructure.Services;

public class OpenAIService : IAIService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIService> _logger;

    private static readonly ChatResponseFormat _jsonSchema = ChatResponseFormat.CreateJsonSchemaFormat(
        jsonSchemaFormatName: "job_applications",
        jsonSchema: BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "applications": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                        "messageId": { "type": "string" },
                        "companyName": { "type": "string" },
                        "jobRole": { "type": "string" },
                        "appliedDate": { "type": "string" },
                        "status": { "type": "string" }
                    },
                    "required": ["messageId", "companyName", "jobRole", "appliedDate", "status"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["applications"],
              "additionalProperties": false
            }
            """),
        jsonSchemaIsStrict: true);

    public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<JobApplication>> ClassifyBatchAsync(List<EmailMessage> emails, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
            return [];

        var client = CreateClient();

        var emailsText = string.Join("\n---\n", emails.Select(e =>
            $"MessageId: {e.Id}\nSubject: {e.Subject}\nFrom: {e.From}\nDate: {e.Date:dd-MM-yyyy}\nBody: {e.Body[..Math.Min(e.Body.Length, 7000)]}"));

        var prompt = $"""
            Given these emails, identify which are job APPLICATION CONFIRMATION or APPLYING for a job emails only.
            Include ONLY emails that confirm a job application was submitted or intention is related to applying for a job (e.g. "Your application has been received", "Thanks for applying", "Application submitted successfully", etc.).
            IGNORE and EXCLUDE these types of emails:
            - Rejection emails or intention is related to job application rejection ("Unfortunately", "We regret", "not moving forward", "position has been filled", etc.)
            - Role closing notifications ("role has been closed", "position is no longer available", "listing has expired", etc.)
            - Interview invitations or scheduling
            - Job alerts or recommendations
            - Follow-ups or status updates that are not the initial application confirmation
            Also SKIP any email where the specific company name cannot be determined from the email content. The job role alone is not enough — the company must be identifiable.
            If the role cannot be determined, but company is identifiable, use "Unknown" as the jobRole.
            For duplicates about the same application (e.g. platform confirmation like Seek.com.au, indeed, etc. + company auto-reply), return only one entry.
            Return objects containing: messageId, companyName, jobRole, appliedDate (use the email date in dd-MM-yyyy format), status (always "applied").
            If no emails are application confirmations, return an empty applications array.

            Emails:
            {emailsText}
            """;

        var options = new ChatCompletionOptions { ResponseFormat = _jsonSchema };

        var response = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)],
            options,
            cancellationToken: cancellationToken);

        return ParseResponse(response.Value.Content[0].Text ?? """{"applications":[]}""");
    }

    public async Task<List<JobApplication>> DeduplicateAsync(List<JobApplication> applications, CancellationToken cancellationToken = default)
    {
        if (applications.Count == 0)
            return [];

        var client = CreateClient();

        var applicationsJson = JsonSerializer.Serialize(applications, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var prompt = $"""
            Given these job application results from multiple batches, deduplicate entries for the same messageId and companyName+jobRole combination.
            Keep the earliest appliedDate for duplicates.
            Return the final consolidated applications with: messageId, companyName, jobRole, appliedDate (in dd-MM-yyyy format), status.

            Applications:
            {applicationsJson}
            """;

        var options = new ChatCompletionOptions { ResponseFormat = _jsonSchema };

        var response = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)],
            options,
            cancellationToken: cancellationToken);

        return ParseResponse(response.Value.Content[0].Text ?? """{"applications":[]}""");
    }

    private ChatClient CreateClient()
    {
        var apiKey = _configuration["OpenAI:ApiKey"]!;
        var model = _configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        return new ChatClient(model, apiKey);
    }

    private List<JobApplication> ParseResponse(string responseText)
    {
        try
        {
            var wrapper = JsonSerializer.Deserialize<ApplicationsWrapper>(responseText, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            return wrapper?.Applications.Select(a => a.ToEntity()).ToList() ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI response: {ResponseText} --- {ExMessage}", responseText, ex.Message);
            throw new InvalidOperationException("Failed to parse AI response. " + ex.Message);
        }
    }

    private sealed class ApplicationsWrapper
    {
        public List<core.Models.JobApplication> Applications { get; set; } = [];
    }
}
