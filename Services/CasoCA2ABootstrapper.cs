using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using CasoC.Agents;
using System.ClientModel;

namespace CasoC.Services;

internal sealed class CasoCA2ABootstrapper
{
    private readonly AIProjectClient _projectClient;
    private readonly CasoCA2ASettings _settings;
    private readonly AgentReconciler _reconciler;

    internal CasoCA2ABootstrapper(AIProjectClient projectClient, CasoCA2ASettings settings)
    {
        _projectClient = projectClient;
        _settings = settings;
        _reconciler = new AgentReconciler(projectClient);
    }

    internal async Task<BootstrapSummary> BootstrapAsync(CancellationToken cancellationToken)
    {
        await ValidateProjectAccessAsync(cancellationToken);
        Console.WriteLine("[VALIDATION] Project access validated");

        AIProjectDeployment deployment = await ValidateDeploymentAsync(
            _settings.ModelDeploymentName!,
            cancellationToken);
        Console.WriteLine($"[VALIDATION] Model deployment validated => {deployment.Name}");

        A2AToolBinding orderBinding = ResolveA2AConnection(
            _settings.OrderA2AConnectionName!,
            _settings.OrderA2ABaseUri,
            "CasoC:OrderA2AConnectionName",
            "CasoC:OrderA2ABaseUri");
        Console.WriteLine(
            $"[VALIDATION] Order A2A connection validated => name: {orderBinding.Name}, id: {orderBinding.Id}, type: {orderBinding.Type}");

        A2AToolBinding policyBinding = ResolveA2AConnection(
            _settings.PolicyA2AConnectionName!,
            _settings.PolicyA2ABaseUri,
            "CasoC:PolicyA2AConnectionName",
            "CasoC:PolicyA2ABaseUri");
        Console.WriteLine(
            $"[VALIDATION] Policy A2A connection validated => name: {policyBinding.Name}, id: {policyBinding.Id}, type: {policyBinding.Type}");

        PromptAgentDefinition plannerDefinition = PlannerAgentFactory.Build(
            deployment.Name,
            orderBinding,
            policyBinding);

        ReconcileResult plannerResult = await _reconciler.ReconcileAsync(
            _settings.PlannerAgentName!,
            plannerDefinition,
            cancellationToken);

        return new BootstrapSummary(deployment.Name, orderBinding, policyBinding, plannerResult);
    }

    private async Task ValidateProjectAccessAsync(CancellationToken cancellationToken)
    {
        await foreach (AgentRecord _ in _projectClient.Agents.GetAgentsAsync(
                           limit: 1,
                           cancellationToken: cancellationToken))
        {
            break;
        }
    }

    private async Task<AIProjectDeployment> ValidateDeploymentAsync(
        string deploymentName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _projectClient.Deployments.GetDeploymentAsync(deploymentName, cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"The configured model deployment '{deploymentName}' from 'CasoC:ModelDeploymentName' does not exist in the Foundry project or is not accessible.",
                ex);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"The configured model deployment '{deploymentName}' from 'CasoC:ModelDeploymentName' does not exist in the Foundry project or is not accessible.",
                ex);
        }
    }

    private A2AToolBinding ResolveA2AConnection(
        string connectionName,
        string? configuredBaseUri,
        string connectionSettingKey,
        string baseUriSettingKey)
    {
        AIProjectConnection connection;

        try
        {
            connection = _projectClient.Connections.GetConnection(connectionName);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"The required A2A connection '{connectionName}' from '{connectionSettingKey}' was not found in the Foundry project.",
                ex);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"The required A2A connection '{connectionName}' from '{connectionSettingKey}' was not found in the Foundry project.",
                ex);
        }

        string connectionType = connection.Type.ToString();
        Uri? baseUri = null;

        if (!string.Equals(connectionType, "RemoteA2A", StringComparison.Ordinal))
        {
            baseUri = GetRequiredAbsoluteUri(
                configuredBaseUri,
                baseUriSettingKey,
                connection.Name,
                connectionType);
        }
        else if (!string.IsNullOrWhiteSpace(configuredBaseUri))
        {
            _ = ParseAbsoluteUri(configuredBaseUri, baseUriSettingKey);
        }

        return new A2AToolBinding(
            connection.Name,
            connection.Id,
            connectionType,
            baseUri);
    }

    private static Uri GetRequiredAbsoluteUri(
        string? configuredBaseUri,
        string baseUriSettingKey,
        string connectionName,
        string connectionType)
    {
        if (string.IsNullOrWhiteSpace(configuredBaseUri))
        {
            throw new InvalidOperationException(
                $"The connection '{connectionName}' is of type '{connectionType}' and does not carry the A2A service base URI. Configure '{baseUriSettingKey}' with an absolute URI.");
        }

        return ParseAbsoluteUri(configuredBaseUri, baseUriSettingKey);
    }

    private static Uri ParseAbsoluteUri(string configuredBaseUri, string baseUriSettingKey)
    {
        if (!Uri.TryCreate(configuredBaseUri, UriKind.Absolute, out Uri? baseUri))
        {
            throw new InvalidOperationException(
                $"The optional setting '{baseUriSettingKey}' must be a valid absolute URI when provided.");
        }

        return baseUri;
    }
}

internal sealed record BootstrapSummary(
    string ModelDeploymentName,
    A2AToolBinding OrderBinding,
    A2AToolBinding PolicyBinding,
    ReconcileResult PlannerAgent);
