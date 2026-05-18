using System.Text.Json;
using OpenAI.Chat;
using core.Interfaces;
using core.Models;
using Microsoft.Extensions.Configuration;

namespace infrastructure.Services;

public class OpenAIService : IAIService
{
    private readonly IConfiguration _configuration;

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
                      "companyName": { "type": "string" },
                      "jobRole": { "type": "string" },
                      "appliedDate": { "type": "string" },
                      "status": { "type": "string" }
                    },
                    "required": ["companyName", "jobRole", "appliedDate", "status"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["applications"],
              "additionalProperties": false
            }
            """),
        jsonSchemaIsStrict: true);

    public OpenAIService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<List<JobApplication>> ClassifyBatchAsync(List<EmailMessage> emails, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
            return [];

        var client = CreateClient();

        var emailsText = string.Join("\n---\n", emails.Select(e =>
            $"Subject: {e.Subject}\nFrom: {e.From}\nDate: {e.Date:dd-MM-yyyy}\nBody: {e.Body[..Math.Min(e.Body.Length, 500)]}"));

        var prompt = $"""
            Given these emails, identify which are job application related.
            For duplicates about the same application (e.g. platform confirmation like Seek.com.au, indeed, etc. + company auto-reply), return only one entry.
            Return objects containing: companyName, jobRole, appliedDate (use the email date in dd-MM-yyyy format), status (always "applied").
            If no emails are job-related, return an empty applications array.

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
            Given these job application results from multiple batches, deduplicate entries for the same company+role combination.
            Keep the earliest appliedDate for duplicates.
            Return the final consolidated applications with: companyName, jobRole, appliedDate (in dd-MM-yyyy format), status.

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

    private static List<JobApplication> ParseResponse(string responseText)
    {
        try
        {
            var wrapper = JsonSerializer.Deserialize<ApplicationsWrapper>(responseText, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            return wrapper?.Applications ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class ApplicationsWrapper
    {
        public List<JobApplication> Applications { get; set; } = [];
    }
}
