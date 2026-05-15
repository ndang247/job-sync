using web_api.Hubs;
using web_api.Services;
using core.Interfaces;
using infrastructure.Data;
using infrastructure.Services;
using worker;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IGmailService, GmailService>();
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<ISyncOrchestrator, SyncOrchestrator>();
builder.Services.AddScoped<ISyncProgressReporter, SyncProgressReporter>();
builder.Services.AddScoped<ISyncHubNotifier, SyncHubNotifier>();
builder.Services.AddScoped<IGoogleTokenExchanger, GoogleTokenExchanger>();
builder.Services.AddSingleton<ISyncJobChannel, SyncJobChannel>();
builder.Services.AddHostedService<SyncBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();
app.MapHub<SyncHub>("/hubs/sync");

app.Run();

// Make the implicit Program class public so test projects can access it
public partial class Program { }
