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
            $"MessageId: {e.Id}\nSubject: {e.Subject}\nFrom: {e.From}\nDate: {e.Date:dd-MM-yyyy}\nBody: {e.Body}"));

        var prompt = $"""
            You are classifying recruitment emails.

            Your task is to extract ONLY initial job application confirmation emails.

            IMPORTANT: First determine whether the email is a rejection, unsuccessful outcome, role closure, interview invite, job alert, recommendation, or later-stage status update. If yes, EXCLUDE it immediately, even if it also mentions an application, role, company, or says "thank you for your interest".

            An email is an application confirmation ONLY if it confirms that the candidate has just submitted/applied for a job, such as:
            - "Your application has been received"
            - "Thank you for applying"
            - "Application submitted successfully"
            - "We received your application"
            - "Thanks for your application"

            EXCLUDE emails containing rejection or unsuccessful outcome language, including but not limited to:
            - "After careful consideration"
            - "we've decided not to move forward"
            - "not move forward with your candidacy"
            - "unfortunately"
            - "we regret"
            - "position has been filled"
            - "role has been closed"
            - "no longer available"
            - "unsuccessful"
            - "not selected"

            If an email contains both application-related words and rejection/outcome words, rejection/outcome words win and the email must be excluded.

            Also exclude:
            - interview invitations or scheduling
            - job alerts or recommendations
            - follow-ups or status updates
            - role closing notifications
            - emails where the company cannot be determined

            If the role cannot be determined but the company is identifiable, use "Unknown" as the jobRole.
            For duplicates about the same application (e.g. platform confirmation + company auto-reply), return only one entry.

            Return objects containing: messageId, companyName, jobRole, appliedDate (use the email date in dd-MM-yyyy format), status (always "applied").
            If there are no initial application confirmation emails, return an empty applications array.

            Emails:
            
            ---

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
