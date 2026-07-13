using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 菜单管理端点(设计 §16)——菜单树(目录/页面/按钮三级)的增删改查,供前端菜单管理页。
/// <para>全 <c>[RolePermission]</c>:超管自动放行;普通管理员须被授予对应菜单权限码
/// (默认种子<b>不</b>把本组端点授给默认角色,故非超管默认不可管理菜单,安全默认拒绝 §14)。</para>
/// <para>只读的门户接口("我的模块/菜单树")在 <c>PersonalController</c>;本控制器只管管理端全量 CRUD。</para>
/// </summary>
[ApiController]
[Route("api/v1/sys/menu")]
public class MenuController(IMenuService menuService) : ControllerBase
{
    /// <summary>全量菜单树(管理端,含停用节点与按钮,全字段)</summary>
    [HttpGet("tree")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<MenuTreeNode>>> Tree()
    {
        var data = await menuService.GetTreeAsync();
        return Result<IReadOnlyList<MenuTreeNode>>.Ok(data);
    }

    /// <summary>
    /// 可授权路由清单——菜单表单里"权限码"字段的<b>下拉数据源</b>。
    /// <para><see cref="RolePermissionAttribute"/> 的设计前提是"勾选路由即完成配权,代码里永远不出现魔法字符串",
    /// 但若不把路由清单暴露出来,魔法字符串只是从代码搬进了管理员手敲的文本框:大小写、<c>{id}</c> 占位符、
    /// 前导斜杠错一个字符,就是"明明授权了却还是 403",而且<b>没有任何报错</b>——超管测试时一切正常(sadm 放行),
    /// 交付给客户的普通账号才炸。这是内核对消费方最贵的一道坎。</para>
    /// <para>数据取自 <see cref="EndpointDataSource"/>(运行时真实路由表),<b>消费方自建的控制器自动出现在这里</b>
    /// (它们经 ApplicationParts 进同一张表);权限码由 <see cref="PermissionCode"/> 统一算,与授权比对天然一致。</para>
    /// </summary>
    [HttpGet("routes")]
    [RolePermission]
    public Result<IReadOnlyList<PermissionRouteItem>> Routes([FromServices] EndpointDataSource endpoints) =>
        Result<IReadOnlyList<PermissionRouteItem>>.Ok(
            endpoints.Endpoints
                .OfType<RouteEndpoint>()
                // 只列受权端点:没挂 [RolePermission] 的(匿名/仅登录)本就不参与角色授权,列出来只会误导
                .Where(e => e.Metadata.GetMetadata<RolePermissionAttribute>() is not null)
                .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                    .Select(m => new PermissionRouteItem
                    {
                        Code = PermissionCode.Build(m, e.RoutePattern.RawText),
                        Method = m.ToUpperInvariant(),
                        Path = "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'),
                    }))
                .DistinctBy(x => x.Code)
                .OrderBy(x => x.Path).ThenBy(x => x.Method)
                .ToList());

    /// <summary>新增菜单,返回新 Id</summary>
    [HttpPost("add")]
    [RolePermission]
    [OperationLog("新增菜单")]
    public async Task<Result<long>> Add(MenuInput input)
    {
        var id = await menuService.CreateAsync(input);
        return Result<long>.Ok(id);
    }

    /// <summary>更新菜单</summary>
    [HttpPut("{id}")]
    [RolePermission]
    [OperationLog("更新菜单")]
    public async Task<Result<bool>> Update(long id, MenuInput input)
    {
        await menuService.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>删除菜单(软删;带子节点则拒删)</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    [OperationLog("删除菜单")]
    public async Task<Result<bool>> Delete(long id)
    {
        await menuService.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }
}
