using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using CasoC.Agents;
using CasoC.Services;
using System.ClientModel;

namespace CasoC;

internal static class Program
{
    private const string TestPrompt = "Dame el estado de la orden ORD-000001 y dime si requiere acción.";

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
            string endpoint = GetRequiredEnv("AZURE_OPENAI_ENDPOINT");
            string deployment = GetRequiredEnv("AZURE_OPENAI_DEPLOYMENT");
            string orderAgentId = GetRequiredEnv("ORDER_AGENT_ID");
            int timeoutSeconds = GetOptionalPositiveInt("RESPONSES_TIMEOUT_SECONDS", 60);
            int maxBackoffSeconds = GetOptionalPositiveInt("RESPONSES_MAX_BACKOFF_SECONDS", 8);

            ValidateProjectEndpoint(endpoint);

            Console.WriteLine("Inicializando clientes...");
            AIProjectClient projectClient = new(new Uri(endpoint), new DefaultAzureCredential());
            ProjectOpenAIClient openAiClient = projectClient.OpenAI;

            await ValidateIdentityCanAccessProjectAsync(projectClient, cts.Token);
            await ValidateOrderAgentIdStrictAsync(projectClient, orderAgentId, cts.Token);
            Console.WriteLine($"ORDER_AGENT_ID validated: {orderAgentId}");

            AgentToolFactory toolFactory = new();
            PolicyAgentFactory policyFactory = new();
            PlannerAgentFactory plannerFactory = new(toolFactory);
            AgentReconciler reconciler = new(projectClient);
            AgentRunner runner = new(TimeSpan.FromSeconds(maxBackoffSeconds));

            ReconcileResult policyResult = await reconciler.ReconcileAsync(
                PolicyAgentFactory.AgentName,
                policyFactory.Build(deployment),
                cts.Token);

            PrintReconciliationResult("PolicyAgent", policyResult);

            PromptAgentDefinition plannerDefinition = plannerFactory.Build(
                deployment,
                orderAgentId,
                policyResult.Version.Id);

            Console.WriteLine($"Tool binding: order_agent -> {orderAgentId}");
            Console.WriteLine($"Tool binding: policy_agent -> {policyResult.Version.Id}");

            ReconcileResult plannerResult = await reconciler.ReconcileAsync(
                PlannerAgentFactory.AgentName,
                plannerDefinition,
                cts.Token);

            PrintReconciliationResult("PlannerAgent", plannerResult);

            string finalText = await runner.RunPromptAsync(
                openAiClient,
                plannerResult.Version.Name,
                TestPrompt,
                TimeSpan.FromSeconds(timeoutSeconds),
                cts.Token);

            Console.WriteLine();
            Console.WriteLine("===== RESPUESTA FINAL DEL PLANNER =====");
            Console.WriteLine(finalText);
            Console.WriteLine("=======================================");
        }
        catch (TimeoutException ex)
        {
            Console.Error.WriteLine($"[TimeoutException] {ex.Message}");
            Environment.ExitCode = 1;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("[OperationCanceledException] Operación cancelada por el usuario.");
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

    private static void PrintReconciliationResult(string label, ReconcileResult result)
    {
        Console.WriteLine($"{label} reconciliation:");
        Console.WriteLine($"  AgentName: {result.Version.Name}");
        Console.WriteLine($"  AgentId: {result.Version.Id}");
        Console.WriteLine($"  AgentVersion: {result.Version.Version}");
        Console.WriteLine($"  ReconciliationStatus: {result.ReconciliationStatus}");
    }

    private static async Task ValidateOrderAgentIdStrictAsync(
        AIProjectClient projectClient,
        string orderAgentId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await projectClient.Agents.GetAgentAsync(orderAgentId, cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"ORDER_AGENT_ID '{orderAgentId}' no existe en el proyecto o no es accesible.",
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

    private static string GetRequiredEnv(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing environment variable '{name}'.");
        }

        return value;
    }

    private static int GetOptionalPositiveInt(string name, int defaultValue)
    {
        string? rawValue = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue, out int value) || value <= 0)
        {
            throw new InvalidOperationException($"Environment variable '{name}' must be a positive integer.");
        }

        return value;
    }

    private static void ValidateProjectEndpoint(string endpoint)
    {
        if (!endpoint.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AZURE_OPENAI_ENDPOINT must be an Azure AI Foundry project endpoint, for example: " +
                "https://<resource>.services.ai.azure.com/api/projects/<project>.");
        }
    }

    private static void PrintEndpointHint(string message)
    {
        if (message.Contains("api/projects", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("endpoint", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Hint: AZURE_OPENAI_ENDPOINT debe ser endpoint de proyecto Foundry: https://<resource>.services.ai.azure.com/api/projects/<project>");
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
            Console.Error.WriteLine("Hint: resource not found. Verify ORDER_AGENT_ID and model deployment in the same Foundry project.");
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
