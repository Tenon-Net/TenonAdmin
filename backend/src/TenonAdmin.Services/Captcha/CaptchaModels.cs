namespace TenonAdmin.Services;

/// <summary>验证码下发出参:票据 Id + SVG 图。前端展示 SVG,登录时回传 <see cref="CaptchaId"/> + 用户输入的码。</summary>
public record CaptchaOutput
{
    /// <summary>验证码票据 Id(登录时随输入码一起回传)</summary>
    public required string CaptchaId { get; init; }

    /// <summary>SVG 图字符串(直接内联展示)</summary>
    public required string Svg { get; init; }
}
