using System.Collections.Frozen;

namespace TenonAdmin.Services;

/// <summary>
/// 高敏感权限码内核默认集合。
/// 集合不可变、不可经管理页删除;消费者只能追加自定义码。
/// </summary>
public static class HighSensitivityPermissions
{
    /// <summary>管理员清除用户 MFA。</summary>
    public const string MfaClear = "POST:/api/v1/sys/mfa/clear";

    /// <summary>追加自定义高敏权限码。</summary>
    public const string HighSensAdd = "POST:/api/v1/sys/mfa/high-sensitivity";

    /// <summary>删除自定义高敏权限码。</summary>
    public const string HighSensDelete = "DELETE:/api/v1/sys/mfa/high-sensitivity/{id:long}";

    /// <summary>内核默认高敏权限码(冻结集)。</summary>
    public static FrozenSet<string> Default { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "POST:/api/v1/sys/user",
        "PUT:/api/v1/sys/user/{id}",
        "DELETE:/api/v1/sys/user/{id}",
        "POST:/api/v1/sys/user/batch-delete",
        "PUT:/api/v1/sys/user/{id}/password",
        "PUT:/api/v1/sys/user/{id}/enabled",

        "POST:/api/v1/sys/role/add",
        "PUT:/api/v1/sys/role/{id}",
        "DELETE:/api/v1/sys/role/{id}",
        "POST:/api/v1/sys/role/batch-delete",
        "PUT:/api/v1/sys/role/menu",
        "PUT:/api/v1/sys/role/datascope",
        "PUT:/api/v1/sys/role/users",

        "POST:/api/v1/sys/config",
        "PUT:/api/v1/sys/config/{id}",
        "DELETE:/api/v1/sys/config/{id}",
        "PUT:/api/v1/sys/config/batch",

        "DELETE:/api/v1/sys/session/{sessionid}",

        MfaClear,
        HighSensAdd,
        HighSensDelete,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否属于内核默认高敏集合(不可移除)。</summary>
    public static bool IsDefault(string permissionCode) =>
        !string.IsNullOrWhiteSpace(permissionCode) && Default.Contains(permissionCode.Trim());
}
