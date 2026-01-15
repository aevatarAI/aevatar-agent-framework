using System.Text.Json;
using Microsoft.AspNetCore.Builder;

namespace VibeResearching.Api.Sessions;

// ============================================================
//  Scientific Research Assistant Sessions API (AG-UI)
//
//  Endpoints:
//  - GET  /health
//  - GET  /api/info
//  - POST /api/sessions
//  - GET  /api/sessions
//  - POST /api/sessions/{id}/input
//  - GET  /api/sessions/{id}/agui/events   (SSE, snapshot-first)
// ============================================================

internal static partial class ResearchSessionsApi
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapResearchSessionsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreate(app);
        MapList(app);
        MapTools(app);
        MapAgentProviders(app);
        MapDeliverables(app);
        MapCompute(app);
        MapUploads(app);
        MapDag(app);
        MapMesh(app);
        MapStatus(app);
        MapInput(app);
        MapMcpReconnect(app);
        MapFacts(app);
        MapWorkspace(app);
        MapFiles(app);
        MapAgUiEvents(app);
    }

    // (Workspace projection helpers are centralized in WorkspaceProjection.)
}
