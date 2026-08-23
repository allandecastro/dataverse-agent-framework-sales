using Azure.Core;
using Azure.Identity;
using Zava.SalesIntelligence.Companion;
using Zava.SalesIntelligence.Companion.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Builds the sales workflow on demand from the ZAVA_* configuration.
builder.Services.AddSingleton<WorkflowFactory>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// A short-lived Azure Speech authorization token for the browser, so the key never leaves the
// server. Keyless: the same AAD identity the rest of the app uses (az login), formatted as the
// Speech SDK's aad# authorization token. The browser does STT/TTS with it; Speech only
// transcribes and synthesises - the squad stays the only brain.
app.MapGet("/api/speech-token", async () =>
{
    var region = Environment.GetEnvironmentVariable("ZAVA_SPEECH_REGION") ?? "westeurope";
    var resourceId = Environment.GetEnvironmentVariable("ZAVA_SPEECH_RESOURCE_ID");
    if (string.IsNullOrEmpty(resourceId))
    {
        return Results.Problem("ZAVA_SPEECH_RESOURCE_ID is not set.");
    }
    var token = await new AzureCliCredential().GetTokenAsync(
        new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]));
    return Results.Json(new { token = $"aad#{resourceId}#{token.Token}", region });
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
