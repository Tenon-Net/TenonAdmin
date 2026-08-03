namespace TenonAdmin.Auth.GitHub;

/// <summary>GitHub OAuth App 配置(对应 <c>TenonAdmin:ExternalAuth:GitHub</c>)。Code 固定为 <c>github</c>,不暴露可改字段。</summary>
public class GitHubAuthOptions
{
    /// <summary>OAuth App Client ID。</summary>
    public string ClientId { get; set; } = "";

    /// <summary>OAuth App Client Secret(仅服务端,不进 providers 响应)。</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>登录页/绑定列表显示名;空或空白回退 <c>GitHub</c>。</summary>
    public string DisplayName { get; set; } = "GitHub";

    /// <summary>可选 Iconify 名(前端 brand map 优先用 code=github)。</summary>
    public string? Icon { get; set; }
}
