using Aevatar.Novel.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Services;

// ============================================================
//  ProjectRootManager
//
//  - Holds the current SSOT ProjectRoot for the sidecar runtime.
//  - Emits ProjectRootChangedEvent into SidecarEventHub when updated.
//
//  NOTE:
//  - This is local-only runtime state (not persisted yet).
// ============================================================

public sealed class ProjectRootManager
{
    private readonly object _lock = new();
    private readonly SidecarEventHub _eventHub;

    private string _projectRoot = string.Empty;

    public ProjectRootManager(SidecarEventHub eventHub, IConfiguration configuration)
    {
        _eventHub = eventHub;

        // Seed from config (optional).
        _projectRoot = configuration.GetSection("Novel").GetValue<string>("ProjectRoot") ?? string.Empty;
    }

    public string GetProjectRoot()
    {
        lock (_lock)
        {
            return _projectRoot;
        }
    }

    public ProjectRootInfo GetInfo()
    {
        var root = GetProjectRoot();
        return new ProjectRootInfo
        {
            ProjectRoot = root,
            Exists = !string.IsNullOrWhiteSpace(root) && Directory.Exists(root),
            ObservedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    public ProjectRootInfo SetProjectRoot(string? projectRoot)
    {
        projectRoot ??= string.Empty;
        projectRoot = projectRoot.Trim();

        string previous;
        lock (_lock)
        {
            previous = _projectRoot;
            _projectRoot = projectRoot;
        }

        // Publish change event only when it actually changes.
        if (!string.Equals(previous, projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            var info = GetInfo();
            _eventHub.Publish(new SidecarEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProjectRootChanged = new ProjectRootChangedEvent
                {
                    EventId = Guid.NewGuid().ToString("N"),
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                    Info = info
                }
            });
        }

        return GetInfo();
    }
}


