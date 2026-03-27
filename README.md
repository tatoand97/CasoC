# CasoC

`CasoC` es un repositorio de bootstrap / IaC logico puro para un Caso C A2A real en Azure AI Foundry sobre .NET 8.

Este repo solo valida y reconcilia recursos. `OrderAgent` es externo y ya existe. `PolicyAgent` y `PlannerAgent` se crean o reconcilian aqui. `PlannerAgent` queda configurado con exactamente dos `A2APreviewTool`: uno hacia `OrderAgent` y otro hacia `PolicyAgent`.

El repo no consume prompts de negocio, no ejecuta runtime funcional, no hace fan-out desde backend y no expone API.

## Target Architecture

```text
User
  |
  v
PlannerAgent
  |- A2A -> OrderAgent
  '- A2A -> PolicyAgent
```

`OrderAgent` puede implementar su propia integracion interna, pero eso queda fuera de este repositorio.

## Responsibilities

- Validar `CasoC:ProjectEndpoint`
- Validar el deployment configurado en `CasoC:ModelDeploymentName`
- Validar acceso al proyecto Foundry
- Validar `CasoC:OrderAgentId` como referencia a un `OrderAgent` externo existente
- Crear o reconciliar `PolicyAgent`
- Validar `CasoC:OrderA2AConnectionName`
- Validar `CasoC:PolicyA2AConnectionName`
- Crear o reconciliar `PlannerAgent` con exactamente dos `A2APreviewTool`
- Imprimir ids, nombres, versiones y conexiones al finalizar

## Prerequisites

- Un Azure AI Foundry project accesible por endpoint de proyecto
- Un deployment de modelo existente en ese mismo proyecto
- Un `OrderAgent` existente en ese mismo proyecto, creado por otro repo o proceso
- Una A2A connection ya creada para `OrderAgent`
- Credenciales validas para `DefaultAzureCredential`

Antes de completar el bootstrap de `PlannerAgent`, tambien debe existir una A2A connection para `PolicyAgent`. Si esa connection depende de crear primero el agente, ver la secuencia operativa mas abajo.

## Configuration

`appsettings.json` debe contener valores alineados con la clase `CasoCA2ASettings`:

```json
{
  "CasoC": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "OrderAgentId": "<existing-order-agent-id-or-version-id>",
    "PolicyAgentName": "policy-agent-casec-a2a",
    "PlannerAgentName": "planner-agent-casec-a2a",
    "OrderA2AConnectionName": "<order-a2a-connection-name>",
    "PolicyA2AConnectionName": "<policy-a2a-connection-name>",
    "OrderA2ABaseUri": "",
    "PolicyA2ABaseUri": ""
  }
}
```

Reglas:

- `ProjectEndpoint` es requerido
- `ModelDeploymentName` es requerido
- `OrderAgentId` es requerido
- `PolicyAgentName` es requerido
- `PlannerAgentName` es requerido
- `OrderA2AConnectionName` es requerido
- `PolicyA2AConnectionName` es requerido
- `OrderA2ABaseUri` y `PolicyA2ABaseUri` son opcionales
- `OrderA2AConnectionName` y `PolicyA2AConnectionName` deben resolver a dos conexiones distintas
- Si una connection no es `RemoteA2A`, se debe informar su base URI manualmente en el setting correspondiente

## Bootstrap Flow

La aplicacion hace exactamente esto:

1. Carga settings.
2. Valida el endpoint Foundry.
3. Valida el deployment configurado.
4. Valida acceso al proyecto.
5. Resuelve y valida `OrderAgent` sin crearlo.
6. Crea o reconcilia `PolicyAgent`.
7. Resuelve y valida las dos conexiones A2A por nombre.
8. Crea o reconcilia `PlannerAgent` con dos A2A tools.
9. Imprime el resumen final y termina.

## Agent Model

- `OrderAgent` sigue siendo externo y solo se valida
- `PolicyAgent` es un prompt agent normal, sin MCP, sin A2A y sin tools externas
- `PlannerAgent` es el unico agente orquestador del caso
- `PlannerAgent` usa A2A tools
- `PlannerAgent` no usa workflow
- `PlannerAgent` no usa MCP directo
- `PlannerAgent` no usa tools tipo `agent`
- El consumidor del caso debe invocar solamente a `PlannerAgent`

## Operational Sequence

Si la A2A connection de `PolicyAgent` requiere crear primero el agente y luego registrar manualmente la connection en Foundry, la secuencia correcta es:

1. Ejecutar `dotnet run` para crear o reconciliar `PolicyAgent`.
2. Crear manualmente la A2A connection de `PolicyAgent` en Foundry portal.
3. Reejecutar `dotnet run` para validar esa connection y reconciliar `PlannerAgent`.

El programa falla con un mensaje claro si la connection no existe o si falta `PolicyA2ABaseUri` para una connection que no sea `RemoteA2A`.

## Execution

```powershell
dotnet run
```

La ejecucion es solo de bootstrap. Este repo no acepta prompts de negocio, no consume prompts de prueba y no invoca runtime funcional de `OrderAgent` ni de `PolicyAgent`.

## Expected Console Shape

```text
[CONFIG] Endpoint validated => https://<resource>.services.ai.azure.com/api/projects/<project>
[CONFIG] Deployment validated => <deployment-name>
[VALIDATION] Project access validated
[VALIDATION] OrderAgent validated => id: <order-agent-id>, name: <order-agent-name>, version: <order-agent-version>
[RECONCILE] policy-agent-casec-a2a => created|updated|unchanged
[VALIDATION] Order A2A connection validated => name: <order-connection>, id: <order-connection-id>, type: <order-connection-type>
[VALIDATION] Policy A2A connection validated => name: <policy-connection>, id: <policy-connection-id>, type: <policy-connection-type>
[RECONCILE] planner-agent-casec-a2a => created|updated|unchanged
[SUMMARY] OrderAgent => id: <order-agent-id>, name: <order-agent-name>, version: <order-agent-version>
[SUMMARY] PolicyAgent => id: <policy-agent-id>, name: <policy-agent-name>, version: <policy-agent-version>
[SUMMARY] PlannerAgent => id: <planner-agent-id>, name: <planner-agent-name>, version: <planner-agent-version>
[SUMMARY] Order A2A connection => name: <order-connection>, id: <order-connection-id>, type: <order-connection-type>
[SUMMARY] Policy A2A connection => name: <policy-connection>, id: <policy-connection-id>, type: <policy-connection-type>
[SUMMARY] PlannerAgent bootstrap for A2A completed
```

## Out of Scope

- Workflow orchestration
- Backend fan-out
- Direct MCP from `PlannerAgent`
- Tools tipo `agent`
- Runtime funcional de negocio
- Prompt consumption
- API HTTP
