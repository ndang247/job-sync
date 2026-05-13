using System.Text.Json;
using GenerativeAI;
using core.Interfaces;
using core.Models;
using Microsoft.Extensions.Configuration;

namespace infrastructure.Services;

public class GeminiService : IGeminiService
{
    private readonly IConfiguration _configuration;

    public GeminiService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<List<JobApplication>> ClassifyBatchAsync(List<EmailMessage> emails, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
            return [];

        var model = CreateModel();

        var emailsText = string.Join("\n---\n", emails.Select(e =>
            $"Subject: {e.Subject}\nFrom: {e.From}\nDate: {e.Date:dd-MM-yyyy}\nBody: {e.Body[..Math.Min(e.Body.Length, 500)]}"));

        var prompt = $"""
            Given these emails, identify which are job application related.
            For duplicates about the same application (e.g. platform confirmation like Seek.com.au + company auto-reply), return only one entry.
            Return ONLY a JSON array (no markdown, no explanation) with objects containing: companyName, jobRole, appliedDate (use the email date in dd-MM-yyyy format), status (always "applied").
            If no emails are job-related, return an empty array [].

            Emails:
            {emailsText}
            """;

        var response = await model.GenerateContentAsync(prompt, cancellationToken: cancellationToken);
        return ParseResponse(response.Text ?? "[]");
    }

    public async Task<List<JobApplication>> DeduplicateAsync(List<JobApplication> applications, CancellationToken cancellationToken = default)
    {
        if (applications.Count == 0)
            return [];

        var model = CreateModel();

        var applicationsJson = JsonSerializer.Serialize(applications, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var prompt = $"""
            Given these job application results from multiple batches, deduplicate entries for the same company+role combination.
            Keep the earliest appliedDate for duplicates.
            Return ONLY the final consolidated JSON array (no markdown, no explanation) with objects containing: companyName, jobRole, appliedDate (use the email date in dd-MM-yyyy format), status.

            Applications:
            {applicationsJson}
            """;

        var response = await model.GenerateContentAsync(prompt, cancellationToken: cancellationToken);
        return ParseResponse(response.Text ?? "[]");
    }

    private GenerativeModel CreateModel()
    {
        var apiKey = _configuration["Google:GeminiApiKey"]!;
        var genAi = new GoogleAi(apiKey);
        return genAi.CreateGenerativeModel(GoogleAIModels.Gemini2Flash);
    }

    private static List<JobApplication> ParseResponse(string responseText)
    {
        var cleaned = responseText.Trim();

        // Strip markdown code fences if present
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline >= 0)
                cleaned = cleaned[(firstNewline + 1)..];
            var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                cleaned = cleaned[..lastFence];
        }
        cleaned = cleaned.Trim();

        try
        {
            return JsonSerializer.Deserialize<List<JobApplication>>(cleaned, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
