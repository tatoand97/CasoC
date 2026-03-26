using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using CasoC.Agents;
using CasoC.Services;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CasoC;

internal static class Program
{
    private const string TestUserRequest = "Dame el estado de la orden ORD-000001 y dime si requiere accion.";
    private const string SupportedOrderStatusList = "Created, Confirmed, Packed, Shipped, Delivered, Cancelled, Unknown, NotFound";

    private const string OrderAgentPromptTemplate =
        """
        Recupera solo datos estructurados de la orden solicitada. Usa tu herramienta MCP configurada si aplica.
        Devuelve un unico objeto JSON y nada mas.
        No uses markdown.
        No agregues explicaciones ni texto fuera del JSON.
        Campos requeridos: "id", "status", "requiresAction".
        Campo opcional: "reason".
        Valores validos para "status": "Created", "Confirmed", "Packed", "Shipped", "Delivered", "Cancelled", "Unknown", "NotFound".
        Si no encuentras la orden, devuelve:
        {{"id":"<id solicitado>","status":"NotFound","requiresAction":false,"reason":"Order not found"}}
        Si no puedes clasificar el estado con certeza, usa "Unknown" y explica el motivo en "reason".

        Solicitud del usuario:
        {0}
        """;

    private const string PolicyAgentPromptTemplate =
        """
        Evalua esta orden validada y devuelve la evaluacion de politica en el formato requerido.
        Devuelve solo JSON valido sin markdown ni texto adicional.
        Usa exactamente este formato:
        {{"requiresAction": true, "message": "explicacion breve"}}

        Orden validada:
        {0}
        """;

    private const string PlannerPromptTemplate =
        """
        Genera la respuesta final para el usuario usando solo este contexto validado.

        Solicitud original:
        {0}

        Datos de la orden:
        {1}

        Evaluacion de politica:
        {2}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Dictionary<string, string> SupportedOrderStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Created"] = "Created",
        ["Confirmed"] = "Confirmed",
        ["Packed"] = "Packed",
        ["Shipped"] = "Shipped",
        ["Delivered"] = "Delivered",
        ["Cancelled"] = "Cancelled",
        ["Unknown"] = "Unknown",
        ["NotFound"] = "NotFound",
    };

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
            string? orderAgentVersionSetting = settings.OrderAgentVersion;
            int timeoutSeconds = GetPositiveSetting(settings.ResponsesTimeoutSeconds, "CasoC:ResponsesTimeoutSeconds");
            int maxBackoffSeconds = GetPositiveSetting(settings.ResponsesMaxBackoffSeconds, "CasoC:ResponsesMaxBackoffSeconds");

            ValidateProjectEndpoint(endpoint);

            Console.WriteLine("Inicializando clientes...");
            AIProjectClient projectClient = new(new Uri(endpoint), new AzureCliCredential());
            ProjectOpenAIClient openAiClient = projectClient.OpenAI;
            TimeSpan responseTimeout = TimeSpan.FromSeconds(timeoutSeconds);

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

            string orderAgentResponseName = await ResolveOrderAgentVersionAsync(
                projectClient,
                orderAgentId,
                orderAgentVersionSetting,
                cts.Token);

            Console.WriteLine($"Usando OrderAgent (response client name): {orderAgentResponseName}");

            string orderAgentResponse = await RunOrderAgentAsync(
                runner,
                openAiClient,
                orderAgentResponseName,
                TestUserRequest,
                responseTimeout,
                cts.Token);

            ValidatedOrderContext orderContext = ParseAndValidateOrderContext(orderAgentResponse);
            string validatedOrderJson = SerializeValidatedJson(orderContext);
            WriteConsoleSection("ORDER PAYLOAD VALIDADO", validatedOrderJson);

            string policyAgentResponse = await RunPolicyAgentAsync(
                runner,
                openAiClient,
                policyResult.Version.Name,
                validatedOrderJson,
                responseTimeout,
                cts.Token);

            ValidatedPolicyAssessment policyAssessment = ParseAndValidatePolicyAssessment(policyAgentResponse);
            string validatedPolicyJson = SerializeValidatedJson(policyAssessment);
            WriteConsoleSection("POLICY RESULT VALIDADO", validatedPolicyJson);

            string finalText = await RunPlannerAgentAsync(
                runner,
                openAiClient,
                plannerResult.Version.Name,
                TestUserRequest,
                validatedOrderJson,
                validatedPolicyJson,
                responseTimeout,
                cts.Token);

            WriteConsoleSection("RESPUESTA FINAL", finalText);
        }
        catch (TimeoutException ex)
        {
            Console.Error.WriteLine($"[TimeoutException] {ex.Message}");
            Environment.ExitCode = 1;
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

    private static async Task<string> RunOrderAgentAsync(
        AgentRunner runner,
        ProjectOpenAIClient openAiClient,
        string orderAgentResponseName,
        string userRequest,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await runner.RunPromptAsync(
            openAiClient,
            orderAgentResponseName,
            string.Format(OrderAgentPromptTemplate, userRequest),
            timeout,
            cancellationToken);
    }

    private static ValidatedOrderContext ParseAndValidateOrderContext(string responseText)
    {
        OrderAgentResponseDto payload = DeserializeJsonObject<OrderAgentResponseDto>(responseText, "OrderAgent");

        if (string.IsNullOrWhiteSpace(payload.Id))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'id' or it is empty. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (string.IsNullOrWhiteSpace(payload.Status))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'status' or it is empty. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (payload.RequiresAction is null)
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'requiresAction'. Raw response: {BuildResponseSnippet(responseText)}");
        }

        string normalizedStatus = NormalizeOrderStatus(payload.Status);

        return new ValidatedOrderContext
        {
            Id = payload.Id.Trim(),
            Status = normalizedStatus,
            RequiresAction = payload.RequiresAction.Value,
            Reason = string.IsNullOrWhiteSpace(payload.Reason) ? null : payload.Reason.Trim(),
        };
    }

    private static async Task<string> RunPolicyAgentAsync(
        AgentRunner runner,
        ProjectOpenAIClient openAiClient,
        string policyAgentName,
        string validatedOrderJson,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await runner.RunPromptAsync(
            openAiClient,
            policyAgentName,
            string.Format(PolicyAgentPromptTemplate, validatedOrderJson),
            timeout,
            cancellationToken);
    }

    private static ValidatedPolicyAssessment ParseAndValidatePolicyAssessment(string responseText)
    {
        PolicyAgentResponseDto payload = DeserializeJsonObject<PolicyAgentResponseDto>(responseText, "PolicyAgent");

        if (payload.RequiresAction is null)
        {
            throw new InvalidOperationException(
                $"PolicyAgent JSON is missing required field 'requiresAction'. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (string.IsNullOrWhiteSpace(payload.Message))
        {
            throw new InvalidOperationException(
                $"PolicyAgent JSON is missing required field 'message' or it is empty. Raw response: {BuildResponseSnippet(responseText)}");
        }

        return new ValidatedPolicyAssessment
        {
            RequiresAction = payload.RequiresAction.Value,
            Message = payload.Message.Trim(),
        };
    }

    private static async Task<string> RunPlannerAgentAsync(
        AgentRunner runner,
        ProjectOpenAIClient openAiClient,
        string plannerAgentName,
        string userRequest,
        string validatedOrderJson,
        string validatedPolicyJson,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await runner.RunPromptAsync(
            openAiClient,
            plannerAgentName,
            string.Format(PlannerPromptTemplate, userRequest, validatedOrderJson, validatedPolicyJson),
            timeout,
            cancellationToken);
    }

    private static T DeserializeJsonObject<T>(string responseText, string agentLabel)
        where T : class
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"{agentLabel} returned invalid JSON. Expected a single JSON object. Raw response: {BuildResponseSnippet(responseText)}");
            }

            T? payload = JsonSerializer.Deserialize<T>(document.RootElement.GetRawText(), JsonOptions);
            return payload ?? throw new InvalidOperationException(
                $"{agentLabel} returned an empty JSON payload. Raw response: {BuildResponseSnippet(responseText)}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{agentLabel} returned invalid JSON. Expected a single JSON object. Raw response: {BuildResponseSnippet(responseText)}",
                ex);
        }
    }

    private static string NormalizeOrderStatus(string status)
    {
        string candidate = status.Trim();
        if (!SupportedOrderStatuses.TryGetValue(candidate, out string? normalized))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON field 'status' has unsupported value '{candidate}'. Supported values: {SupportedOrderStatusList}.");
        }

        return normalized;
    }

    private static string SerializeValidatedJson<T>(T payload)
    {
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildResponseSnippet(string responseText)
    {
        const int MaxLength = 240;
        string condensed = responseText
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (condensed.Length <= MaxLength)
        {
            return condensed;
        }

        return $"{condensed[..MaxLength]}...";
    }

    private static void WriteConsoleSection(string title, string content)
    {
        Console.WriteLine();
        Console.WriteLine($"===== {title} =====");
        Console.WriteLine(content);
        Console.WriteLine(new string('=', title.Length + 12));
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

    private static async Task<string> ResolveOrderAgentVersionAsync(
        AIProjectClient projectClient,
        string agentNameOrId,
        string? versionSetting,
        CancellationToken cancellationToken)
    {
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

            throw new InvalidOperationException($"No se encontro ninguna version para el agente '{agentNameOrId}'.");
        }

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
            $"No se encontro la version '{versionSetting}' para el agente '{agentNameOrId}'. Use 'latest' o deje vacio para la version mas reciente.");
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

    private sealed class OrderAgentResponseDto
    {
        public string? Id { get; init; }

        public string? Status { get; init; }

        public bool? RequiresAction { get; init; }

        public string? Reason { get; init; }
    }

    private sealed class ValidatedOrderContext
    {
        public required string Id { get; init; }

        public required string Status { get; init; }

        public required bool RequiresAction { get; init; }

        public string? Reason { get; init; }
    }

    private sealed class PolicyAgentResponseDto
    {
        public bool? RequiresAction { get; init; }

        public string? Message { get; init; }
    }

    private sealed class ValidatedPolicyAssessment
    {
        public required bool RequiresAction { get; init; }

        public required string Message { get; init; }
    }
}
