using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="ISecurityProfileAccessor"/> 默认实现:读 <see cref="AdminSecurityOptions.Profile"/> + 宿主环境。
/// 产品能力判定请优先 <see cref="AdminSecurityOptions"/> helpers(ADR 0006)。
/// </summary>
public class SecurityProfileAccessor(AdminSecurityOptions security, IHostEnvironment env) : ISecurityProfileAccessor
{
    /// <inheritdoc />
    public virtual SecurityProfile Profile => security.Profile;

    /// <inheritdoc />
    public virtual bool IsLevel3 => security.IsLegacyLevel3Profile;

    /// <inheritdoc />
    public virtual bool IsProductionWithoutLevel3 => env.IsProduction() && !IsLevel3;
}
