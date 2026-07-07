using Microsoft.AspNetCore.Mvc.ApplicationModels;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 按配置禁用模块(设计 §5.4)。构建应用模型时,把带 <see cref="ModuleAttribute"/> 且模块名在
/// <c>Api:DisabledModules</c> 里的控制器整体移除——其路由不再注册,接口返回 404。
/// </summary>
internal sealed class DisabledModuleConvention(AdminApiOptions options) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        if (options.DisabledModules.Length == 0) return;

        var disabled = new HashSet<string>(options.DisabledModules, StringComparer.OrdinalIgnoreCase);
        // 先物化再移除:遍历中修改集合会抛
        var toRemove = application.Controllers
            .Where(c => c.Attributes.OfType<ModuleAttribute>().FirstOrDefault() is { } m && disabled.Contains(m.Name))
            .ToList();

        foreach (var controller in toRemove)
            application.Controllers.Remove(controller);
    }
}
