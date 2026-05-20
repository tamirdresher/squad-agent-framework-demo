using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

internal sealed class ProviderAgentFactory
{
    private const string ProviderVariable = "SQUAD_AF_PROVIDER";
    private const string EndpointVariable = "SQUAD_AF_ENDPOINT";
    private const string DeploymentVariable = "SQUAD_AF_DEPLOYMENT";
    private const string ModelVariable = "SQUAD_AF_MODEL";
    private const string ApiKeyVariable = "SQUAD_AF_API_KEY";

    private readonly string? provider;
    private readonly string? endpoint;
    private readonly string? deployment;
    private readonly string? model;
    private readonly string? apiKey;
    private readonly IChatClient? chatClient;

    private ProviderAgentFactory(string? provider, string? endpoint, string? deployment, string? model, string? apiKey, IChatClient? chatClient = null)
    {
        this.provider = Normalize(provider);
        this.endpoint = Trim(endpoint);
        this.deployment = Trim(deployment);
        this.model = Trim(model);
        this.apiKey = Trim(apiKey);
        this.chatClient = chatClient;
    }

    public ProviderSummary Summary => new(
        Provider: chatClient is not null ? (provider ?? "chat-client") : provider ?? "not-configured",
        IsProviderBacked: IsConfigured,
        HasEndpoint: endpoint is not null,
        HasDeployment: deployment is not null,
        HasModel: model is not null);

    private bool IsConfigured => chatClient is not null || provider is "azure-openai" or "openai-compatible" or "foundry-local";

    public static ProviderAgentFactory FromEnvironment() =>
        new(
            Environment.GetEnvironmentVariable(ProviderVariable),
            Environment.GetEnvironmentVariable(EndpointVariable),
            Environment.GetEnvironmentVariable(DeploymentVariable),
            Environment.GetEnvironmentVariable(ModelVariable),
            Environment.GetEnvironmentVariable(ApiKeyVariable));

    public static ProviderAgentFactory FromChatClient(IChatClient client, string provider = "foundry-local") =>
        new(provider, endpoint: null, deployment: null, model: null, apiKey: null, chatClient: client);

    public IChatClient CreateChatClient()
    {
        EnsureConfigured("A provider-backed IChatClient");
        return ResolveChatClient();
    }

    public AgentHandle CreateAgent(string name, string instructions)
    {
        EnsureConfigured($"A provider-backed agent for '{name}'");
        var agent = CreateProviderBackedAgent(name, instructions);
        return new AgentHandle(agent, "provider IChatClient.AsAIAgent");
    }

    private AIAgent CreateProviderBackedAgent(string name, string instructions)
    {
        return CreateChatClient().AsAIAgent(
            instructions,
            name,
            $"Provider-backed Agent Framework participant '{name}'.",
            tools: [],
            loggerFactory: null,
            services: null);
    }

    private void EnsureConfigured(string target)
    {
        if (IsConfigured)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{target} requires the AppHost chat connection or SQUAD_AF_PROVIDER configuration. " +
            "For the workflow sample, run the AppHost or set SQUAD_AF_PROVIDER plus the matching endpoint/model settings.");
    }

    private IChatClient ResolveChatClient() =>
        chatClient ?? provider switch
        {
            "azure-openai" => CreateAzureOpenAIChatClient(),
            "openai-compatible" => CreateOpenAICompatibleChatClient(requireLoopback: false),
            "foundry-local" => CreateOpenAICompatibleChatClient(requireLoopback: true),
            _ => throw new InvalidOperationException($"Unsupported {ProviderVariable} value '{provider}'.")
        };

    private IChatClient CreateAzureOpenAIChatClient()
    {
        if (endpoint is null || deployment is null)
        {
            throw new InvalidOperationException($"{EndpointVariable} and {DeploymentVariable} are required for azure-openai.");
        }

        return new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            .GetChatClient(deployment)
            .AsIChatClient();
    }

    private IChatClient CreateOpenAICompatibleChatClient(bool requireLoopback)
    {
        if (endpoint is null || (model ?? deployment) is null)
        {
            throw new InvalidOperationException($"{EndpointVariable} plus {ModelVariable} or {DeploymentVariable} are required.");
        }

        var uri = new Uri(endpoint);
        if (requireLoopback && !uri.IsLoopback)
        {
            throw new InvalidOperationException($"{EndpointVariable} must be a loopback URI for foundry-local.");
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey ?? "not-required"),
            new OpenAIClientOptions { Endpoint = uri });

        return client.GetChatClient(model ?? deployment).AsIChatClient();
    }

    private static string? Normalize(string? value) => Trim(value)?.ToLowerInvariant();
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record AgentHandle(AIAgent Agent, string Kind) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        if (Agent is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (Agent is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

internal sealed record ProviderSummary(
    string Provider,
    bool IsProviderBacked,
    bool HasEndpoint,
    bool HasDeployment,
    bool HasModel);
