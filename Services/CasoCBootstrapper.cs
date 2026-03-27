using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using CasoC.Agents;
using System.ClientModel;

namespace CasoC.Services;

internal sealed class CasoCBootstrapper
{
    private readonly AIProjectClient _projectClient;
    private readonly CasoCA2ASettings _settings;
    private readonly AgentReconciler _reconciler;
    private readonly ExternalAgentResolver _externalAgentResolver;

    internal CasoCBootstrapper(AIProjectClient projectClient, CasoCA2ASettings settings)
    {
        _projectClient = projectClient;
        _settings = settings;
        _reconciler = new AgentReconciler(projectClient);
        _externalAgentResolver = new ExternalAgentResolver(projectClient);
    }

    internal async Task<BootstrapSummary> BootstrapAsync(CancellationToken cancellationToken)
    {
        AIProjectDeployment deployment = await ValidateDeploymentAsync(
            _settings.ModelDeploymentName!,
            cancellationToken);
        Console.WriteLine("[CONFIG] Deployment validated");

        await ValidateProjectAccessAsync(cancellationToken);
        Console.WriteLine("[VALIDATION] Project access validated");

        AgentVersion orderAgent = await _externalAgentResolver.ResolveRequiredAgentVersionAsync(
            _settings.OrderAgentId!,
            cancellationToken);
        Console.WriteLine("[VALIDATION] OrderAgent validated");

        ReconcileResult policyResult = await _reconciler.ReconcileAsync(
            _settings.PolicyAgentName!,
            PolicyAgentFactory.Build(deployment.Name),
            cancellationToken);
        Console.WriteLine($"[RECONCILE] {policyResult.Version.Name} => {policyResult.ReconciliationStatus}");

        A2AToolBinding orderBinding = ResolveA2AConnection(
            "OrderAgent",
            _settings.OrderA2AConnectionName!,
            _settings.OrderA2ABaseUri,
            "CasoC:OrderA2AConnectionName",
            "CasoC:OrderA2ABaseUri",
            "Create the Order A2A connection in Foundry portal before rerunning bootstrap.");
        Console.WriteLine(
            $"[VALIDATION] Order A2A connection validated => name: {orderBinding.Name}, id: {orderBinding.Id}, type: {orderBinding.Type}");

        A2AToolBinding policyBinding = ResolveA2AConnection(
            "PolicyAgent",
            _settings.PolicyA2AConnectionName!,
            _settings.PolicyA2ABaseUri,
            "CasoC:PolicyA2AConnectionName",
            "CasoC:PolicyA2ABaseUri",
            "Create the Policy A2A connection in Foundry portal for the reconciled PolicyAgent, then rerun bootstrap. " +
            "If needed, use the two-step sequence: 1. create PolicyAgent, 2. create the Policy A2A connection, 3. rerun bootstrap.");
        Console.WriteLine(
            $"[VALIDATION] Policy A2A connection validated => name: {policyBinding.Name}, id: {policyBinding.Id}, type: {policyBinding.Type}");

        ReconcileResult plannerResult = await _reconciler.ReconcileAsync(
            _settings.PlannerAgentName!,
            PlannerAgentFactory.Build(deployment.Name, orderBinding, policyBinding),
            cancellationToken);
        Console.WriteLine($"[RECONCILE] {plannerResult.Version.Name} => {plannerResult.ReconciliationStatus}");

        return new BootstrapSummary(
            deployment.Name,
            orderAgent,
            policyResult,
            plannerResult,
            orderBinding,
            policyBinding);
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
        string targetAgent,
        string connectionName,
        string? configuredBaseUri,
        string connectionSettingKey,
        string baseUriSettingKey,
        string missingConnectionGuidance)
    {
        AIProjectConnection connection;

        try
        {
            connection = _projectClient.Connections.GetConnection(connectionName);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw BuildMissingConnectionException(targetAgent, connectionName, connectionSettingKey, missingConnectionGuidance, ex);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw BuildMissingConnectionException(targetAgent, connectionName, connectionSettingKey, missingConnectionGuidance, ex);
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
            targetAgent,
            connection.Name,
            connection.Id,
            connectionType,
            baseUri);
    }

    private static InvalidOperationException BuildMissingConnectionException(
        string targetAgent,
        string connectionName,
        string connectionSettingKey,
        string missingConnectionGuidance,
        Exception innerException)
    {
        return new InvalidOperationException(
            $"The required {targetAgent} A2A connection '{connectionName}' from '{connectionSettingKey}' was not found in the Foundry project. " +
            missingConnectionGuidance,
            innerException);
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
    AgentVersion OrderAgent,
    ReconcileResult PolicyAgent,
    ReconcileResult PlannerAgent,
    A2AToolBinding OrderBinding,
    A2AToolBinding PolicyBinding);
