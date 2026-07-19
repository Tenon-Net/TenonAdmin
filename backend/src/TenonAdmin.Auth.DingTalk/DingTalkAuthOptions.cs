namespace TenonAdmin.Auth.DingTalk;

/// <summary>钉钉登录配置(对应 <c>TenonAdmin:ExternalAuth:DingTalk</c> 节)。密钥随部署配置,不进库(批次 D 折中)。</summary>
public class DingTalkAuthOptions
{
    /// <summary>provider 唯一码(登录按钮 / 回调路由 / 运营配置键都用它)</summary>
    public string Code { get; set; } = "dingtalk";

    /// <summary>展示名(登录页按钮文案兜底)</summary>
    public string DisplayName { get; set; } = "钉钉";

    /// <summary>图标(iconify 名或图片 URL,可空)</summary>
    public string? Icon { get; set; } = "ph:qr-code-duotone";

    /// <summary>应用 AppKey(扫码登录应用的 client_id)</summary>
    public string AppKey { get; set; } = "";

    /// <summary>应用 AppSecret(client_secret;换取用户 access_token 用)</summary>
    public string AppSecret { get; set; } = "";
}
