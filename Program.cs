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
    public static async Task Main()
    {
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            cts.Cancel();
        };

        try
        {
            CasoCSettings settings = LoadSettings();
            string endpoint = GetRequiredSetting(settings.AzureOpenAiEndpoint, "CasoC:AzureOpenAiEndpoint");
            string deploymentName = GetRequiredSetting(settings.AzureOpenAiDeployment, "CasoC:AzureOpenAiDeployment");
            string orderAgentId = GetRequiredSetting(settings.OrderAgentId, "CasoC:OrderAgentId");

            ValidateProjectEndpoint(endpoint);

            AIProjectClient projectClient = new(new Uri(endpoint), new AzureCliCredential());

            await ValidateIdentityCanAccessProjectAsync(projectClient, cts.Token);
            Console.WriteLine($"[CONFIG] Endpoint validado => {endpoint}");

            AIProjectDeployment deployment = await ValidateDeploymentAsync(projectClient, deploymentName, cts.Token);
            Console.WriteLine($"[CONFIG] Deployment validado => {deployment.Name}");

            AgentRecord orderAgent = await ValidateOrderAgentIdStrictAsync(projectClient, orderAgentId, cts.Token);
            AgentVersion orderAgentVersion = await ResolveLatestAgentVersionAsync(projectClient, orderAgent.Name, cts.Token);
            Console.WriteLine(
                $"[VALIDATION] OrderAgentId validado => {orderAgentId} (latest version: {orderAgentVersion.Version})");

            AgentReconciler reconciler = new(projectClient);

            ReconcileResult policyResult = await reconciler.ReconcileAsync(
                PolicyAgentFactory.AgentName,
                PolicyAgentFactory.Build(deploymentName),
                cts.Token);
            PrintReconciliationResult(policyResult);

            ReconcileResult plannerResult = await reconciler.ReconcileAsync(
                PlannerAgentFactory.AgentName,
                PlannerAgentFactory.Build(deploymentName),
                cts.Token);
            PrintReconciliationResult(plannerResult);

            Console.WriteLine(
                $"[SUMMARY] Bindings => OrderAgent={orderAgent.Name} (id: {orderAgent.Id}, version: {orderAgentVersion.Version}); " +
                $"PolicyAgent={policyResult.Version.Name} (id: {policyResult.Version.Id}, version: {policyResult.Version.Version}); " +
                $"PlannerAgent={plannerResult.Version.Name} (id: {plannerResult.Version.Id}, version: {plannerResult.Version.Version})");
            Console.WriteLine("[SUMMARY] Foundry bootstrap completed");

            Environment.ExitCode = 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("[OperationCanceledException] Operacion cancelada por el usuario.");
            Environment.ExitCode = 1;
        }
        catch (RequestFailedException ex)
        {
            Console.Error.WriteLine($"[RequestFailedException] Status: {ex.Status}, Code: {ex.ErrorCode}, Message: {ex.Message}");
            PrintEndpointHint(ex.Message);
            Environment.ExitCode = 1;
        }
        catch (ClientResultException ex)
        {
            WriteClientError(ex);
            PrintEndpointHint(ex.Message);
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{ex.GetType().Name}] {ex.Message}");
            PrintEndpointHint(ex.Message);
            Environment.ExitCode = 1;
        }
    }

    private static void PrintReconciliationResult(ReconcileResult result)
    {
        Console.WriteLine(
            $"[RECONCILE] {result.Version.Name} => {result.ReconciliationStatus} (id: {result.Version.Id}, version: {result.Version.Version})");
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

    private static async Task ValidateIdentityCanAccessProjectAsync(
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
