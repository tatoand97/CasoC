using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using CasoC.Agents;
using CasoC.Services;
using Microsoft.Extensions.Configuration;
using System.ClientModel;

namespace CasoC;

internal static class Program
{
    public static async Task<int> Main()
    {
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            cts.Cancel();
        };

        try
        {
            BootstrapSummary summary = await BootstrapAsync(cts.Token);
            WriteBootstrapSummary(summary);
            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("[OperationCanceledException] Operacion cancelada por el usuario.");
            return 1;
        }
        catch (RequestFailedException ex)
        {
            Console.Error.WriteLine($"[RequestFailedException] Status: {ex.Status}, Code: {ex.ErrorCode}, Message: {ex.Message}");
            PrintEndpointHint(ex.Message);
            return 1;
        }
        catch (ClientResultException ex)
        {
            WriteClientError(ex);
            PrintEndpointHint(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{ex.GetType().Name}] {ex.Message}");
            PrintEndpointHint(ex.Message);
            return 1;
        }
    }

    private static async Task<BootstrapSummary> BootstrapAsync(CancellationToken cancellationToken)
    {
        CasoCSettings settings = LoadSettings();
        string endpoint = GetRequiredSetting(settings.AzureOpenAiEndpoint, "CasoC:AzureOpenAiEndpoint");
        string deploymentName = GetRequiredSetting(settings.AzureOpenAiDeployment, "CasoC:AzureOpenAiDeployment");
        string orderAgentId = GetRequiredSetting(settings.OrderAgentId, "CasoC:OrderAgentId");

        ValidateProjectEndpoint(endpoint);

        AIProjectClient projectClient = CreateProjectClient(endpoint);
        await ValidateProjectAccessAsync(projectClient, cancellationToken);
        Console.WriteLine($"[CONFIG] Endpoint validado => {endpoint}");

        AIProjectDeployment deployment = await ValidateDeploymentAsync(projectClient, deploymentName, cancellationToken);
        Console.WriteLine($"[CONFIG] Deployment validado => {deployment.Name}");

        ValidatedAgentBinding orderAgent = await ValidateOrderAgentBindingAsync(projectClient, orderAgentId, cancellationToken);
        Console.WriteLine(
            $"[VALIDATION] OrderAgentId validado => {orderAgentId} -> {orderAgent.Name} (id: {orderAgent.Id}, version: {orderAgent.Version})");

        AgentReconciler reconciler = new(projectClient);

        ReconcileResult policyResult = await reconciler.ReconcileAsync(
            PolicyAgentFactory.AgentName,
            PolicyAgentFactory.Build(deployment.Name),
            cancellationToken);
        WriteReconciliationResult(policyResult);

        ReconcileResult plannerResult = await reconciler.ReconcileAsync(
            PlannerAgentFactory.AgentName,
            PlannerAgentFactory.Build(deployment.Name),
            cancellationToken);
        WriteReconciliationResult(plannerResult);

        return new BootstrapSummary(orderAgent, policyResult, plannerResult);
    }

    private static AIProjectClient CreateProjectClient(string endpoint)
    {
        return new AIProjectClient(new Uri(endpoint), new AzureCliCredential());
    }

    private static void WriteReconciliationResult(ReconcileResult result)
    {
        Console.WriteLine(
            $"[RECONCILE] {result.Version.Name} => {result.ReconciliationStatus} (id: {result.Version.Id}, version: {result.Version.Version})");
    }

    private static void WriteBootstrapSummary(BootstrapSummary summary)
    {
        Console.WriteLine(
            $"[SUMMARY] Bindings => OrderAgent={summary.OrderAgent.Name} (id: {summary.OrderAgent.Id}, version: {summary.OrderAgent.Version}); " +
            $"PolicyAgent={summary.PolicyAgent.Version.Name} (id: {summary.PolicyAgent.Version.Id}, version: {summary.PolicyAgent.Version.Version}); " +
            $"PlannerAgent={summary.PlannerAgent.Version.Name} (id: {summary.PlannerAgent.Version.Id}, version: {summary.PlannerAgent.Version.Version})");
        Console.WriteLine("[SUMMARY] Foundry bootstrap completed");
    }

    private static async Task<AIProjectDeployment> ValidateDeploymentAsync(
        AIProjectClient projectClient,
        string deploymentName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await projectClient.Deployments.GetDeploymentAsync(deploymentName, cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"La configuracion 'CasoC:AzureOpenAiDeployment' con valor '{deploymentName}' no existe en el proyecto o no es accesible.",
                ex);
        }
    }

    private static async Task<AgentRecord> ValidateOrderAgentIdStrictAsync(
        AIProjectClient projectClient,
        string orderAgentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await projectClient.Agents.GetAgentAsync(orderAgentId, cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"La configuracion 'CasoC:OrderAgentId' con valor '{orderAgentId}' no existe en el proyecto o no es accesible.",
                ex);
        }
    }

    private static async Task<ValidatedAgentBinding> ValidateOrderAgentBindingAsync(
        AIProjectClient projectClient,
        string orderAgentId,
        CancellationToken cancellationToken)
    {
        AgentRecord orderAgent = await ValidateOrderAgentIdStrictAsync(projectClient, orderAgentId, cancellationToken);
        AgentVersion orderAgentVersion = await ResolveLatestAgentVersionAsync(projectClient, orderAgent.Name, cancellationToken);

        return new ValidatedAgentBinding(orderAgent.Name, orderAgent.Id, orderAgentVersion.Version);
    }

    private static async Task ValidateProjectAccessAsync(
        AIProjectClient projectClient,
        CancellationToken cancellationToken)
    {
        await foreach (AgentRecord _ in projectClient.Agents.GetAgentsAsync(limit: 1, cancellationToken: cancellationToken))
        {
            break;
        }
    }

    private static async Task<AgentVersion> ResolveLatestAgentVersionAsync(
        AIProjectClient projectClient,
        string agentName,
        CancellationToken cancellationToken)
    {
        await foreach (AgentVersion version in projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentName,
                           limit: 1,
                           order: AgentListOrder.Descending,
                           cancellationToken: cancellationToken))
        {
            return version;
        }

        throw new InvalidOperationException(
            $"No se encontro ninguna version para el agente '{agentName}'.");
    }

    private static CasoCSettings LoadSettings()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        CasoCSettings? settings = configuration
            .GetSection(CasoCSettings.SectionName)
            .Get<CasoCSettings>();

        if (settings is null)
        {
            throw new InvalidOperationException(
                $"No se encontro la seccion '{CasoCSettings.SectionName}' en appsettings.json.");
        }

        return settings;
    }

    private static string GetRequiredSetting(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"La clave requerida '{key}' no esta configurada en appsettings.json.");
        }

        return value;
    }

    private static void ValidateProjectEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _) ||
            !endpoint.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La clave 'CasoC:AzureOpenAiEndpoint' debe ser un endpoint de proyecto Azure AI Foundry, por ejemplo: " +
                "https://<resource>.services.ai.azure.com/api/projects/<project>.");
        }
    }

    private static void PrintEndpointHint(string message)
    {
        if (message.Contains("api/projects", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("endpoint", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Hint: configura 'CasoC:AzureOpenAiEndpoint' con un endpoint de proyecto Foundry: https://<resource>.services.ai.azure.com/api/projects/<project>");
        }
    }

    private static void WriteClientError(ClientResultException ex)
    {
        Console.Error.WriteLine($"[ClientResultException] Status: {ex.Status}, Message: {ex.Message}");

        if (ex.Status is 401 or 403)
        {
            Console.Error.WriteLine("Hint: identity authorization failed. Ensure the principal has access in the Foundry project.");
        }

        if (ex.Status == 404)
        {
            Console.Error.WriteLine("Hint: resource not found. Verify 'CasoC:AzureOpenAiDeployment' and 'CasoC:OrderAgentId' in the same Foundry project.");
        }

        if (ex.GetRawResponse() is { } rawResponse)
        {
            string requestId = rawResponse.Headers.TryGetValue("x-request-id", out string? rid)
                ? rid ?? "(unavailable)"
                : "(unavailable)";
            string clientRequestId = rawResponse.Headers.TryGetValue("x-ms-client-request-id", out string? crid)
                ? crid ?? "(unavailable)"
                : "(unavailable)";

            Console.Error.WriteLine($"RequestId: {requestId}");
            Console.Error.WriteLine($"ClientRequestId: {clientRequestId}");
        }
    }
}

internal sealed record BootstrapSummary(
    ValidatedAgentBinding OrderAgent,
    ReconcileResult PolicyAgent,
    ReconcileResult PlannerAgent);

internal sealed record ValidatedAgentBinding(string Name, string Id, string Version);
