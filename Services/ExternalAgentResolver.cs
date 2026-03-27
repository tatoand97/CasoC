using Azure.AI.Projects;
using Azure.AI.Projects.Agents;

namespace CasoC.Services;

internal sealed class ExternalAgentResolver
{
    private readonly AIProjectClient _projectClient;

    internal ExternalAgentResolver(AIProjectClient projectClient)
    {
        _projectClient = projectClient;
    }

    internal async Task<AgentVersion> ResolveRequiredAgentVersionAsync(
        string configuredAgentId,
        CancellationToken cancellationToken)
    {
        List<AgentRecord> agents = await GetAgentsAsync(cancellationToken);

        foreach (AgentRecord agent in agents)
        {
            if (string.Equals(agent.Id, configuredAgentId, StringComparison.Ordinal))
            {
                AgentVersion latestVersion = await GetRequiredLatestVersionAsync(agent.Name, cancellationToken);
                return latestVersion;
            }
        }

        foreach (AgentRecord agent in agents)
        {
            await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                               agentName: agent.Name,
                               order: AgentListOrder.Descending,
                               cancellationToken: cancellationToken))
            {
                if (string.Equals(version.Id, configuredAgentId, StringComparison.Ordinal))
                {
                    return version;
                }
            }
        }

        throw new InvalidOperationException(
            $"The configured external OrderAgent reference '{configuredAgentId}' from 'CasoC:OrderAgentId' was not found in the current Foundry project. " +
            "Configure an existing OrderAgent agent id or agent version id and rerun bootstrap.");
    }

    private async Task<List<AgentRecord>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        List<AgentRecord> agents = [];

        await foreach (AgentRecord agent in _projectClient.Agents.GetAgentsAsync(
                           order: AgentListOrder.Descending,
                           cancellationToken: cancellationToken))
        {
            agents.Add(agent);
        }

        return agents;
    }

    private async Task<AgentVersion> GetRequiredLatestVersionAsync(
        string agentName,
        CancellationToken cancellationToken)
    {
        await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentName,
                           limit: 1,
                           order: AgentListOrder.Descending,
                           cancellationToken: cancellationToken))
        {
            return version;
        }

        throw new InvalidOperationException(
            $"The external OrderAgent '{agentName}' was found, but no versions were returned by the Foundry project.");
    }
}
