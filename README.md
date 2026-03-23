# Caso C - Orquestacion de Agentes (.NET 8, Responses API)

Console App de ejemplo para Azure AI Foundry que implementa:
- Reutilizar `OrderAgent` existente con `OrderAgentId` (no se recrea).
- Crear `PolicyAgent` dinamicamente.
- Crear `PlannerAgent` dinamicamente para consolidar la respuesta final.
- Orquestar desde la aplicacion el flujo `OrderAgent -> PolicyAgent -> PlannerAgent`.
- Ejecutar prompts con polling hasta estado terminal y mostrar la respuesta final.

## Requisitos

- .NET SDK 8+
- Endpoint de **Azure AI Foundry Project** (no AOAI clasico)
- Permisos para crear agentes en el proyecto

## Configuracion

Crea o ajusta `appsettings.json` en la raiz del proyecto:

```json
{
  "CasoC": {
    "AzureOpenAiEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureOpenAiDeployment": "<deployment-name>",
    "OrderAgentId": "<existing-order-agent-id>",
    "ResponsesTimeoutSeconds": 60,
    "ResponsesMaxBackoffSeconds": 8
  }
}
```

## Ejecutar

```powershell
dotnet run
```

## Flujo de prueba incluido

El programa envia automaticamente:

```text
Dame el estado de la orden ORD-000001 y dime si requiere accion.
```

## Salida esperada (ejemplo)

```text
Inicializando clientes...
OrderAgentId validado: OrderAgent
PolicyAgent reconciliation:
  AgentName: policy-agent-casec
  AgentId: policy-agent-casec:1
  AgentVersion: 1
  ReconciliationStatus: created
PlannerAgent reconciliation:
  AgentName: planner-agent-casec-orchestrated
  AgentId: planner-agent-casec-orchestrated:1
  AgentVersion: 1
  ReconciliationStatus: created

===== RESPUESTA FINAL DEL PLANNER =====
La orden ORD-000001 esta en estado "En revision" y si requiere accion: validar el pago pendiente antes del despacho.
=======================================
```

## Notas

- No usa `Azure.AI.Agents.Persistent` ni `PersistentAgentsClient`.
- No usa OpenAPI tools.
- No depende de tools tipo `agent`, ya que el servicio actual no acepta ese tipo en la API de responses.
- La app lee configuracion unicamente desde `appsettings.json`; no usa variables de entorno para estos valores.
