namespace VibeResearching.Vibe.Pivot;

/// <summary>
/// Provides localized messages for pivot feedback (Chinese primary, English fallback).
/// </summary>
public static class PivotMessages
{
    // Detection acknowledgment
    public const string DetectionAcknowledgment_ZH = "检测到您想调整研究方向...";
    public const string DetectionAcknowledgment_EN = "Detected a potential research direction change...";

    // Clarification prompts
    public const string ClarificationPrompt_ZH = "您是想更换研究方向吗？";
    public const string ClarificationPrompt_EN = "Would you like to change your research direction?";

    public const string ClarificationWithTopic_ZH = "您是想将研究方向从「{0}」转向「{1}」吗？";
    public const string ClarificationWithTopic_EN = "Would you like to change your research direction from '{0}' to '{1}'?";

    // Progress updates
    public const string ProgressCancellingPlans_ZH = "正在取消 {0} 个待执行计划...";
    public const string ProgressCancellingPlans_EN = "Cancelling {0} pending plan(s)...";

    public const string ProgressPreservingKnowledge_ZH = "正在保留 {0} 个已完成的知识节点...";
    public const string ProgressPreservingKnowledge_EN = "Preserving {0} completed knowledge node(s)...";

    public const string ProgressNotifyingAgents_ZH = "正在通知所有子代理更新研究方向...";
    public const string ProgressNotifyingAgents_EN = "Notifying all subagents of direction change...";

    public const string ProgressUpdatingDag_ZH = "正在更新知识图谱...";
    public const string ProgressUpdatingDag_EN = "Updating knowledge graph...";

    // Completion messages
    public const string CompletionSuccess_ZH = "研究方向已更新为：{0}";
    public const string CompletionSuccess_EN = "Research direction updated to: {0}";

    public const string CompletionSummary_ZH = "方向变更完成：取消了 {0} 个计划，保留了 {1} 个知识节点";
    public const string CompletionSummary_EN = "Direction change complete: cancelled {0} plan(s), preserved {1} knowledge node(s)";

    public const string CompletionWithPreserve_ZH = "研究方向已更新。已保留与「{0}」相关的内容。";
    public const string CompletionWithPreserve_EN = "Research direction updated. Content related to '{0}' has been preserved.";

    // Error messages
    public const string ErrorPivotFailed_ZH = "方向变更失败：{0}";
    public const string ErrorPivotFailed_EN = "Direction change failed: {0}";

    public const string ErrorTimeout_ZH = "部分代理响应超时，但方向变更已完成";
    public const string ErrorTimeout_EN = "Some agents timed out, but direction change completed";

    // Rollback messages
    public const string RollbackAvailable_ZH = "如需撤销此变更，请在 {0} 分钟内说「撤销方向变更」";
    public const string RollbackAvailable_EN = "To undo this change, say 'undo direction change' within {0} minutes";

    public const string RollbackSuccess_ZH = "已成功撤销方向变更，恢复到「{0}」";
    public const string RollbackSuccess_EN = "Successfully rolled back to '{0}'";

    public const string RollbackExpired_ZH = "撤销窗口已过期，无法回滚此方向变更";
    public const string RollbackExpired_EN = "Rollback window expired, cannot undo this direction change";

    /// <summary>
    /// Gets the localized detection acknowledgment message.
    /// </summary>
    public static string GetDetectionAcknowledgment(bool useChinese = true) =>
        useChinese ? DetectionAcknowledgment_ZH : DetectionAcknowledgment_EN;

    /// <summary>
    /// Gets the localized clarification prompt.
    /// </summary>
    public static string GetClarificationPrompt(bool useChinese = true) =>
        useChinese ? ClarificationPrompt_ZH : ClarificationPrompt_EN;

    /// <summary>
    /// Gets the localized clarification prompt with topics.
    /// </summary>
    public static string GetClarificationWithTopic(string oldTopic, string newTopic, bool useChinese = true) =>
        string.Format(useChinese ? ClarificationWithTopic_ZH : ClarificationWithTopic_EN, oldTopic, newTopic);

    /// <summary>
    /// Gets the localized cancellation progress message.
    /// </summary>
    public static string GetProgressCancellingPlans(int count, bool useChinese = true) =>
        string.Format(useChinese ? ProgressCancellingPlans_ZH : ProgressCancellingPlans_EN, count);

    /// <summary>
    /// Gets the localized preservation progress message.
    /// </summary>
    public static string GetProgressPreservingKnowledge(int count, bool useChinese = true) =>
        string.Format(useChinese ? ProgressPreservingKnowledge_ZH : ProgressPreservingKnowledge_EN, count);

    /// <summary>
    /// Gets the localized agent notification progress message.
    /// </summary>
    public static string GetProgressNotifyingAgents(bool useChinese = true) =>
        useChinese ? ProgressNotifyingAgents_ZH : ProgressNotifyingAgents_EN;

    /// <summary>
    /// Gets the localized DAG update progress message.
    /// </summary>
    public static string GetProgressUpdatingDag(bool useChinese = true) =>
        useChinese ? ProgressUpdatingDag_ZH : ProgressUpdatingDag_EN;

    /// <summary>
    /// Gets the localized completion message.
    /// </summary>
    public static string GetCompletionSuccess(string newTopic, bool useChinese = true) =>
        string.Format(useChinese ? CompletionSuccess_ZH : CompletionSuccess_EN, newTopic);

    /// <summary>
    /// Gets the localized completion summary.
    /// </summary>
    public static string GetCompletionSummary(int cancelledCount, int preservedCount, bool useChinese = true) =>
        string.Format(useChinese ? CompletionSummary_ZH : CompletionSummary_EN, cancelledCount, preservedCount);

    /// <summary>
    /// Gets the localized completion message with preservation details.
    /// </summary>
    public static string GetCompletionWithPreserve(string preservedAspect, bool useChinese = true) =>
        string.Format(useChinese ? CompletionWithPreserve_ZH : CompletionWithPreserve_EN, preservedAspect);

    /// <summary>
    /// Gets the localized error message.
    /// </summary>
    public static string GetErrorPivotFailed(string error, bool useChinese = true) =>
        string.Format(useChinese ? ErrorPivotFailed_ZH : ErrorPivotFailed_EN, error);

    /// <summary>
    /// Gets the localized timeout warning.
    /// </summary>
    public static string GetErrorTimeout(bool useChinese = true) =>
        useChinese ? ErrorTimeout_ZH : ErrorTimeout_EN;

    /// <summary>
    /// Gets the localized rollback availability message.
    /// </summary>
    public static string GetRollbackAvailable(int minutes, bool useChinese = true) =>
        string.Format(useChinese ? RollbackAvailable_ZH : RollbackAvailable_EN, minutes);

    /// <summary>
    /// Gets the localized rollback success message.
    /// </summary>
    public static string GetRollbackSuccess(string restoredDirection, bool useChinese = true) =>
        string.Format(useChinese ? RollbackSuccess_ZH : RollbackSuccess_EN, restoredDirection);

    /// <summary>
    /// Gets the localized rollback expired message.
    /// </summary>
    public static string GetRollbackExpired(bool useChinese = true) =>
        useChinese ? RollbackExpired_ZH : RollbackExpired_EN;
}
