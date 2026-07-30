namespace TenonAdmin.Core;

/// <summary>
/// 安全档访问器:读部署配置中的 <see cref="AdminSecurityOptions.Profile"/>,供策略层/预检/业务判定 Level3。
/// 默认实现注册为 Singleton;<c>TryAdd</c> 可替换(测试可钉死档位)。
/// </summary>
public interface ISecurityProfileAccessor
{
    /// <summary>当前安全档(部署配置,运行时不可经 SysConfig 降级)</summary>
    SecurityProfile Profile { get; }

    /// <summary>是否已启用 Level3 强制档</summary>
    bool IsLevel3 { get; }

    /// <summary>
    /// 生产环境且未启用 Level3——应告警并在预检标记不合规,但不阻断既有项目运行。
    /// </summary>
    bool IsProductionWithoutLevel3 { get; }
}
