using web_api.Hubs;
using web_api.Services;
using core.Entities;
using core.Interfaces;
using infrastructure.Data;
using infrastructure.Services;
using worker;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromMinutes(10);
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IEmailService, GmailService>();
builder.Services.AddScoped<IAIService, OpenAIService>();
builder.Services.AddScoped<ISyncOrchestrator, SyncOrchestrator>();
builder.Services.AddScoped<IJobApplicationService, JobApplicationService>();
builder.Services.AddSingleton<IApplicationListCacheState, ApplicationListCacheState>();
builder.Services.AddScoped<ISyncProgressReporter, SyncProgressReporter>();
builder.Services.AddScoped<ISyncHubNotifier, SyncHubNotifier>();
builder.Services.AddScoped<IGoogleTokenExchanger, GoogleTokenExchanger>();
builder.Services.AddSingleton<ISyncJobChannel, SyncJobChannel>();
builder.Services.AddHostedService<SyncBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("SignalRCors", policy =>
    {
        policy.WithOrigins(builder.Configuration["FrontendUrl"] ?? "http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("SignalRCors");
app.UseSession();
app.MapControllers();
app.MapHub<SyncHub>("/hubs/sync");

app.Run();

// Make the implicit Program class public so test projects can access it
public partial class Program { }
