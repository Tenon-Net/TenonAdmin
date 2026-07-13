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

    /// <summary>新增用户,返回新用户 Id + 实际生效的初始口令(留空口令时由系统随机生成,不回传则无人知晓)</summary>
    [HttpPost]
    [RolePermission]
    [OperationLog("新增用户")]   // 入参含 Password,操作日志里会被脱敏为 ***;出参明文不进日志(过滤器只记入参)
    public async Task<Result<AddUserOutput>> Add(AddUserInput input) =>
        Result<AddUserOutput>.Ok(await users.AddAsync(input));

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

    /// <summary>批量软删除用户(集合含超管则整体拒绝)</summary>
    [HttpPost("batch-delete")]
    [RolePermission]
    [OperationLog("批量删除用户")]
    public async Task<Result<bool>> BatchDelete(BatchDeleteInput input)
    {
        await users.DeleteBatchAsync(input.Ids);
        return Result<bool>.Ok(true);
    }

    /// <summary>重置密码;返回实际生效的初始密码(供管理员当场转达)</summary>
    [HttpPut("{id}/password")]
    [RolePermission]
    [OperationLog("重置用户密码")]   // 入参 NewPassword 会被脱敏;返回的明文密码不进日志(过滤器只记入参)
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
