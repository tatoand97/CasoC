# CasoC

`CasoC` es un repositorio de bootstrap / IaC logico puro para un Caso C A2A real en Azure AI Foundry sobre .NET 8.

Este repo no consume prompts de negocio, no ejecuta workflow runtime, no hace fan-out desde backend y no expone API. Su unica responsabilidad es validar configuracion y reconciliar los agentes que pertenecen al caso.

## Target Architecture

```text
User
  |
  v
PlannerAgent
  |- A2A -> OrderAgent
  '- A2A -> PolicyAgent
```

- `OrderAgent` es externo y lo crea otro repo o proceso.
- `PolicyAgent` se crea o reconcilia aqui.
- `PlannerAgent` se crea o reconcilia aqui.
- `PlannerAgent` es el unico orquestador del caso y queda configurado con exactamente dos `A2APreviewTool`.
- `PlannerAgent` no usa MCP directo.
- `OrderAgent` sigue siendo el unico agente que puede usar MCP dentro de su propio caso, fuera de este repo.

## Responsibilities

- Cargar `CasoCA2ASettings`.
- Validar `CasoC:ProjectEndpoint` como endpoint de proyecto Foundry.
- Validar el deployment configurado en `CasoC:ModelDeploymentName`.
- Validar acceso al proyecto Foundry.
- Validar `CasoC:OrderAgentId` como referencia a un `OrderAgent` externo existente.
- Resolver y validar `CasoC:OrderA2AConnectionName`.
- Resolver y validar `CasoC:PolicyA2AConnectionName`.
- Crear o reconciliar `PolicyAgent`.
- Crear o reconciliar `PlannerAgent`.
- Imprimir un resumen final del bootstrap y terminar.

## Prerequisites

- Un Azure AI Foundry project accesible por endpoint de proyecto.
- Un deployment de modelo existente en ese mismo proyecto.
- Un `OrderAgent` existente en ese mismo proyecto, creado fuera de este repo.
- Una A2A connection ya creada para `OrderAgent`.
- Una A2A connection creada para `PolicyAgent` antes de completar la reconciliacion de `PlannerAgent`.
- Credenciales validas para `DefaultAzureCredential`.

Este repo no intenta crear conexiones A2A desde C#. Si una connection no existe, el bootstrap falla con un mensaje claro para que se cree en Foundry y luego se reintente.

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

- `ProjectEndpoint` es requerido.
- `ModelDeploymentName` es requerido.
- `OrderAgentId` es requerido.
- `PolicyAgentName` es requerido.
- `PlannerAgentName` es requerido.
- `OrderA2AConnectionName` es requerido.
- `PolicyA2AConnectionName` es requerido.
- `OrderA2ABaseUri` y `PolicyA2ABaseUri` son opcionales.
- `OrderA2AConnectionName` y `PolicyA2AConnectionName` deben resolver a conexiones distintas.
- Si una connection no es `RemoteA2A`, el bootstrap exige la base URI absoluta correspondiente.

## Bootstrap Flow

La aplicacion hace exactamente esto:

1. Carga `CasoCA2ASettings`.
2. Valida el endpoint de proyecto Foundry.
3. Valida el deployment configurado.
4. Valida acceso al proyecto.
5. Valida `OrderAgent` sin crearlo.
6. Crea o reconcilia `PolicyAgent`.
7. Valida las dos conexiones A2A por nombre e imprime `name`, `id` y `type`.
8. Crea o reconcilia `PlannerAgent` con exactamente dos `A2APreviewTool`.
9. Imprime el resumen final y termina.

## Agent Model

- `OrderAgent` sigue siendo externo y solo se valida.
- `PolicyAgent` es un prompt agent normal, sin MCP, sin A2A y sin tools externas.
- `PlannerAgent` es el unico orquestador del caso.
- `PlannerAgent` usa exactamente dos `A2APreviewTool`: uno hacia `OrderAgent` y otro hacia `PolicyAgent`.
- `PlannerAgent` no usa MCP directo.
- `PlannerAgent` no usa tools tipo `agent`.
- El consumidor del caso debe invocar solamente a `PlannerAgent`.

## Execution

```powershell
dotnet run
```

La ejecucion es solo de bootstrap. Este repo no acepta prompts de negocio, no ejecuta responses finales de negocio y no invoca runtime funcional de `OrderAgent` ni de `PolicyAgent`.

## Expected Console Shape

```text
[CONFIG] Endpoint validated
[CONFIG] Deployment validated
[VALIDATION] Project access validated
[VALIDATION] OrderAgent validated
[RECONCILE] policy-agent-casec-a2a => created|updated|unchanged
[VALIDATION] Order A2A connection validated => name: <order-connection>, id: <order-connection-id>, type: <order-connection-type>
[VALIDATION] Policy A2A connection validated => name: <policy-connection>, id: <policy-connection-id>, type: <policy-connection-type>
[RECONCILE] planner-agent-casec-a2a => created|updated|unchanged
[SUMMARY] OrderAgent => id: <order-agent-id>, name: <order-agent-name>, version: <order-agent-version>
[SUMMARY] PolicyAgent => id: <policy-agent-id>, name: <policy-agent-name>, version: <policy-agent-version>
[SUMMARY] PlannerAgent => id: <planner-agent-id>, name: <planner-agent-name>, version: <planner-agent-version>
[SUMMARY] Order A2A connection => name: <order-connection>, id: <order-connection-id>, type: <order-connection-type>
[SUMMARY] Policy A2A connection => name: <policy-connection>, id: <policy-connection-id>, type: <policy-connection-type>
[SUMMARY] Model deployment => <deployment-name>
[SUMMARY] PlannerAgent bootstrap for A2A completed
```

## Out of Scope

- Crear `OrderAgent`.
- Crear conexiones A2A desde C#.
- Workflow secuencial o fan-out backend.
- MCP directo desde `PlannerAgent`.
- Tools tipo `agent`.
- Runtime funcional de negocio.
- Prompt consumption.
- API HTTP.
