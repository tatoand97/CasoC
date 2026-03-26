# Caso C - Orquestacion multi-agente desde la aplicacion (.NET 8, Responses API)

Console App de ejemplo para Azure AI Foundry que implementa un flujo multi-agente orquestado por la aplicacion .NET, no una delegacion agent-to-agent dentro de Foundry.

## Arquitectura implementada

- La aplicacion coordina el flujo `OrderAgent -> PolicyAgent -> PlannerAgent`.
- `OrderAgent` se reutiliza desde `OrderAgentId` y puede usar internamente el MCP configurado en Caso B.
- `PolicyAgent` y `PlannerAgent` se crean dinamicamente mediante reconciliacion.
- La aplicacion valida salidas estructuradas entre pasos antes de continuar.
- `PlannerAgent` no delega a otros agentes ni usa agent tools.

```text
User
  |
App Orchestrator (.NET 8)
  |-- OrderAgent -> MCP -> APIM -> API REST
  |-- PolicyAgent
  '-- PlannerAgent
```

## Roles de los agentes

- `OrderAgent`: recupera solo datos estructurados de la orden y debe devolver JSON valido.
- `PolicyAgent`: decide si se requiere accion adicional a partir del JSON validado de la orden y devuelve JSON compacto.
- `PlannerAgent`: redacta la respuesta final para el usuario usando solo la solicitud original y el contexto validado.

## Contratos JSON validados por la aplicacion

`OrderAgent` debe devolver:

```json
{
  "id": "ORD-000001",
  "status": "Created",
  "requiresAction": true,
  "reason": "optional string"
}
```

Estados admitidos:

```text
Created, Confirmed, Packed, Shipped, Delivered, Cancelled, Unknown, NotFound
```

Si la orden no existe, el agente debe devolver JSON valido, por ejemplo:

```json
{
  "id": "ORD-000001",
  "status": "NotFound",
  "requiresAction": false,
  "reason": "Order not found"
}
```

`PolicyAgent` debe devolver:

```json
{
  "requiresAction": true,
  "message": "short explanation"
}
```

Si alguno de estos contratos falla, la aplicacion lanza `InvalidOperationException` antes de invocar al siguiente agente.

## Requisitos

- .NET SDK 8+
- Endpoint de Azure AI Foundry Project
- Permisos para listar agentes y crear versiones de agentes en el proyecto
- Credenciales validas para `AzureCliCredential`

## Configuracion

Ajusta `appsettings.json` en la raiz del proyecto:

```json
{
  "CasoC": {
    "AzureOpenAiEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureOpenAiDeployment": "<deployment-name>",
    "OrderAgentId": "<existing-order-agent-id>",
    "OrderAgentVersion": "latest",
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

La salida en consola muestra:

- la reconciliacion de `PolicyAgent` y `PlannerAgent`
- el JSON validado de la orden
- el resultado validado de politica
- la respuesta final generada por `PlannerAgent`

Ejemplo resumido:

```text
Inicializando clientes...
OrderAgentId validado: OrderAgent
PolicyAgent reconciliation:
  AgentName: policy-agent-casec
  AgentId: policy-agent-casec:2
  AgentVersion: 2
  ReconciliationStatus: updated
PlannerAgent reconciliation:
  AgentName: planner-agent-casec-composer
  AgentId: planner-agent-casec-composer:1
  AgentVersion: 1
  ReconciliationStatus: created

===== ORDER PAYLOAD VALIDADO =====
{
  "id": "ORD-000001",
  "status": "Confirmed",
  "requiresAction": false
}
===============================

===== POLICY RESULT VALIDADO =====
{
  "requiresAction": false,
  "message": "La orden no requiere accion adicional."
}
===============================

===== RESPUESTA FINAL =====
La orden ORD-000001 esta confirmada y no requiere accion adicional en este momento.
===========================
```

## Notas

- Este caso no implementa delegacion agent-to-agent dentro de Foundry.
- La aplicacion es quien orquesta el workflow completo.
- Se conserva el comportamiento existente de reconciliacion, polling y backoff para Responses API.
- No se documenta ningun uso de agent tools porque no forman parte de la ruta real de ejecucion de este ejemplo.
