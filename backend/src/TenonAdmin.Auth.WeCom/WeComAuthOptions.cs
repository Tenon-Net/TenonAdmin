namespace TenonAdmin.Auth.WeCom;

/// <summary>企业微信登录配置(对应 <c>TenonAdmin:ExternalAuth:WeCom</c> 节)。密钥随部署配置,不进库(批次 D 折中)。</summary>
public class WeComAuthOptions
{
    /// <summary>provider 唯一码(登录按钮 / 回调路由 / 运营配置键都用它)</summary>
    public string Code { get; set; } = "wecom";

    /// <summary>展示名(登录页按钮文案兜底)</summary>
    public string DisplayName { get; set; } = "企业微信";

    /// <summary>图标(iconify 名或图片 URL,可空)</summary>
    public string? Icon { get; set; } = "ph:qr-code-duotone";

    /// <summary>企业 Id(corpid)</summary>
    public string CorpId { get; set; } = "";

    /// <summary>应用 AgentId(网页/扫码登录归属的自建应用)</summary>
    public string AgentId { get; set; } = "";

    /// <summary>应用 Secret(corpsecret;换取 access_token 用)</summary>
    public string CorpSecret { get; set; } = "";
}
