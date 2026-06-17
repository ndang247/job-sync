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

        var prompt = $$"""
            You are extracting job applications from recruitment-related emails.

            Goal:

            Return an application when the email provides strong evidence that the candidate applied for a specific job.

            Include an email if its primary purpose is to confirm, acknowledge, or reference an existing job application. Positive evidence includes:

            * application submitted
            * application sent
            * application received
            * application acknowledged
            * application exists for a specific company and role
            * recruiter or employer acknowledgement of an application

            Important:

            Classify each email independently.

            Do not exclude a valid application because the email also contains:

            * recommended jobs
            * next steps
            * interview preparation information
            * scheduling links
            * marketing content appended to the email
            * platform-generated suggestions

            Exclude emails whose primary purpose is:

            * job alerts or job recommendations
            * newsletters, events, or marketing
            * account, security, or platform notifications
            * application view notifications
            * application status updates that do not confirm a submitted application
            * rejection or unsuccessful outcome notifications
            * role closure notifications
            * application withdrawal confirmations
            * interview invitations or scheduling messages that do not otherwise confirm an application
            * emails where the applied company cannot be determined

            Priority rule:

            If an email contains both application evidence and rejection, unsuccessful outcome, withdrawal, or role-closure language, exclude it.

            Extraction rules:

            * Use the email MessageId.
            * Use the applied company, not the email platform, as companyName.
            * Use the applied role/title when present.
            * If the role cannot be determined but the company can be determined, use "Unknown".
            * Use the email Date as appliedDate in dd-MM-yyyy format.
            * If the email explicitly states an application date, use that date instead.
            * Set status to "applied".

            Duplicate handling:

            Return one record per real-world job application, not one record per email.

            Treat emails as duplicates when they refer to the same candidate applying to the same company for the same role, even if they have:

            * different MessageIds
            * different email dates
            * different senders
            * different templates
            * different wording
            * different recruitment platforms
            * one direct employer email and one platform confirmation

            Normalize company and role names before comparing:

            * ignore casing
            * ignore extra whitespace
            * treat punctuation differences as the same
            * treat "&" and "and" as the same
            * treat minor title variations as the same when the role is clearly equivalent

            Do not treat emails as duplicates solely because they share:

            * sender
            * template
            * wording
            * subject structure
            * recruitment platform

            When duplicates exist:

            * keep the earliest appliedDate
            * prefer the MessageId from the clearest original application confirmation
            * discard later acknowledgements, reminders, status updates, or repeated confirmations

            Validation:

            Before returning the final result:

            1. Identify all emails that satisfy the inclusion criteria.
            2. Ensure every qualifying application appears exactly once in the output.
            3. Ensure no excluded email appears in the output.

            Output:

            Return valid JSON only.

            Schema:

            {
              "applications": [
                {
                  "messageId": "...",
                  "companyName": "...",
                  "jobRole": "...",
                  "appliedDate": "dd-MM-yyyy",
                  "status": "applied"
                }
              ]
            }

            If there are no matching applications, return:

            {
              "applications": []
            }

            Emails:

            ---

            {{emailsText}}
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
            Deduplicate these extracted job applications.

            Return one record per real-world application, not one record per email.

            Treat records as duplicates when they have the same candidate/email, same normalized companyName, and same normalized jobRole, even if messageId or appliedDate differ.

            Normalize before comparing:
            * case-insensitive company and role
            * trim whitespace
            * ignore punctuation differences
            * treat "&" and "and" as equivalent
            * treat minor role wording differences as equivalent when clearly the same job

            Do not duplicate applications because they came from different emails, batches, platforms, or templates.

            For each duplicate group:
            * keep the earliest appliedDate
            * keep the messageId from the record that best represents the original application confirmation
            * keep status as "applied"

            Return valid JSON only with: messageId, companyName, jobRole, appliedDate (in dd-MM-yyyy format), status.

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
