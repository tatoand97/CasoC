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
    private const string TestPrompt = "Dame el estado de la orden ORD-000001 y dime si requiere acción.";
    private const string OrderAgentPromptTemplate =
        """
        Extrae y devuelve solo los datos relevantes para resolver esta solicitud.
        Si faltan datos, indícalo explícitamente.

        Solicitud del usuario:
        {0}
        """;
    private const string PolicyAgentPromptTemplate =
        """
        Evalúa la siguiente información de la orden y determina si requiere acción.
        Responde de forma clara y profesional sin agregar información nueva.

        Solicitud original:
        {0}

        Datos de la orden:
        {1}
        """;
    private const string PlannerPromptTemplate =
        """
        Genera la respuesta final para el usuario usando exclusivamente el contexto disponible.

        Solicitud original:
        {0}

        Datos obtenidos de la orden:
        {1}

        Evaluación de política:
        {2}
        """;

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
            string deployment = GetRequiredSetting(settings.AzureOpenAiDeployment, "CasoC:AzureOpenAiDeployment");
            string orderAgentId = GetRequiredSetting(settings.OrderAgentId, "CasoC:OrderAgentId");
            // Nueva: valor opcional para escoger versión. Si es null/empty/"latest" -> usar la última.
            string? orderAgentVersionSetting = settings.OrderAgentVersion;
            int timeoutSeconds = GetPositiveSetting(settings.ResponsesTimeoutSeconds, "CasoC:ResponsesTimeoutSeconds");
            int maxBackoffSeconds = GetPositiveSetting(settings.ResponsesMaxBackoffSeconds, "CasoC:ResponsesMaxBackoffSeconds");

            ValidateProjectEndpoint(endpoint);

            Console.WriteLine("Inicializando clientes...");
            AIProjectClient projectClient = new(new Uri(endpoint), new AzureCliCredential());
            ProjectOpenAIClient openAiClient = projectClient.OpenAI;

            await ValidateIdentityCanAccessProjectAsync(projectClient, cts.Token);
            await ValidateOrderAgentIdStrictAsync(projectClient, orderAgentId, cts.Token);
            Console.WriteLine($"OrderAgentId validado: {orderAgentId}");

            AgentReconciler reconciler = new(projectClient);
            AgentRunner runner = new(TimeSpan.FromSeconds(maxBackoffSeconds));

            ReconcileResult policyResult = await reconciler.ReconcileAsync(
                PolicyAgentFactory.AgentName,
                PolicyAgentFactory.Build(deployment),
                cts.Token);

            PrintReconciliationResult("PolicyAgent", policyResult);

            ReconcileResult plannerResult = await reconciler.ReconcileAsync(
                PlannerAgentFactory.AgentName,
                PlannerAgentFactory.Build(deployment),
                cts.Token);

            PrintReconciliationResult("PlannerAgent", plannerResult);

            // Resuelve la versión del OrderAgent a usar (nombre de versión que consume Responses).
            string orderAgentResponseName = await ResolveOrderAgentVersionAsync(projectClient, orderAgentId, orderAgentVersionSetting, cts.Token);
            Console.WriteLine($"Usando OrderAgent (response client name): {orderAgentResponseName}"); 

            string orderContext = await runner.RunPromptAsync(
                openAiClient,
                orderAgentResponseName,
                string.Format(OrderAgentPromptTemplate, TestPrompt),
                TimeSpan.FromSeconds(timeoutSeconds),
                cts.Token);

            Console.WriteLine();
            Console.WriteLine("===== CONTEXTO DEL ORDER AGENT =====");
            Console.WriteLine(orderContext);
            Console.WriteLine("====================================");

            string policyResultText = await runner.RunPromptAsync(
                openAiClient,
                policyResult.Version.Name,
                string.Format(PolicyAgentPromptTemplate, TestPrompt, orderContext),
                TimeSpan.FromSeconds(timeoutSeconds),
                cts.Token);

            Console.WriteLine();
            Console.WriteLine("===== EVALUACION DEL POLICY AGENT =====");
            Console.WriteLine(policyResultText);
            Console.WriteLine("=======================================");

            string finalText = await runner.RunPromptAsync(
                openAiClient,
                plannerResult.Version.Name,
                string.Format(PlannerPromptTemplate, TestPrompt, orderContext, policyResultText),
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
                $"La configuración 'CasoC:OrderAgentId' con valor '{orderAgentId}' no existe en el proyecto o no es accesible.",
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

    // Nueva: resuelve el nombre de versión (Version.Name) para usar con ProjectResponsesClient.
    private static async Task<string> ResolveOrderAgentVersionAsync(
        AIProjectClient projectClient,
        string agentNameOrId,
        string? versionSetting,
        CancellationToken cancellationToken)
    {
        // Si no se especifica versión o está marcada como "latest", devolver la versión más reciente.
        if (string.IsNullOrWhiteSpace(versionSetting) ||
            string.Equals(versionSetting, "latest", StringComparison.OrdinalIgnoreCase))
        {
            await foreach (AgentVersion version in projectClient.Agents.GetAgentVersionsAsync(
                               agentName: agentNameOrId,
                               limit: 1,
                               order: AgentListOrder.Descending,
                               cancellationToken: cancellationToken))
            {
                return version.Name;
            }

            throw new InvalidOperationException($"No se encontró ninguna versión para el agente '{agentNameOrId}'.");
        }

        // Si el usuario indicó una cadena concreta, buscar una versión que coincida por Name, Id o Version.
        await foreach (AgentVersion version in projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentNameOrId,
                           cancellationToken: cancellationToken))
        {
            if (string.Equals(version.Name, versionSetting, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(version.Id, versionSetting, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(version.Version, versionSetting, StringComparison.OrdinalIgnoreCase))
            {
                return version.Name;
            }
        }

        throw new InvalidOperationException(
            $"No se encontró la versión '{versionSetting}' para el agente '{agentNameOrId}'. Use 'latest' o deje vacío para la versión más reciente.");
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
                $"No se encontró la sección '{CasoCSettings.SectionName}' en appsettings.json.");
        }

        return settings;
    }

    private static string GetRequiredSetting(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"La clave requerida '{key}' no está configurada en appsettings.json.");
        }

        return value;
    }

    private static int GetPositiveSetting(int value, string key)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException(
                $"La clave '{key}' en appsettings.json debe ser un entero positivo.");
        }

        return value;
    }

    private static void ValidateProjectEndpoint(string endpoint)
    {
        if (!endpoint.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase))
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
            Console.Error.WriteLine("Hint: resource not found. Verify 'CasoC:OrderAgentId' and 'CasoC:AzureOpenAiDeployment' in the same Foundry project.");
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
