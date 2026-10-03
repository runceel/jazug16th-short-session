#pragma warning disable OPENAI001
using EnvReporter.Web;
using EnvReporter.Web.Components;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel.Primitives;

var builder = WebApplication.CreateBuilder(args);
var useEntraAuthentication = builder.Configuration.GetValue<bool>("Foundry:UseEntraAuthentication");
var hostedAgentEndpoint = builder.Configuration["services:env-reporter:https:0"];

if (useEntraAuthentication && string.IsNullOrWhiteSpace(hostedAgentEndpoint))
{
    throw new InvalidOperationException("Aspire did not provide the Hosted Agent HTTPS endpoint.");
}

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient("AgentResponses", client =>
{
    client.Timeout = TimeSpan.FromMinutes(6);
}).AddHttpMessageHandler(() => new HostedAgentApiVersionHandler(useEntraAuthentication));
builder.Services.AddSingleton(services =>
{
    var httpClient = services.GetRequiredService<IHttpClientFactory>().CreateClient("AgentResponses");
    AuthenticationPolicy authenticationPolicy = useEntraAuthentication
        ? new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default")
        : new NoAuthenticationPolicy();
    var endpoint = useEntraAuthentication
        ? $"{hostedAgentEndpoint!.TrimEnd('/')}/endpoint/protocols/openai"
        : "http://env-reporter";

    // ローカル Aspire の内部 endpoint は認証不要なので、API key ヘッダーを付けない。
    var openAIClient = new OpenAIClient(authenticationPolicy, new OpenAIClientOptions
    {
        Endpoint = new Uri(endpoint),
        NetworkTimeout = TimeSpan.FromMinutes(6),
        RetryPolicy = new ClientRetryPolicy(0),
        Transport = new HttpClientPipelineTransport(httpClient),
    });

    var chatClient = openAIClient.GetResponsesClient().AsIChatClient("gpt-6-luna");
    return chatClient;
});
builder.Services.AddTransient<AgentClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
