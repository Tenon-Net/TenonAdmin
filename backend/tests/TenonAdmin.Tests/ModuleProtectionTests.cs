using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.Tests;

/// <summary>
/// 模块守卫:内置 system 模块不可停用(它承载全部管理页,停用即门户失联且无 UI 恢复入口,比照删除保护);
/// 任何模块只要还有挂靠菜单就不可删(防顶级目录 ModuleId 悬空、整棵子树从门户消失)。
/// </summary>
public class ModuleProtectionTests
{
    /// <summary>内置 system 模块固定 Id(= DefaultModuleSeed.BUILTIN_MODULE_ID,internal 不可见,此处复述)。</summary>
    private const long BuiltinModuleId = 1;

    private static ModuleInput ToInput(SysModule m, bool enabled) => new()
    {
        Code = m.Code, Title = m.Title, Icon = m.Icon, DefaultRoute = m.DefaultRoute,
        ApiPrefix = m.ApiPrefix, Sort = m.Sort, Enabled = enabled, Remark = m.Remark,
    };

    [Fact]
    public async Task Builtin_module_cannot_be_disabled_but_normal_update_passes()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();
        var modules = scope.ServiceProvider.GetRequiredService<IModuleService>();

        var builtin = await modules.GetAsync(BuiltinModuleId);
        var ex = await Assert.ThrowsAsync<AdminException>(
            () => modules.UpdateAsync(BuiltinModuleId, ToInput(builtin, enabled: false)));
        Assert.Equal(ErrorCode.ModuleProtected, ex.Code);
        Assert.True((await modules.GetAsync(BuiltinModuleId)).Enabled);   // 拦截生效,库里没被改

        // 不停用的常规更新(改标题等)不受守卫影响
        await modules.UpdateAsync(BuiltinModuleId, ToInput(builtin, enabled: true));
    }

    [Fact]
    public async Task Module_with_menus_rejects_delete_until_menus_removed()
    {
        using var factory = new AdminAppFactory();
        using var scope = factory.Services.CreateScope();
        var modules = scope.ServiceProvider.GetRequiredService<IModuleService>();
        var menus = scope.ServiceProvider.GetRequiredService<IMenuService>();

        var moduleId = await modules.AddAsync(new ModuleInput { Code = "t-del", Title = "待删模块" });
        var menuId = await menus.CreateAsync(new MenuInput
        {
            ParentId = 0, Type = MenuType.Catalog, Title = "挂靠目录", ModuleId = moduleId, Enabled = true,
        });

        var ex = await Assert.ThrowsAsync<AdminException>(() => modules.DeleteAsync(moduleId));
        Assert.Equal(ErrorCode.ModuleHasMenus, ex.Code);

        await menus.DeleteAsync(menuId);      // 先清挂靠菜单(软删即可——守卫只看未删行)
        await modules.DeleteAsync(moduleId);  // 即可删除,不抛即过
    }
}
