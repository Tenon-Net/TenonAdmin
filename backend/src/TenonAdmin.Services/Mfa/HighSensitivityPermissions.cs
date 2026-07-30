using System.Collections.Frozen;

namespace TenonAdmin.Services;

/// <summary>
/// 高敏感权限码内核默认集合(等保三级应用安全一期)。
/// <para>
/// 集合不可变、不可经管理页删除;消费者只能通过 <c>sys_high_sensitivity_permission</c> <b>追加</b>自定义码。
/// 权限码 = 规范化路由(与 <c>[RolePermission]</c> 同源)。
/// </para>
/// </summary>
public static class HighSensitivityPermissions
{
    /// <summary>发放 TOTP 绑定邀请的权限码(与路由一致;服务层授权与高敏默认集共用,勿重复字面量)。</summary>
    public const string MfaInvite = "POST:/api/v1/sys/mfa/invite";

    /// <summary>撤销 TOTP 绑定邀请的权限码。</summary>
    public const string MfaInviteRevoke = "DELETE:/api/v1/sys/mfa/invite/{id:long}";

    /// <summary>超级管理员 MFA 重置权限码。</summary>
    public const string MfaReset = "POST:/api/v1/sys/mfa/reset";

    /// <summary>追加自定义高敏权限码。</summary>
    public const string HighSensAdd = "POST:/api/v1/sys/mfa/high-sensitivity";

    /// <summary>删除自定义高敏权限码。</summary>
    public const string HighSensDelete = "DELETE:/api/v1/sys/mfa/high-sensitivity/{id:long}";

    /// <summary>
    /// 内核默认高敏权限码(冻结集)。覆盖:用户管理写操作、角色授权、配置写操作、会话强退、MFA 管理。
    /// </summary>
    public static FrozenSet<string> Default { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // ── 用户管理(写) ──────────────────────────────────────────
        "POST:/api/v1/sys/user",
        "PUT:/api/v1/sys/user/{id}",
        "DELETE:/api/v1/sys/user/{id}",
        "POST:/api/v1/sys/user/batch-delete",
        "PUT:/api/v1/sys/user/{id}/password",
        "PUT:/api/v1/sys/user/{id}/enabled",

        // ── 角色与授权 ────────────────────────────────────────────
        "POST:/api/v1/sys/role/add",
        "PUT:/api/v1/sys/role/{id}",
        "DELETE:/api/v1/sys/role/{id}",
        "POST:/api/v1/sys/role/batch-delete",
        "PUT:/api/v1/sys/role/menu",
        "PUT:/api/v1/sys/role/datascope",
        "PUT:/api/v1/sys/role/users",

        // ── 系统配置(安全策略等) ──────────────────────────────────
        "POST:/api/v1/sys/config",
        "PUT:/api/v1/sys/config/{id}",
        "DELETE:/api/v1/sys/config/{id}",
        "PUT:/api/v1/sys/config/batch",

        // ── 会话强退 ──────────────────────────────────────────────
        "DELETE:/api/v1/sys/session/{sessionid}",

        // ── MFA 管理(内核 MFA 端点;常量引用,避免与服务层漂移) ────
        MfaInvite,
        MfaReset,
        MfaInviteRevoke,
        HighSensAdd,
        HighSensDelete,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否属于内核默认高敏集合(不可移除)。</summary>
    public static bool IsDefault(string permissionCode) =>
        !string.IsNullOrWhiteSpace(permissionCode) && Default.Contains(permissionCode.Trim());
}
