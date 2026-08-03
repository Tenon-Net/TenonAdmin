namespace TenonAdmin.Auth.WeChat;

/// <summary>微信开放平台网站应用配置(<c>TenonAdmin:ExternalAuth:WeChat</c>)。Code 固定 <c>wechat</c>。</summary>
public class WeChatAuthOptions
{
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";

    /// <summary>空则回退「微信」。</summary>
    public string DisplayName { get; set; } = "微信";

    public string? Icon { get; set; }
}
