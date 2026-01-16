using System.Text;
using System.Text.Json;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services.DeviationImpact;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Services.Branches;

// ============================================================
//  RewriteBranchService (v1)
//
//  - Branch is file-first:
//    storyRoot/branches/<branchId>/chapters/*.txt
//  - Merge is explicit and author-driven:
//    copy a selected chapter file from branch back to main chapters/
// ============================================================

public sealed class RewriteBranchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ProjectRootManager _projectRoot;
    private readonly ILogger<RewriteBranchService> _logger;

    public RewriteBranchService(ProjectRootManager projectRoot, ILogger<RewriteBranchService> logger)
    {
        _projectRoot = projectRoot;
        _logger = logger;
    }

    public async Task<CreateRewriteBranchResponse> CreateAsync(CreateRewriteBranchRequest req, CancellationToken ct)
    {
        var root = _projectRoot.GetProjectRoot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new CreateRewriteBranchResponse
            {
                Labels = { ["error"] = "project_root_not_set" }
            };
        }

        var storyRel = (req.StoryRelativePath ?? "").Trim().Replace('\\', '/');
        if (!TryResolveUnderRoot(root, storyRel, out var storyRoot, out var err) || !Directory.Exists(storyRoot))
        {
            return new CreateRewriteBranchResponse
            {
                Labels = { ["error"] = err }
            };
        }

        var chaptersDir = Path.Combine(storyRoot, "chapters");
        if (!Directory.Exists(chaptersDir))
        {
            return new CreateRewriteBranchResponse
            {
                Labels = { ["error"] = "story_has_no_chapters_dir" }
            };
        }

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "branch";

        var basedOn = (req.BasedOnBranchId ?? "").Trim();
        var branchId = Guid.NewGuid().ToString("N");

        var branchRoot = Path.Combine(storyRoot, "branches", branchId);
        var branchChapters = Path.Combine(branchRoot, "chapters");
        Directory.CreateDirectory(branchChapters);

        // Copy chapters snapshot.
        foreach (var file in Directory.EnumerateFiles(chaptersDir, "*.txt", SearchOption.TopDirectoryOnly))
        {
            var dst = Path.Combine(branchChapters, Path.GetFileName(file));
            File.Copy(file, dst, overwrite: true);
        }

        var info = new RewriteBranchInfo
        {
            StoryRelativePath = storyRel,
            BranchId = branchId,
            Name = name,
            BasedOnBranchId = basedOn,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        var manifestPath = Path.Combine(branchRoot, "branch.md");
        await AtomicWriteAsync(manifestPath, BuildBranchManifestMarkdown(info), ct);
        await AtomicWriteAsync(Path.Combine(branchRoot, "branch.json"), JsonSerializer.Serialize(info, JsonOptions), ct);

        return new CreateRewriteBranchResponse
        {
            Branch = info,
            BranchManifest = new ArtifactRef
            {
                ArtifactId = branchId,
                Kind = ArtifactKind.Custom,
                Title = "Rewrite Branch Manifest",
                Uri = $"file://{manifestPath.Replace('\\', '/')}",
                Labels = { ["type"] = "rewrite_branch_manifest" }
            }
        };
    }

    public Task<ListRewriteBranchesResponse> ListAsync(ListRewriteBranchesRequest req, CancellationToken ct)
    {
        var root = _projectRoot.GetProjectRoot();
        var storyRel = (req.StoryRelativePath ?? "").Trim().Replace('\\', '/');

        var resp = new ListRewriteBranchesResponse { StoryRelativePath = storyRel };

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            resp.Labels["error"] = "project_root_not_set";
            return Task.FromResult(resp);
        }

        if (!TryResolveUnderRoot(root, storyRel, out var storyRoot, out var err) || !Directory.Exists(storyRoot))
        {
            resp.Labels["error"] = err;
            return Task.FromResult(resp);
        }

        var branchesDir = Path.Combine(storyRoot, "branches");
        if (!Directory.Exists(branchesDir))
            return Task.FromResult(resp);

        foreach (var dir in Directory.EnumerateDirectories(branchesDir, "*", SearchOption.TopDirectoryOnly))
        {
            var id = Path.GetFileName(dir);
            if (string.Equals(id, "_merges", StringComparison.OrdinalIgnoreCase))
                continue;

            var jsonPath = Path.Combine(dir, "branch.json");
            if (!File.Exists(jsonPath))
                continue;

            try
            {
                var json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var info = JsonSerializer.Deserialize<RewriteBranchInfo>(json, JsonOptions);
                if (info is null) continue;
                resp.Branches.Add(info);
            }
            catch
            {
                // best-effort
            }
        }

        return Task.FromResult(resp);
    }

    public async Task<DiffBranchChapterResponse> DiffChapterAsync(DiffBranchChapterRequest req, CancellationToken ct)
    {
        var root = _projectRoot.GetProjectRoot();
        var storyRel = (req.StoryRelativePath ?? "").Trim().Replace('\\', '/');
        var branchId = (req.BranchId ?? "").Trim();
        var chapterFile = (req.ChapterFile ?? "").Trim();

        var resp = new DiffBranchChapterResponse
        {
            StoryRelativePath = storyRel,
            BranchId = branchId,
            ChapterFile = chapterFile
        };

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            resp.Labels["error"] = "project_root_not_set";
            return resp;
        }

        if (!TryResolveUnderRoot(root, storyRel, out var storyRoot, out var err) || !Directory.Exists(storyRoot))
        {
            resp.Labels["error"] = err;
            return resp;
        }

        var mainPath = Path.Combine(storyRoot, "chapters", chapterFile);
        var branchPath = Path.Combine(storyRoot, "branches", branchId, "chapters", chapterFile);

        if (!File.Exists(mainPath) || !File.Exists(branchPath))
        {
            resp.Labels["error"] = "chapter_not_found";
            return resp;
        }

        var a = await File.ReadAllTextAsync(mainPath, Encoding.UTF8, ct);
        var b = await File.ReadAllTextAsync(branchPath, Encoding.UTF8, ct);

        var diff = BuildDiff(a, b);
        resp.Diff = new TextPayload
        {
            Format = TextFormat.Markdown,
            InlineText = "```diff\n" + diff + "\n```",
            Preview = diff.Length > 400 ? diff[..400] : diff
        };

        return resp;
    }

    public async Task<MergeBranchChapterResponse> MergeChapterAsync(MergeBranchChapterRequest req, CancellationToken ct)
    {
        var root = _projectRoot.GetProjectRoot();
        var storyRel = (req.StoryRelativePath ?? "").Trim().Replace('\\', '/');
        var branchId = (req.BranchId ?? "").Trim();
        var chapterFile = (req.ChapterFile ?? "").Trim();

        var resp = new MergeBranchChapterResponse();

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            resp.Success = false;
            resp.Message = "project_root_not_set";
            return resp;
        }

        if (!TryResolveUnderRoot(root, storyRel, out var storyRoot, out var err) || !Directory.Exists(storyRoot))
        {
            resp.Success = false;
            resp.Message = err;
            return resp;
        }

        var mainPath = Path.Combine(storyRoot, "chapters", chapterFile);
        var branchPath = Path.Combine(storyRoot, "branches", branchId, "chapters", chapterFile);
        if (!File.Exists(branchPath))
        {
            resp.Success = false;
            resp.Message = "branch_chapter_not_found";
            return resp;
        }

        var mergeId = Guid.NewGuid().ToString("N");
        var mergesDir = Path.Combine(storyRoot, "branches", "_merges");
        Directory.CreateDirectory(mergesDir);

        if (req.CreateBackup && File.Exists(mainPath))
        {
            var backupPath = Path.Combine(mergesDir, $"{mergeId}_backup_{chapterFile}");
            File.Copy(mainPath, backupPath, overwrite: true);
        }

        var content = await File.ReadAllTextAsync(branchPath, Encoding.UTF8, ct);
        await AtomicWriteAsync(mainPath, content, ct);

        var recordPath = Path.Combine(mergesDir, $"{mergeId}_merge.md");
        await AtomicWriteAsync(recordPath, BuildMergeRecordMarkdown(storyRel, branchId, chapterFile, mainPath, branchPath), ct);

        resp.Success = true;
        resp.Message = "ok";
        resp.MergeRecord = new ArtifactRef
        {
            ArtifactId = mergeId,
            Kind = ArtifactKind.Custom,
            Title = "Merge Record",
            Uri = $"file://{recordPath.Replace('\\', '/')}",
            Labels = { ["type"] = "rewrite_merge_record" }
        };

        return resp;
    }

    private static string BuildDiff(string baseText, string editedText)
    {
        var a = (baseText ?? "").Replace("\r\n", "\n").Split('\n');
        var b = (editedText ?? "").Replace("\r\n", "\n").Split('\n');
        var ops = MyersDiff.DiffLines(a, b);

        var sb = new StringBuilder();
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case MyersDiff.OpKind.Equal:
                    sb.AppendLine(" " + op.Text);
                    break;
                case MyersDiff.OpKind.Delete:
                    sb.AppendLine("-" + op.Text);
                    break;
                case MyersDiff.OpKind.Insert:
                    sb.AppendLine("+" + op.Text);
                    break;
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildBranchManifestMarkdown(RewriteBranchInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Rewrite Branch");
        sb.AppendLine();
        sb.AppendLine($"- **branch_id**: `{info.BranchId}`");
        sb.AppendLine($"- **name**: `{info.Name}`");
        sb.AppendLine($"- **story_relative_path**: `{info.StoryRelativePath}`");
        sb.AppendLine($"- **based_on_branch_id**: `{info.BasedOnBranchId}`");
        sb.AppendLine($"- **created_at**: `{info.CreatedAt.ToDateTime():O}`");
        sb.AppendLine();
        sb.AppendLine("## How to use");
        sb.AppendLine();
        sb.AppendLine("- Edit branch chapters under `branches/<branch_id>/chapters/*.txt`.");
        sb.AppendLine("- Compare with main using diff tool (UI) or `/api/novel/branches/diff`.");
        sb.AppendLine("- Merge selected chapters back to main explicitly (author-driven).");
        return sb.ToString();
    }

    private static string BuildMergeRecordMarkdown(
        string storyRel,
        string branchId,
        string chapterFile,
        string mainPath,
        string branchPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Merge Record");
        sb.AppendLine();
        sb.AppendLine($"- **story_relative_path**: `{storyRel}`");
        sb.AppendLine($"- **branch_id**: `{branchId}`");
        sb.AppendLine($"- **chapter_file**: `{chapterFile}`");
        sb.AppendLine($"- **merged_at**: `{DateTime.UtcNow:O}`");
        sb.AppendLine();
        sb.AppendLine("## Paths");
        sb.AppendLine();
        sb.AppendLine($"- **branch_source**: `file://{branchPath.Replace('\\', '/')}`");
        sb.AppendLine($"- **main_target**: `file://{mainPath.Replace('\\', '/')}`");
        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- This merge is a file copy (branch → main).");
        sb.AppendLine("- If you want selective merge inside the chapter, use the editor + diff view.");
        return sb.ToString();
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8, ct);
        File.Move(tmp, path, overwrite: true);
    }

    private static bool TryResolveUnderRoot(string root, string relativePath, out string fullPath, out string error)
    {
        fullPath = "";
        error = "";

        try
        {
            var rootFull = Path.GetFullPath(root.Trim());
            var rel = (relativePath ?? "").Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel))
            {
                error = "relative_path_required";
                return false;
            }

            var combined = Path.GetFullPath(Path.Combine(rootFull, rel));
            var rootPrefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "path_outside_project_root";
                return false;
            }

            fullPath = combined;
            return true;
        }
        catch (Exception ex)
        {
            error = "path_resolution_failed:" + ex.Message;
            return false;
        }
    }
}


