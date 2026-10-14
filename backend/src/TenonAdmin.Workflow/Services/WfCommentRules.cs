namespace TenonAdmin.Workflow;

/// <summary>审批意见规则。拒绝和退回必须填写；长度上限在后续校验里收紧。</summary>
public static class WfCommentRules
{
    /// <summary>
    /// 拒绝 / 退回的意见。空白视为没填。
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

        return text;
    }
}
