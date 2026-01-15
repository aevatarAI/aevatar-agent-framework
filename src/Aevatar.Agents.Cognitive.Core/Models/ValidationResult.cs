namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  VALIDATION RESULT
//  选项验证结果
// ============================================================

/// <summary>
/// 验证结果。
/// </summary>
public sealed record ValidationResult
{
    /// <summary>
    /// 是否有效。
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// 错误列表。
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// 警告列表。
    /// </summary>
    public IReadOnlyList<ValidationWarning> Warnings { get; init; } = [];

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static ValidationResult Success() => new();

    /// <summary>
    /// 创建带警告的成功结果。
    /// </summary>
    public static ValidationResult WithWarnings(params ValidationWarning[] warnings) => new()
    {
        Warnings = warnings
    };

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static ValidationResult Failed(params ValidationError[] errors) => new()
    {
        Errors = errors
    };

    /// <summary>
    /// 创建失败结果（单个错误）。
    /// </summary>
    public static ValidationResult Failed(string field, string message) =>
        Failed(new ValidationError(field, message));
}

/// <summary>
/// 验证错误。
/// </summary>
public sealed record ValidationError(string Field, string Message);

/// <summary>
/// 验证警告。
/// </summary>
public sealed record ValidationWarning(string Field, string Message);

