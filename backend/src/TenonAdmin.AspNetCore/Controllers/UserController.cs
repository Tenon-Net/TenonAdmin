using Microsoft.AspNetCore.Mvc;
using TenonAdmin.Core;
using TenonAdmin.Services;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 用户管理端点(设计 §4 组织模块)。全部 <c>[RolePermission]</c>——超管放行,普通用户需被授予对应路由权限码。
/// 安全细节(账号唯一、不出密码哈希、不建/改超管、超管不可删停)由 <see cref="IUserService"/> 保证。
/// </summary>
[ApiController]
[Route("api/v1/sys/user")]
public class UserController(IUserService users) : ControllerBase
{
    /// <summary>分页查询用户</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<UserItem>>> Page([FromQuery] UserPageInput input) =>
        Result<PagedList<UserItem>>.Ok(await users.PageAsync(input));

    /// <summary>用户详情(含角色 Id)</summary>
    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<UserDetail>> Get(long id) =>
        Result<UserDetail>.Ok(await users.GetAsync(id));

    /// <summary>新增用户,返回新用户 Id</summary>
    [HttpPost]
    [RolePermission]
    public async Task<Result<long>> Add(AddUserInput input) =>
        Result<long>.Ok(await users.AddAsync(input));

    /// <summary>更新用户资料与角色</summary>
    [HttpPut("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Update(long id, UpdateUserInput input)
    {
        await users.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    /// <summary>软删除用户</summary>
    [HttpDelete("{id}")]
    [RolePermission]
    public async Task<Result<bool>> Delete(long id)
    {
        await users.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }

    /// <summary>重置密码;返回实际生效的初始密码(供管理员当场转达)</summary>
    [HttpPut("{id}/password")]
    [RolePermission]
    public async Task<Result<string>> ResetPassword(long id, ResetPasswordInput input) =>
        Result<string>.Ok(await users.ResetPasswordAsync(id, input.NewPassword));

    /// <summary>启用/停用</summary>
    [HttpPut("{id}/enabled")]
    [RolePermission]
    public async Task<Result<bool>> SetEnabled(long id, SetEnabledInput input)
    {
        await users.SetEnabledAsync(id, input.Enabled);
        return Result<bool>.Ok(true);
    }
}
