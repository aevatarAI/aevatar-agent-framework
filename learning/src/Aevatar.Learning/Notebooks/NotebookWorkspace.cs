namespace Aevatar.Learning.Notebooks;

// ============================================================
//  NotebookWorkspace
//
//  约定：每个 notebook 对应一个真实目录，并在该目录下固定子目录结构：
//  - sources/       资料
//  - reports/       报告与版本
//  - encyclopedia/  专题百科条目
//  - cards/         学习卡片（SRS）
//  - quizzes/       测验记录与题库
//  - skills/        skills 输出
//
//  设计目标：路径拼装集中化，避免到处散落 "Path.Combine(...)"。
// ============================================================
public sealed class NotebookWorkspace
{
    public NotebookWorkspace(string notebookId, string rootDir, string notebookDir)
    {
        NotebookId = (notebookId ?? string.Empty).Trim();
        RootDir = (rootDir ?? string.Empty).Trim();
        NotebookDir = (notebookDir ?? string.Empty).Trim();

        if (NotebookId.Length == 0) throw new ArgumentException("notebookId is required.", nameof(notebookId));
        if (RootDir.Length == 0) throw new ArgumentException("rootDir is required.", nameof(rootDir));
        if (NotebookDir.Length == 0) throw new ArgumentException("notebookDir is required.", nameof(notebookDir));
    }

    public string NotebookId { get; }
    public string RootDir { get; }
    public string NotebookDir { get; }

    public string SourcesDir => Path.Combine(NotebookDir, "sources");
    public string ReportsDir => Path.Combine(NotebookDir, "reports");
    public string EncyclopediaDir => Path.Combine(NotebookDir, "encyclopedia");
    public string CardsDir => Path.Combine(NotebookDir, "cards");
    public string QuizzesDir => Path.Combine(NotebookDir, "quizzes");
    public string SkillsDir => Path.Combine(NotebookDir, "skills");

    public string MetaFilePath => Path.Combine(NotebookDir, "notebook.json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(NotebookDir);
        Directory.CreateDirectory(SourcesDir);
        Directory.CreateDirectory(ReportsDir);
        Directory.CreateDirectory(EncyclopediaDir);
        Directory.CreateDirectory(CardsDir);
        Directory.CreateDirectory(QuizzesDir);
        Directory.CreateDirectory(SkillsDir);
    }
}


