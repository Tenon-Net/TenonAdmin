using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Integration;

/// <summary>
/// 第三方接入菜单种子:内置「系统」应用下的「第三方接入」目录。模块未启用即不注册本种子 → 不出现任何不可用菜单。
/// <para>按钮权限码 = 规范化路由(<c>PermissionCode.Build</c>:方法大写、模板小写,路由参数名同样小写),
/// 与授权管道逐字一致;号段 <c>ConsumerMin + 49_000</c> 起(工作流占 47_000 段)。
/// 结构属于模块所有,<see cref="SyncOnUpgrade"/> 开启以便内核版本升级时刷回挂载点。</para>
/// </summary>
internal sealed class IntegrationMenuSeed : ISeedData<SysMenu>
{
    internal const long RootId = TenonSeedIds.ConsumerMin + 49_000;
    internal const long AppMenuId = RootId + 1;
    internal const long InboundLogMenuId = RootId + 40;
    internal const long OutboundLogMenuId = RootId + 50;
    internal const long DeliveryMenuId = RootId + 60;

    // 与内核 DefaultModuleSeed 同值;那些常量是 internal,跨程序集取不到,故此处复制(同工作流种子)。
    private const long BuiltInModuleId = 1;

    public bool SyncOnUpgrade => true;

    public IEnumerable<SysMenu> HasData()
    {
        yield return new SysMenu { Id = RootId, ParentId = 0, Type = MenuType.Catalog, Title = "第三方接入", Permission = "", Icon = "ph:plugs-connected-duotone", Sort = 7, Enabled = true, Visible = true, ModuleId = BuiltInModuleId };

        yield return new SysMenu { Id = AppMenuId, ParentId = RootId, Type = MenuType.Menu, Title = "接入应用", Permission = "", Path = "/integration/app", Component = "integration/app/index", Icon = "ph:app-window-duotone", Sort = 1, Enabled = true, Visible = true };
        var appButtons = new (string Title, string Permission)[]
        {
            ("接入应用-分页", "GET:/api/v1/integration/app/page"),
            ("接入应用-详情", "GET:/api/v1/integration/app/{id}"),
            ("接入应用-新增", "POST:/api/v1/integration/app"),
            ("接入应用-修改", "PUT:/api/v1/integration/app/{id}"),
            ("接入应用-启用", "POST:/api/v1/integration/app/{id}/enable"),
            ("接入应用-停用", "POST:/api/v1/integration/app/{id}/disable"),
            ("接入应用-删除", "DELETE:/api/v1/integration/app/{id}"),
            ("接入凭据-列表", "GET:/api/v1/integration/app/{id}/credentials"),
            ("接入凭据-发放", "POST:/api/v1/integration/app/{id}/credentials"),
            ("接入凭据-轮换", "POST:/api/v1/integration/app/{id}/credentials/{credentialid}/rotate"),
            ("接入凭据-撤销", "POST:/api/v1/integration/app/{id}/credentials/{credentialid}/revoke"),
            ("接入凭据-调整到期", "PUT:/api/v1/integration/app/{id}/credentials/{credentialid}/expiry"),
            ("接入授权-查看", "GET:/api/v1/integration/app/{id}/grants"),
            ("接入授权-修改", "PUT:/api/v1/integration/app/{id}/grants"),
            ("数据范围-查看", "GET:/api/v1/integration/app/{id}/scopes"),
            ("数据范围-修改", "PUT:/api/v1/integration/app/{id}/scopes"),
            ("开放端点清单", "GET:/api/v1/integration/catalog/endpoints"),
            ("范围策略清单", "GET:/api/v1/integration/catalog/scopes"),
            ("范围候选值", "GET:/api/v1/integration/catalog/scopes/{key}/options"),
            ("开放文档下载", "GET:/api/v1/integration/catalog/open-api/{version}"),
            ("接入凭据-删除", "DELETE:/api/v1/integration/app/{id}/credentials/{credentialid}"),
        };
        for (var i = 0; i < appButtons.Length; i++)
            yield return new SysMenu { Id = AppMenuId + 1 + i, ParentId = AppMenuId, Type = MenuType.Button, Title = appButtons[i].Title, Permission = appButtons[i].Permission, Sort = i + 1, Enabled = true };

        yield return new SysMenu { Id = InboundLogMenuId, ParentId = RootId, Type = MenuType.Menu, Title = "开放接口调用记录", Permission = "", Path = "/integration/inbound-log", Component = "integration/inbound-log/index", Icon = "ph:list-magnifying-glass-duotone", Sort = 2, Enabled = true, Visible = true };
        yield return new SysMenu { Id = InboundLogMenuId + 1, ParentId = InboundLogMenuId, Type = MenuType.Button, Title = "查看开放接口调用记录", Permission = "GET:/api/v1/integration/inbound-log/page", Sort = 1, Enabled = true };

        yield return new SysMenu { Id = OutboundLogMenuId, ParentId = RootId, Type = MenuType.Menu, Title = "第三方接口调用记录", Permission = "", Path = "/integration/outbound-log", Component = "integration/outbound-log/index", Icon = "ph:arrow-square-out-duotone", Sort = 3, Enabled = true, Visible = true };
        yield return new SysMenu { Id = OutboundLogMenuId + 1, ParentId = OutboundLogMenuId, Type = MenuType.Button, Title = "查看第三方接口调用记录", Permission = "GET:/api/v1/integration/outbound-log/page", Sort = 1, Enabled = true };
        yield return new SysMenu { Id = OutboundLogMenuId + 2, ParentId = OutboundLogMenuId, Type = MenuType.Button, Title = "查看对方系统配置", Permission = "GET:/api/v1/integration/outbound-log/targets", Sort = 2, Enabled = true };

        yield return new SysMenu { Id = DeliveryMenuId, ParentId = RootId, Type = MenuType.Menu, Title = "发送任务", Permission = "", Path = "/integration/delivery", Component = "integration/delivery/index", Icon = "ph:paper-plane-tilt-duotone", Sort = 4, Enabled = true, Visible = true };
        var deliveryButtons = new (string Title, string Permission)[]
        {
            ("查看发送任务", "GET:/api/v1/integration/delivery/page"),
            ("查看任务状态汇总", "GET:/api/v1/integration/delivery/summary"),
            ("查看发送任务详情", "GET:/api/v1/integration/delivery/{id}"),
            ("重新发送任务", "POST:/api/v1/integration/delivery/{id}/retry"),
            ("查询对方处理结果", "POST:/api/v1/integration/delivery/{id}/query"),
            ("确认任务已成功", "POST:/api/v1/integration/delivery/{id}/confirm-succeeded"),
            ("确认对方未执行", "POST:/api/v1/integration/delivery/{id}/confirm-not-executed"),
            ("取消发送任务", "POST:/api/v1/integration/delivery/{id}/cancel"),
        };
        for (var i = 0; i < deliveryButtons.Length; i++)
            yield return new SysMenu { Id = DeliveryMenuId + 1 + i, ParentId = DeliveryMenuId, Type = MenuType.Button, Title = deliveryButtons[i].Title, Permission = deliveryButtons[i].Permission, Sort = i + 1, Enabled = true };
    }
}
