using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2c Task 2「回执服务」契约测试(设计规划 §14.2、数据库评审 §五)。
/// <para><b>⚠ 射程声明</b>:本文件钉的是<b>持久化语义</b>——占位、命中、回填、<b>同事务回滚不残留</b>、
/// 归一化同源。<b>真并发交错构造不出来</b>(与 <see cref="WfVersionCasTests"/> 的射程限制同因:
/// 单线程顺序执行下第二次 SELECT 必然读到最新值),所以<b>不要</b>把
/// <see cref="Rollback_leaves_no_receipt_behind"/> 读成「并发只推进一次」的证明——它证明的是
/// 「回执与领域状态同生共死」,那才是并发正确性的**前提**而非并发本身。并发的真实仲裁者是唯一索引
/// (<c>uk_wf_receipt_identity</c>)与 M2b 的 <c>Version</c> CAS,四库行为差异留给 Task 8 的契约套件。</para>
/// <para>本文件<b>不经引擎</b>:引擎挂钩是 Task 5,这里直接对 SPI 断言,失败时定位不必穿过整条命令链。</para>
/// </summary>
public class WfOperationReceiptTests
{
    private static WfOperationIdentity Identity(
        string? scope = "org-1",
        WfCommandType command = WfCommandType.Approve,
        long targetId = 1001L,
        long actor = 2002L,
        string requestKey = "req-a") =>
        WfOperationIdentity.Create(scope, command, WfTargetType.Task, targetId, actor, requestKey);

    /// <summary>宿主起来 + 建表;返回作用域内的服务与同一个 SqlSugar 单例。</summary>
    private static (IServiceScope Scope, IWfOperationReceiptService Svc, ISqlSugarClient Db) Open(WorkflowAppFactory f)
    {
        _ = f.CreateClient(); // 触发宿主启动与 CodeFirst 建表
        var scope = f.Services.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<IWfOperationReceiptService>(),
            scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
    }

    private static Task<WfOperationReceipt> FindAsync(ISqlSugarClient db, WfOperationIdentity id) =>
        db.Queryable<WfOperationReceipt>().Where(r => r.IdentityHash == id.IdentityHash).FirstAsync();

    /// <summary>#1 首次 TryBegin 返回 null(= 可以继续执行),并落下占位行。</summary>
    [Fact]
    public async Task First_try_begin_reserves_a_placeholder_and_returns_null()
    {
        using var f = new WorkflowAppFactory();
        var (scope, svc, db) = Open(f);
        using var _ = scope;
        var id = Identity();

        Assert.Null(await svc.TryBeginAsync(id));

        var row = await FindAsync(db, id);
        Assert.NotNull(row);
        Assert.Equal(id.ScopeKey, row.ScopeKey);
        Assert.Equal(id.RequestKey, row.RequestKey);
        Assert.Equal(WfCommandType.Approve, row.CommandType);
        Assert.Equal(WfTargetType.Task, row.TargetType);
        Assert.Null(row.ResultJson); // 占位:结果列还空着
    }

    /// <summary>#2 同 identity 第二次 TryBegin 命中已有回执,拿回第一次 Commit 写下的结果。</summary>
    [Fact]
    public async Task Second_try_begin_returns_the_first_result()
    {
        using var f = new WorkflowAppFactory();
        var (scope, svc, _) = Open(f);
        using var __ = scope;
        var id = Identity();

        Assert.Null(await svc.TryBeginAsync(id));
        await svc.CommitAsync(id, 0, """{"instanceId":77}""");

        var hit = await svc.TryBeginAsync(id);
        Assert.NotNull(hit);
        Assert.Equal(0, hit.ResultCode);
        Assert.Equal("""{"instanceId":77}""", hit.ResultJson);
    }

    /// <summary>#3 Commit 只回填不新增:同 hash 仍然只有一行。</summary>
    [Fact]
    public async Task Commit_updates_in_place_without_inserting_a_second_row()
    {
        using var f = new WorkflowAppFactory();
        var (scope, svc, db) = Open(f);
        using var _ = scope;
        var id = Identity();

        await svc.TryBeginAsync(id);
        await svc.CommitAsync(id, 0, """{"ok":true}""");

        var count = await db.Queryable<WfOperationReceipt>()
            .Where(r => r.IdentityHash == id.IdentityHash).CountAsync();
        Assert.Equal(1, count);
    }

    /// <summary>
    /// #4 <b>本文件的核心钉子</b>:占位行与领域状态同事务——事务回滚后一行不剩,重试可以重来。
    /// 把 <c>TryBeginAsync</c> 改成自己开事务(或先 commit 再干活),本条立刻红。
    /// </summary>
    [Fact]
    public async Task Rollback_leaves_no_receipt_behind()
    {
        using var f = new WorkflowAppFactory();
        var (scope, svc, db) = Open(f);
        using var _ = scope;
        var id = Identity(requestKey: "req-rollback");

        var tran = await db.Ado.UseTranAsync(async () =>
        {
            await svc.TryBeginAsync(id);
            throw new InvalidOperationException("业务失败,整事务回滚");
        });

        Assert.False(tran.IsSuccess);
        Assert.Null(await FindAsync(db, id));
    }

    /// <summary>#5 归一化同源:null / 哨兵 / 带空白 命中同一行,入库的是归一化后的值。</summary>
    [Fact]
    public async Task Scope_and_request_key_are_stored_normalized()
    {
        using var f = new WorkflowAppFactory();
        var (scope, svc, db) = Open(f);
        using var _ = scope;

        var withNull = Identity(scope: null, requestKey: "  req-n  ");
        Assert.Null(await svc.TryBeginAsync(withNull));

        var row = await FindAsync(db, withNull);
        Assert.Equal(WfIdentityHash.ScopeSentinel, row.ScopeKey);
        Assert.Equal("req-n", row.RequestKey);

        // 显式哨兵 + 已 trim 的 key => 同一个 identity => 命中同一行
        var withSentinel = Identity(scope: WfIdentityHash.ScopeSentinel, requestKey: "req-n");
        Assert.Equal(withNull.IdentityHash, withSentinel.IdentityHash);
        Assert.NotNull(await svc.TryBeginAsync(withSentinel));
    }

    /// <summary>#6 不串味:仅 RequestKey 不同、或仅动词不同(同意 vs 拒绝)都是两条独立回执。</summary>
    [Fact]
    public async Task Different_request_key_or_verb_do_not_hit_each_other()
    {
        using var f = new WorkflowAppFactory();
        var (scope, svc, _) = Open(f);
        using var __ = scope;

        Assert.Null(await svc.TryBeginAsync(Identity(requestKey: "k-1")));
        Assert.Null(await svc.TryBeginAsync(Identity(requestKey: "k-2")));
        Assert.Null(await svc.TryBeginAsync(Identity(command: WfCommandType.Reject, requestKey: "k-1")));
    }

    /// <summary>
    /// #7 同源钉子:值对象算出的 hash 必须与 <see cref="WfIdentityHash.Compute"/> 逐参数一致 ——
    /// 两条路径一旦分叉,入库诊断列就与唯一键对不上。
    /// </summary>
    [Fact]
    public void Identity_hash_matches_the_raw_algorithm()
    {
        var id = WfOperationIdentity.Create(
            " org-9 ", WfCommandType.Cancel, WfTargetType.Instance, 5L, 6L, " rk ");

        Assert.Equal(
            WfIdentityHash.Compute(" org-9 ", WfCommandType.Cancel, WfTargetType.Instance, 5L, 6L, " rk "),
            id.IdentityHash);
        Assert.Equal("org-9", id.ScopeKey);
        Assert.Equal("rk", id.RequestKey);
    }
}
