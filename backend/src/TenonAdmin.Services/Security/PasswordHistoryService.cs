using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IPasswordHistoryService"/> 默认实现。条数经 <see cref="ISecurityPolicyProvider"/> 读取
/// (DB 优先、Options 兜底,改值即时生效);哈希逐条 <c>Verify(明文)</c> 比对——盐不同不能比哈希、只能验明文。
/// </summary>
public class PasswordHistoryService(
    IRepository<SysPasswordHistory> history,
    IRepository<SysUser> users,
    IPasswordHasher hasher,
    ISecurityPolicyProvider policy) : IPasswordHistoryService
{
    /// <inheritdoc />
    public virtual async Task EnsureNotReusedAsync(long userId, string newPlainPassword)
    {
        var count = await policy.GetPasswordHistoryCountAsync();
        if (count <= 0) return;   // 关闭

        // 当前口令也算"最近用过的一个"——历史为空(刚开启策略)时也能挡住"改成当前口令"。
        var user = await users.GetByIdAsync(userId);
        if (user is not null && !string.IsNullOrEmpty(user.Password) && hasher.Verify(newPlainPassword, user.Password))
            throw new AdminException(ErrorCode.PasswordReused);

        var recent = await RecentHashesAsync(userId, count);
        if (recent.Any(h => hasher.Verify(newPlainPassword, h)))
            throw new AdminException(ErrorCode.PasswordReused);
    }

    /// <inheritdoc />
    public virtual async Task AppendAsync(long userId, string passwordHash)
    {
        var count = await policy.GetPasswordHistoryCountAsync();
        if (count <= 0) return;   // 关闭:不写,零表增长

        await history.InsertAsync(new SysPasswordHistory { UserId = userId, PasswordHash = passwordHash });
        await TrimAsync(userId, count);
    }

    /// <summary>取该用户最新 <paramref name="count"/> 条历史口令哈希(按雪花 Id 降序 = 时间新→旧)。</summary>
    protected virtual async Task<List<string>> RecentHashesAsync(long userId, int count) =>
        await history.AsQueryable()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .Take(count)
            .Select(x => x.PasswordHash)
            .ToListAsync();

    /// <summary>
    /// 裁剪该用户历史,只保留最新 <paramref name="count"/> 条,余者硬删(历史无软删语义)。
    /// 每用户历史恒 ≤ N+1 条(每次 Append 后即裁剪),故全量取 Id 再内存跳过成本可忽略。
    /// </summary>
    protected virtual async Task TrimAsync(long userId, int count)
    {
        var ids = await history.AsQueryable()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .Select(x => x.Id)
            .ToListAsync();
        if (ids.Count <= count) return;

        var stale = ids.Skip(count).ToList();
        await history.Db.Deleteable<SysPasswordHistory>().Where(x => stale.Contains(x.Id)).ExecuteCommandAsync();
    }
}
