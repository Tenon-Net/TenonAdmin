namespace TenonAdmin.Workflow;

/// <summary>审批意见规则。拒绝和退回必须填写，所有人工意见最长 1000 字。</summary>
public static class WfCommentRules
{
    /// <summary>去空白后的最大字数。历史列宽与此一致。</summary>
    public const int MaxLength = 1000;

    /// <summary>
    /// 拒绝 / 退回的意见。空白视为没填，超长单独拒绝。
    /// </summary>
    public static string Require(string? comment)
    {
        var text = comment?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw WorkflowErrorCode.Exception(WorkflowErrorCode.CommentRequired, new Dictionary<string, object?>
            {
                ["reason"] = "commentRequired",
            });
        }

        EnsureLength(text);
        return text;
    }

    /// <summary>同意、转办等可选意见。纯空白当成没填，超长拒绝。</summary>
    public static string? NormalizeOptional(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;
        var text = comment.Trim();
        EnsureLength(text);
        return text;
    }

    private static void EnsureLength(string text)
    {
        if (text.Length <= MaxLength)
            return;

        throw WorkflowErrorCode.Exception(WorkflowErrorCode.CommentTooLong, new Dictionary<string, object?>
        {
            ["reason"] = "commentTooLong",
            ["maxLength"] = MaxLength,
        });
    }
}
