using System.Text.Json;

namespace Aevatar.Learning.Notebooks;

// ============================================================
//  NotebookDirectoryStore (MVP)
//
//  Responsibilities:
//  - Create/List/Get notebooks backed by real directories.
//  - Root directory selection:
//      1) explicit rootPath (API request)
//      2) env LEARNING_NOTEBOOK_ROOT
//      3) default: ~/AevatarLearning
//
//  NOTE:
//  - This is a local-only implementation (no DB).
//  - The folder naming rule embeds notebookId to allow fast lookup.
// ============================================================
public sealed class NotebookDirectoryStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public NotebookInfo CreateNotebook(string displayName, string? rootPath = null)
    {
        displayName = (displayName ?? string.Empty).Trim();
        if (displayName.Length == 0)
            throw new ArgumentException("displayName is required.", nameof(displayName));

        var rootDir = ResolveRootDir(rootPath);
        Directory.CreateDirectory(rootDir);

        var notebookId = Guid.NewGuid().ToString("N")[..8];
        var folderSlug = Slugify(displayName);
        var folderName = $"{folderSlug}-{notebookId}";
        var notebookDir = Path.Combine(rootDir, folderName);

        var workspace = new NotebookWorkspace(notebookId, rootDir, notebookDir);
        workspace.EnsureDirectories();

        var now = DateTimeOffset.UtcNow;
        var meta = new NotebookMeta
        {
            NotebookId = notebookId,
            DisplayName = displayName,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };

        WriteMeta(workspace.MetaFilePath, meta);

        return new NotebookInfo(notebookId, displayName, notebookDir, now, now);
    }

    public IReadOnlyList<NotebookInfo> ListNotebooks(string? rootPath = null)
    {
        var rootDir = ResolveRootDir(rootPath);
        if (!Directory.Exists(rootDir))
            return Array.Empty<NotebookInfo>();

        var result = new List<NotebookInfo>(capacity: 32);
        foreach (var dir in Directory.EnumerateDirectories(rootDir))
        {
            var info = TryReadNotebookInfoFromDirectory(dir);
            if (info != null)
                result.Add(info);
        }

        return result
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public NotebookInfo? GetNotebook(string notebookId, string? rootPath = null)
    {
        notebookId = (notebookId ?? string.Empty).Trim();
        if (notebookId.Length == 0)
            return null;

        var rootDir = ResolveRootDir(rootPath);
        if (!Directory.Exists(rootDir))
            return null;

        // Fast path: directory name contains "-{notebookId}" suffix.
        var suffix = "-" + notebookId;
        foreach (var dir in Directory.EnumerateDirectories(rootDir))
        {
            var name = Path.GetFileName(dir) ?? string.Empty;
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            var info = TryReadNotebookInfoFromDirectory(dir);
            if (info != null && string.Equals(info.NotebookId, notebookId, StringComparison.OrdinalIgnoreCase))
                return info;
        }

        return null;
    }

    // ============================================================
    //  Internals
    // ============================================================

    private static NotebookInfo? TryReadNotebookInfoFromDirectory(string notebookDir)
    {
        try
        {
            var metaPath = Path.Combine(notebookDir, "notebook.json");
            if (!File.Exists(metaPath))
                return null;

            var meta = ReadMeta(metaPath);
            if (meta == null || string.IsNullOrWhiteSpace(meta.NotebookId))
                return null;

            return new NotebookInfo(
                meta.NotebookId.Trim(),
                (meta.DisplayName ?? string.Empty).Trim(),
                notebookDir,
                meta.CreatedAt,
                meta.UpdatedAt);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveRootDir(string? explicitRoot)
    {
        var root = (explicitRoot ?? string.Empty).Trim();
        if (root.Length == 0)
            root = (Environment.GetEnvironmentVariable("LEARNING_NOTEBOOK_ROOT") ?? string.Empty).Trim();

        if (root.Length == 0)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = Path.Combine(home, "AevatarLearning");
        }

        return Path.GetFullPath(root);
    }

    private static string Slugify(string input)
    {
        input = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (input.Length == 0) return "notebook";

        var sb = new System.Text.StringBuilder(capacity: Math.Min(48, input.Length));
        var lastDash = false;

        foreach (var ch in input)
        {
            var isAlnum = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9');
            if (isAlnum)
            {
                sb.Append(ch);
                lastDash = false;
                continue;
            }

            // treat whitespace/_ as '-'
            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-')
            {
                if (!lastDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0) slug = "notebook";
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        return slug.Length == 0 ? "notebook" : slug;
    }

    private static void WriteMeta(string path, NotebookMeta meta)
    {
        var json = JsonSerializer.Serialize(meta, Json);
        File.WriteAllText(path, json);
    }

    private static NotebookMeta? ReadMeta(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<NotebookMeta>(json, Json);
    }

    private sealed class NotebookMeta
    {
        public string NotebookId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int Version { get; set; }
    }
}

public sealed record NotebookInfo(
    string NotebookId,
    string DisplayName,
    string DirectoryPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);


