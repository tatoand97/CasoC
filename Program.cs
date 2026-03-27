using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
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
            Console.Error.WriteLine("[OperationCanceledException] Operation cancelled by the user.");
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
        string endpoint = GetRequiredSetting(settings.ProjectEndpoint, "CasoC:ProjectEndpoint");
        _ = GetRequiredSetting(settings.ModelDeploymentName, "CasoC:ModelDeploymentName");
        _ = GetRequiredSetting(settings.OrderAgentId, "CasoC:OrderAgentId");
        _ = GetRequiredSetting(settings.PolicyAgentName, "CasoC:PolicyAgentName");
        _ = GetRequiredSetting(settings.PlannerAgentName, "CasoC:PlannerAgentName");
        _ = GetRequiredSetting(settings.OrderA2AConnectionName, "CasoC:OrderA2AConnectionName");
        _ = GetRequiredSetting(settings.PolicyA2AConnectionName, "CasoC:PolicyA2AConnectionName");

        ValidateProjectEndpoint(endpoint);
        Console.WriteLine($"[CONFIG] Endpoint validated => {endpoint}");

        AIProjectClient projectClient = CreateProjectClient(endpoint);
        CasoCBootstrapper bootstrapper = new(projectClient, settings);
        return await bootstrapper.BootstrapAsync(cancellationToken);
    }

    private static AIProjectClient CreateProjectClient(string endpoint)
    {
        return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    private static void WriteBootstrapSummary(BootstrapSummary summary)
    {
        WriteAgentSummary("OrderAgent", summary.OrderAgent);
        WriteAgentSummary("PolicyAgent", summary.PolicyAgent.Version);
        WriteAgentSummary("PlannerAgent", summary.PlannerAgent.Version);
        Console.WriteLine(
            $"[SUMMARY] Planner bindings => OrderAgent via '{summary.OrderBinding.Name}', PolicyAgent via '{summary.PolicyBinding.Name}'");
        Console.WriteLine(
            $"[SUMMARY] Order A2A connection => name: {summary.OrderBinding.Name}, id: {summary.OrderBinding.Id}, type: {summary.OrderBinding.Type}");
        Console.WriteLine(
            $"[SUMMARY] Policy A2A connection => name: {summary.PolicyBinding.Name}, id: {summary.PolicyBinding.Id}, type: {summary.PolicyBinding.Type}");
        Console.WriteLine(
            $"[SUMMARY] Model deployment => {summary.ModelDeploymentName}");
        Console.WriteLine("[SUMMARY] PlannerAgent bootstrap for A2A completed");
    }

    private static void WriteAgentSummary(string label, AgentVersion agentVersion)
    {
        Console.WriteLine(
            $"[SUMMARY] {label} => id: {agentVersion.Id}, name: {agentVersion.Name}, version: {agentVersion.Version}");
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
                $"The '{CasoCSettings.SectionName}' section was not found in appsettings.json.");
        }

        return settings;
    }

    private static string GetRequiredSetting(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The required setting '{key}' is not configured in appsettings.json.");
        }

        return value;
    }

    private static void ValidateProjectEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _) ||
            !endpoint.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The setting 'CasoC:ProjectEndpoint' must be a valid Azure AI Foundry project endpoint, for example: " +
                "https://<resource>.services.ai.azure.com/api/projects/<project>.");
        }
    }

    private static void PrintEndpointHint(string message)
    {
        if (message.Contains("api/projects", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("endpoint", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Hint: configure 'CasoC:ProjectEndpoint' with a Foundry project endpoint: https://<resource>.services.ai.azure.com/api/projects/<project>");
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
            Console.Error.WriteLine("Hint: resource not found. Verify 'CasoC:ModelDeploymentName', 'CasoC:OrderAgentId', 'CasoC:OrderA2AConnectionName', and 'CasoC:PolicyA2AConnectionName' in the same Foundry project.");
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
