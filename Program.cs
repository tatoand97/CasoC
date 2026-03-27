using Azure;
using Azure.AI.Projects;
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
        CasoCA2ASettings settings = LoadSettings();
        string endpoint = GetRequiredSetting(settings.ProjectEndpoint, "CasoC:ProjectEndpoint");
        _ = GetRequiredSetting(settings.ModelDeploymentName, "CasoC:ModelDeploymentName");
        _ = GetRequiredSetting(settings.PlannerAgentName, "CasoC:PlannerAgentName");
        _ = GetRequiredSetting(settings.OrderA2AConnectionName, "CasoC:OrderA2AConnectionName");
        _ = GetRequiredSetting(settings.PolicyA2AConnectionName, "CasoC:PolicyA2AConnectionName");

        ValidateProjectEndpoint(endpoint);
        Console.WriteLine($"[CONFIG] Endpoint validated => {endpoint}");

        AIProjectClient projectClient = CreateProjectClient(endpoint);
        CasoCA2ABootstrapper bootstrapper = new(projectClient, settings);
        BootstrapSummary summary = await bootstrapper.BootstrapAsync(cancellationToken);

        WriteReconciliationResult(summary.PlannerAgent);

        return summary;
    }

    private static AIProjectClient CreateProjectClient(string endpoint)
    {
        return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    private static void WriteReconciliationResult(ReconcileResult result)
    {
        Console.WriteLine(
            $"[RECONCILE] {result.Version.Name} => {result.ReconciliationStatus} (id: {result.Version.Id}, version: {result.Version.Version})");
    }

    private static void WriteBootstrapSummary(BootstrapSummary summary)
    {
        Console.WriteLine(
            $"[SUMMARY] PlannerAgent => name: {summary.PlannerAgent.Version.Name}, id: {summary.PlannerAgent.Version.Id}, version: {summary.PlannerAgent.Version.Version}");
        Console.WriteLine(
            $"[SUMMARY] Order A2A connection => name: {summary.OrderBinding.Name}, id: {summary.OrderBinding.Id}, type: {summary.OrderBinding.Type}");
        Console.WriteLine(
            $"[SUMMARY] Policy A2A connection => name: {summary.PolicyBinding.Name}, id: {summary.PolicyBinding.Id}, type: {summary.PolicyBinding.Type}");
        Console.WriteLine(
            $"[SUMMARY] Prerequisites validated => project access, model deployment '{summary.ModelDeploymentName}', Order A2A connection, Policy A2A connection");
        Console.WriteLine("[SUMMARY] PlannerAgent bootstrap for A2A completed");
    }

    private static CasoCA2ASettings LoadSettings()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        CasoCA2ASettings? settings = configuration
            .GetSection(CasoCA2ASettings.SectionName)
            .Get<CasoCA2ASettings>();

        if (settings is null)
        {
            throw new InvalidOperationException(
                $"The '{CasoCA2ASettings.SectionName}' section was not found in appsettings.json.");
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
            Console.Error.WriteLine("Hint: resource not found. Verify 'CasoC:ModelDeploymentName', 'CasoC:OrderA2AConnectionName', and 'CasoC:PolicyA2AConnectionName' in the same Foundry project.");
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
