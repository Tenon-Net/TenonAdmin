using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 系统配置管理端点。全部 <c>[RolePermission]</c>——超管放行,普通用户需被授予对应路由权限码。
/// <c>value/{key}</c> 走读穿透缓存,供前端/其他调用方按键取配置值。
/// </summary>
[ApiController]
[Route("api/v1/sys/config")]
[Module("Config")]   // 可经 Api:DisabledModules=["Config"] 关闭
public class ConfigController(IConfigService configs) : ControllerBase
{
    /// <summary>分页查询配置</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<SysConfig>>> Page([FromQuery] ConfigPageInput input) =>
        Result<PagedList<SysConfig>>.Ok(await configs.PageAsync(input));

    /// <summary>配置详情</summary>
    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<SysConfig>> Get(long id) =>
        Result<SysConfig>.Ok(await configs.GetAsync(id));

    /// <summary>按配置键取值(读穿透缓存)</summary>
    [HttpGet("value/{key}")]
    [RolePermission]
    public async Task<Result<string?>> GetValue(string key) =>
        Result<string?>.Ok(await configs.GetValueByKeyAsync(key));

    /// <summary>站点信息(匿名可读:登录前/无配置权限的用户取站点标题等展示白名单,不暴露任意配置)</summary>
    [HttpGet("site")]
    [AllowAnonymous]
    public async Task<Result<SiteInfoOutput>> Site() =>
        Result<SiteInfoOutput>.Ok(await configs.GetSiteInfoAsync());

    /// <summary>批量按键回写配置值(分类配置中心结构化表单保存)</summary>
    [HttpPut("batch")]
    [RolePermission]
    public async Task<Result<bool>> Batch([FromBody] IReadOnlyCollection<ConfigBatchItem> items)
    {
        await configs.SaveValuesAsync(items);
        return Result<bool>.Ok(true);
    }

    /// <summary>新增配置,返回新 Id</summary>
    [HttpPost]
    [RolePermission]
    public async Task<Result<long>> Add(ConfigInput input) =>
        Result<long>.Ok(await configs.AddAsync(input));

    /// <summary>更新配置(不含配置键)</summary>
    [HttpPut("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Update(long id, ConfigInput input)
    {
        await configs.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>软删除配置</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Delete(long id)
    {
        await configs.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }
}
