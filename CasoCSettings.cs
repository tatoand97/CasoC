namespace CasoC;

internal sealed class CasoCSettings
{
    public const string SectionName = "CasoC";

    public string? AzureOpenAiEndpoint { get; init; }

    public string? AzureOpenAiDeployment { get; init; }

    public string? OrderAgentId { get; init; }

    // Opcional: si es null, vacio o "latest" se usara la version mas reciente disponible.
    public string? OrderAgentVersion { get; init; }

    public int ResponsesTimeoutSeconds { get; init; } = 60;

    public int ResponsesMaxBackoffSeconds { get; init; } = 8;
}
