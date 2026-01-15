using Aevatar.Agents.Abstractions.Memory;

namespace Aevatar.Notebook.Reports;

// ============================================================
//  Reports API (Query endpoints)
//
//  Endpoints:
//  - GET /api/reports            : list report resources (memoryId prefix report::)
//  - GET /api/reports/{reportId} : list recent entries for a report resource (debug-grade)
//
//  Notes:
//  - Report content is stored as MemoryEntry under memoryId "report::<reportId>".
// ============================================================
internal static class ReportApi
{
    public static void MapReportApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapList(app);
        MapGet(app);
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/reports", async (IMemoryStore store, CancellationToken ct) =>
        {
            var list = await store.ListResourcesAsync(scopeTypeFilter: MemoryScopeType.Graph, limit: 200, ct: ct);

            var reports = list
                .Where(r => (r.MemoryId ?? string.Empty).StartsWith("report::", StringComparison.Ordinal))
                .Select(r => new
                {
                    reportId = (r.MemoryId ?? string.Empty).Replace("report::", "", StringComparison.Ordinal),
                    memoryId = r.MemoryId,
                    entryCount = r.EntryCount,
                    latestAt = r.LatestAt?.ToDateTime().ToString("O") ?? ""
                })
                .OrderBy(r => r.reportId, StringComparer.Ordinal)
                .ToList();

            return Results.Json(new { count = reports.Count, reports });
        });
    }

    private static void MapGet(WebApplication app)
    {
        app.MapGet("/api/reports/{reportId}", async (
            string reportId,
            int? limit,
            IMemoryStore store,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(reportId))
                return Results.BadRequest(new { error = "reportId is required" });

            var memoryId = $"report::{reportId.Trim()}";
            var take = Math.Clamp(limit ?? 200, 1, 2000);
            var entries = await store.ListEntriesAsync(memoryId, limit: take, ct: ct);

            // Best-effort sort by version desc when tag exists.
            var sorted = entries
                .OrderByDescending(e =>
                {
                    if (e?.Tags == null) return 0;
                    return e.Tags.TryGetValue("version", out var v) && int.TryParse(v, out var i) ? i : 0;
                })
                .ToList();

            return Results.Json(new { reportId = reportId.Trim(), memoryId, count = sorted.Count, entries = sorted });
        });
    }
}


