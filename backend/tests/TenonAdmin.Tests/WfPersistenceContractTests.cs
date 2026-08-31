using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M2c Task 8「四库持久化契约」。同一套用例经 <see cref="TestDb"/> 在 CI 的
/// sqlite / mysql / postgres / sqlserver 四条腿上各跑一遍。
/// <para><b>本文件只写现有用例覆盖不到的东西</b>。工作流的 245 条测试<b>本来就是四库跑的</b>
/// (<see cref="WorkflowAppFactory"/> 只喂 <c>TestDb.DbType</c>),所以把 CAS / 回滚 / 重放再抄一遍
/// 毫无价值。这里钉三类:</para>
/// <list type="number">
/// <item><b>PG 的事务中止语义</b>——<c>TryBeginAsync</c> 靠「唯一冲突 → 二次 SELECT 认赢家」,而 PG 一旦
/// 语句报错就把整个事务置为 aborted,那次 SELECT 根本执行不了。单库套件永远看不见这条。</item>
/// <item><b>数据库层的方言真相</b>——唯一索引是否真被 CodeFirst 建出来、长文本列的中文往返、列宽满宽、
/// 可空列的旧行读 <c>null</c>、条件 UPDATE 的 affected-rows、<c>DateTime</c> 相等游标。这些都不是业务
/// 逻辑,单靠读代码判断不了,必须在真库上问。</item>
/// <item><b>端到端幂等冒烟(F 段 3 条)</b>——形状与 <see cref="WfReceiptEngineTests"/> 相近,<b>存在理由
/// 不同</b>:SqlServer 的 push/PR 腿跑的是 <c>TEST_FILTER</c> 子集,今天那份名单里<b>一个 <c>Wf*</c> 类都
/// 没有</b>,也就是说一个改动整个工作流包的 PR 在 SqlServer 上零条工作流测试会跑。本类进了那份名单,这 3 条
/// 就是工作流持久化在 SqlServer 上的 PR 时刻信号。<b>别当重复代码删掉。</b></item>
/// </list>
/// <para><b>⚠ 射程声明(两类,都只能在本机 SQLite 腿证实「不假红」,证不了「真钉住」)</b>:
/// ①凡是与 PG 相关的断言,在 SQLite 腿上<b>加不加 savepoint 都是绿的</b>——SQLite/MySQL/SqlServer 的
/// 语句错误不中止事务。那几条的「修前红」只能在 postgres 腿观察到,本机(无 Docker)取不到证。
/// ②B 段两条列宽/列类型断言(<see cref="Result_json_round_trips_chinese_and_long_payloads"/>、
/// <see cref="Scope_key_request_key_and_hash_hold_their_declared_width"/>)在 SQLite 腿上是<b>恒真断言</b>
/// ——SQLite 的类型亲和性不执行列宽/类型约束,把 <c>ResultJson</c> 的声明换成 <c>Length = 200</c> 之后
/// 这两条依旧全绿(Round 24 mutation 实测)。它们真正钉住方言事故(CHANGELOG #26 那类中文变 <c>???</c>、
/// MySQL 非严格模式静默截断)的地方是 mysql / postgres / sqlserver 三腿。<b>别把 SQLite 的绿读成四库的绿</b>
/// ——本文件名字里的「四库」指的是四条 CI 腿都跑这同一套用例,不是「本机就能证明四库都对」。</para>
/// </summary>
public class WfPersistenceContractTests
{
    private const string Password = "Test@123456";

    // ────────────────────────── A. 回执唯一性与 PG 事务中止 ──────────────────────────

    /// <summary>
    /// A1 <b>本 Task 的头等钉子</b>:唯一冲突之后,<c>TryBeginAsync</c> 必须能查到赢家并返回它的结果。
    /// <para>真并发交错构造不出来(单线程下第一次 SELECT 必然读到已提交的赢家),所以只伪造<b>一个</b>前提:
    /// 让第一次 <c>FindAsync</c> 返回 <c>null</c>——现实中这个前提由「查完之后赢家才提交」提供。
    /// 冲突是真的,恢复用的二次 SELECT 也是真的。</para>
    /// <para>PG 上若没有 savepoint,这条会红:整事务已 aborted,二次 SELECT 报 <c>25P02</c>。</para>
    /// </summary>
    [Fact]
    public async Task Unique_violation_recovery_returns_the_winner_receipt()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db, repo) = Open(f);
        using var _ = scope;
        var id = Identity("req-conflict-recovery");

        // 赢家:事务**外**提交,必须是已提交的行,否则另一个事务里的插入会阻塞而不是报冲突。
        var winner = new WfOperationReceiptService(repo);
        Assert.Null(await winner.TryBeginAsync(id));
        await winner.CommitAsync(id, 0, """{"instanceId":4242}""");

        var blinded = new BlindedReceiptService(repo, blindCalls: 1);
        WfOperationReceipt? hit = null;
        var tran = await db.Ado.UseTranAsync(async () => { hit = await blinded.TryBeginAsync(id); });

        Assert.True(tran.IsSuccess, tran.ErrorMessage);
        Assert.NotNull(hit);
        Assert.Equal(id.IdentityHash, hit.IdentityHash);
        Assert.Equal("""{"instanceId":4242}""", hit.ResultJson);
    }

    /// <summary>
    /// A2 冲突且赢家真的查不到时,抛出去的必须是<b>唯一冲突本身</b>,不能被别的异常顶替。
    /// PG 上没有 savepoint 时,二次 SELECT 会抛 <c>current transaction is aborted</c> 并把真因盖掉,
    /// 排查的人只看到「事务中止」,看不到「撞了唯一键」。
    /// </summary>
    [Fact]
    public async Task Unique_violation_without_a_winner_rethrows_the_original_error()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db, repo) = Open(f);
        using var _ = scope;
        var id = Identity("req-conflict-nowinner");

        var winner = new WfOperationReceiptService(repo);
        Assert.Null(await winner.TryBeginAsync(id));
        await winner.CommitAsync(id, 0, "{}");

        // 全程致盲:恢复路径永远查不到赢家 → 必须原样抛出插入时的那个异常。
        var blinded = new BlindedReceiptService(repo, blindCalls: int.MaxValue);
        var tran = await db.Ado.UseTranAsync(async () => { await blinded.TryBeginAsync(id); });

        Assert.False(tran.IsSuccess);
        Assert.NotNull(tran.ErrorException);
        Assert.DoesNotContain("aborted", tran.ErrorException.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A3 唯一性由<b>数据库</b>兜底,不是靠应用层先查一次。绕开服务直插两条同 <c>IdentityHash</c> 的行,
    /// 第二条必须失败——这钉的是 CodeFirst 在四库上都真的把 <c>uk_wf_receipt_identity</c> 建了出来。
    /// </summary>
    [Fact]
    public async Task The_identity_hash_unique_index_is_enforced_by_the_database()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db, _) = Open(f);
        using var __ = scope;
        var hash = Identity("req-unique-index").IdentityHash;

        await db.Insertable(RawReceipt(hash)).ExecuteCommandAsync();

        Exception? failure = null;
        try
        {
            await db.Insertable(RawReceipt(hash)).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.NotNull(failure);
        Assert.Equal(1, await db.Queryable<WfOperationReceipt>().Where(r => r.IdentityHash == hash).CountAsync());
    }

    /// <summary>
    /// A4 冲突恢复<b>不能把事务毁掉</b>:恢复之后同一个事务里继续写的行必须能提交。
    /// PG 上专门验「savepoint 只回滚到点」——若整事务被扔掉,后面这条 INSERT 与提交都做不成。
    /// </summary>
    [Fact]
    public async Task A_recovered_conflict_does_not_poison_the_rest_of_the_transaction()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db, repo) = Open(f);
        using var _ = scope;
        var id = Identity("req-conflict-then-write");
        const long probeInstanceId = 987654321L;

        var winner = new WfOperationReceiptService(repo);
        Assert.Null(await winner.TryBeginAsync(id));
        await winner.CommitAsync(id, 0, "{}");

        var blinded = new BlindedReceiptService(repo, blindCalls: 1);
        WfOperationReceipt? hit = null;
        var tran = await db.Ado.UseTranAsync(async () =>
        {
            hit = await blinded.TryBeginAsync(id);
            await db.Insertable(new WfHistory
            {
                InstanceId = probeInstanceId,
                EventType = WfHistoryEventType.InstanceStarted,
                RequestId = null,
            }).ExecuteCommandAsync();
        });

        Assert.True(tran.IsSuccess, tran.ErrorMessage);
        Assert.NotNull(hit);
        Assert.Equal(1, await db.Queryable<WfHistory>().Where(h => h.InstanceId == probeInstanceId).CountAsync());
    }

    // ────────────────────────── B. 列类型与列宽 ──────────────────────────

    /// <summary>
    /// B1 <c>ResultJson</c>(<c>CodeFirst_BigString</c>)必须原样往返<b>中文</b>与<b>长载荷</b>。
    /// 仓内出过这个事故:裸 <c>text</c> 在 SqlServer 上是非 Unicode,中文写进去读出来是 <c>???</c>
    /// (CHANGELOG #26)。工作流侧此前一条直接断言都没有。
    /// <para><b>射程</b>:本条在 <b>SQLite 腿不具判别力</b>——SQLite 的类型亲和性不管列声明成什么都能
    /// 原样存取任意长度/编码的文本,把 <c>ResultJson</c> 换成 <c>Length = 200</c> 这条依旧全绿
    /// (Round 24 mutation 实测)。真正的钉子在 mysql / postgres / sqlserver 三腿。</para>
    /// </summary>
    [Fact]
    public async Task Result_json_round_trips_chinese_and_long_payloads()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db, repo) = Open(f);
        using var _ = scope;
        var id = Identity("req-bigstring");

        var payload = $$"""{"note":"{{string.Concat(Enumerable.Repeat("流程审批回执中文载荷", 900))}}"}""";
        Assert.True(payload.Length > 8000, "载荷不够长就钉不住长文本列。");

        var svc = new WfOperationReceiptService(repo);
        Assert.Null(await svc.TryBeginAsync(id));
        await svc.CommitAsync(id, 0, payload);

        var row = await db.Queryable<WfOperationReceipt>()
            .Where(r => r.IdentityHash == id.IdentityHash).FirstAsync();
        Assert.Equal(payload, row.ResultJson);
        Assert.DoesNotContain('?', row.ResultJson!);
    }

    /// <summary>
    /// B2 声明成 64 的三列必须真的存得下 64 个字符,不被静默截断。
    /// DTO 侧的「<c>requestId</c> ≤ 64」在 Task 4 已经卡住,列侧的「64 真能装 64」从没验过;
    /// MySQL 非严格模式下截断是<b>静默</b>的,诊断列与 identity 就此对不上。
    /// <para><b>射程</b>:同 B1,本条在 <b>SQLite 腿不具判别力</b>——SQLite 不执行列宽约束,把声明宽度
    /// 改窄这条依旧全绿。真正的钉子在 mysql / postgres / sqlserver 三腿(MySQL 的静默截断正是本条要防的)。</para>
    /// </summary>
    [Fact]
    public async Task Scope_key_request_key_and_hash_hold_their_declared_width()
    {
        using var f = new WorkflowAppFactory();
        var (scope, db, repo) = Open(f);
        using var _ = scope;

        var scopeKey = new string('s', 64);
        var requestKey = new string('r', 64);
        var id = WfOperationIdentity.Create(
            scopeKey, WfCommandType.Approve, WfTargetType.Task, 1001L, 2002L, requestKey);

        Assert.Null(await new WfOperationReceiptService(repo).TryBeginAsync(id));

        var row = await db.Queryable<WfOperationReceipt>()
            .Where(r => r.IdentityHash == id.IdentityHash).FirstAsync();
        Assert.Equal(scopeKey, row.ScopeKey);
        Assert.Equal(requestKey, row.RequestKey);
        Assert.Equal(64, row.IdentityHash.Length);
    }

    // ────────────────────────── C. 可空升级列:旧行读 null ──────────────────────────

    /// <summary>
    /// C1 <c>wf_instance.CompletedTime</c> 是 M2c 新加的可空列(nullable <c>ADD COLUMN</c>,四库都接受)。
    /// 在途实例读回必须是 <c>null</c>——若某方言把它建成 <c>NOT NULL DEFAULT</c>,升级后的旧行会凭空得到
    /// 一个「完结时间」。终态那半边由非空断言做对照,避免「整列没建出来所以恒 null」的假绿。
    /// </summary>
    [Fact]
    public async Task Legacy_instance_rows_read_null_for_completed_time()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pc-ct-starter");
        var aId = await AddUser(admin, "wf-pc-ct-a");
        var definitionId = await Publish(admin, "四库-完结时间", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-pc-ct-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var running = await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instanceId).FirstAsync();
        Assert.Null(running.CompletedTime);

        // 对照组:走到终态就必须有值,否则「恒 null」也能让上面那句绿。
        var a = await ClientFor(f, "wf-pc-ct-a");
        Assert.Equal(0, (await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId }))
            .GetProperty("code").GetInt32());

        var done = await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instanceId).FirstAsync();
        Assert.NotNull(done.CompletedTime);
    }

    /// <summary>
    /// C2 <c>wf_history.RequestId</c> 的 <c>null</c> 必须与空串<b>可区分</b>。语义契约:无请求身份的写入
    /// 一律 <c>null</c>,<b>不是空串</b>。若某方言把二者混同(或列被建成 <c>NOT NULL DEFAULT ''</c>),
    /// 「这一行有没有请求身份」这个问题就永远答不上来。
    /// </summary>
    [Fact]
    public async Task Legacy_history_rows_read_null_for_request_id_not_empty_string()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pc-rid-starter");
        var aId = await AddUser(admin, "wf-pc-rid-a");
        var definitionId = await Publish(admin, "四库-历史请求键", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-pc-rid-starter");
        // 不带 requestId 发起 → 真实写路径落下的就是 null。
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var started = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.InstanceStarted)
            .FirstAsync();
        Assert.Null(started.RequestId);

        // 同一列上显式写空串,读回必须还是空串——两者若被库混同,这里会红。
        await db.Insertable(new WfHistory
        {
            InstanceId = instanceId,
            EventType = WfHistoryEventType.InstanceStarted,
            RequestId = "",
        }).ExecuteCommandAsync();

        var keys = await db.Queryable<WfHistory>()
            .Where(h => h.InstanceId == instanceId && h.EventType == WfHistoryEventType.InstanceStarted)
            .Select(h => h.RequestId).ToListAsync();
        Assert.Contains(keys, k => k is null);
        Assert.Contains(keys, k => k == "");
    }

    // ────────────────────────── D. CAS 与 affected-rows ──────────────────────────

    /// <summary>
    /// D1 M2b 的三处 CAS(实例 / Token / 待办)全都建立在「条件 UPDATE 的返回值」上:命中 <c>1</c>、
    /// 落空 <c>0</c>。而各驱动对 affected-rows 的定义并不一致(MySQL 有 matched 与 changed 两种口径),
    /// 一旦某腿把「匹配到但没改动」算成 0,CAS 就会把胜者误判成败者。这条在真库上逐一问过去。
    /// </summary>
    [Fact]
    public async Task Conditional_updates_report_affected_rows_the_same_on_every_dialect()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pc-cas-starter");
        var aId = await AddUser(admin, "wf-pc-cas-a");
        var definitionId = await Publish(admin, "四库-CAS", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-pc-cas-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var instance = await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instanceId).FirstAsync();
        var token = await db.Queryable<WfToken>().Where(t => t.InstanceId == instanceId).FirstAsync();
        var task = await db.Queryable<WfTask>().Where(t => t.Id == taskId).FirstAsync();

        // 版本对得上 → 1(值真的改变,以免撞上 matched/changed 的口径差异)
        Assert.Equal(1, await db.Updateable<WfInstance>()
            .SetColumns(i => new WfInstance { Version = instance.Version + 1 })
            .Where(i => i.Id == instance.Id && i.Version == instance.Version).ExecuteCommandAsync());
        Assert.Equal(1, await db.Updateable<WfToken>()
            .SetColumns(t => new WfToken { Version = token.Version + 1 })
            .Where(t => t.Id == token.Id && t.Version == token.Version).ExecuteCommandAsync());
        Assert.Equal(1, await db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { Version = task.Version + 1 })
            .Where(t => t.Id == task.Id && t.Version == task.Version).ExecuteCommandAsync());

        // 版本对不上 → 0(上面刚推过版本,旧值必然落空)
        Assert.Equal(0, await db.Updateable<WfInstance>()
            .SetColumns(i => new WfInstance { Version = 999 })
            .Where(i => i.Id == instance.Id && i.Version == instance.Version).ExecuteCommandAsync());
        Assert.Equal(0, await db.Updateable<WfToken>()
            .SetColumns(t => new WfToken { Version = 999 })
            .Where(t => t.Id == token.Id && t.Version == token.Version).ExecuteCommandAsync());
        Assert.Equal(0, await db.Updateable<WfTask>()
            .SetColumns(t => new WfTask { Version = 999 })
            .Where(t => t.Id == task.Id && t.Version == task.Version).ExecuteCommandAsync());
    }

    /// <summary>
    /// D2 超时领取与人工同意<b>只能有一方胜出</b>。让超时先赢,随后到来的人工同意必须收到一个<b>业务错误</b>
    /// (能解析成信封 = 没有变成 500),实例不得被推进第二次。
    /// <para><b>顺序只能是这一个</b>:人工赢在前时,活动的 <c>wf_task</c> 行当场就归档进 <c>wf_his_task</c>
    /// 了,库里已经没有那件待办可以推到期——扫描根本碰不到它,那种写法证明不了仲裁,只能证明「没东西可扫」。
    /// 反过来让超时先动,人工那一侧的落败才是可观测的。</para>
    /// </summary>
    [Fact]
    public async Task A_timeout_claim_and_a_manual_approve_cannot_both_win()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pc-race-starter");
        var aId = await AddUser(admin, "wf-pc-race-a");
        var definitionId = await Publish(
            admin, "四库-超时对人工", SingleApprovalModel(aId, new { hours = 1, action = "autoPass" }));

        var starter = await ClientFor(f, "wf-pc-race-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // 把这件**仍然活动**的待办推到期。hours = 0 在本仓是「不设到期」,不能用。
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            // 时间必须先算成局部变量:写在 SetColumns 的表达式里会被 SqlSugar 当 SQL 翻译,
            // 落进列里的值读回来绑不上 DateTime(与 WfTimeoutTests / WfReceiptEngineTests 同一姿势)。
            var past = DateTime.Now - TimeSpan.FromHours(1);
            var affected = await db.Updateable<WfTask>()
                .SetColumns(t => new WfTask { DueTime = past })
                .Where(t => t.Id == taskId).ExecuteCommandAsync();
            Assert.Equal(1, affected);
        }

        // 超时先赢
        await RunTimeoutJob(f);

        // 人工后到:必须是一个能解析成信封的**业务错误**(不是 500),而且不得再推进一次。
        var a = await ClientFor(f, "wf-pc-race-a");
        var late = await PostEnvelope(a, "/api/v1/workflow/task/approve", new { taskId });
        Assert.NotEqual(0, late.GetProperty("code").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var instance = await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>()
                .Where(i => i.Id == instanceId).FirstAsync();
            Assert.Equal(WfInstanceStatus.Approved, instance.Status);

            // 只推进过一次:已办记录只有超时那一条,人工那次没有再生一条。
            Assert.Equal(1, await db.Queryable<WfHisTask>().Where(h => h.InstanceId == instanceId).CountAsync());
        }
    }

    // ────────────────────────── E. DateTime 相等游标 ──────────────────────────

    /// <summary>
    /// E1 <c>ScanDueTasksAsync</c> 用 <c>(DueTime, Id)</c> 键集翻页,tie-break 是
    /// <c>DueTime == cursor &amp;&amp; Id &gt; afterTaskId</c> —— 一个 <b><c>DateTime</c> 相等比较</b>。
    /// 若某方言的时间列有舍入(SqlServer 的 <c>datetime</c> 是 3.33ms 刻度),相等判定落空,同一
    /// <c>DueTime</c> 的那批待办会被 <c>DueTime &gt; cursor</c> 整批跳过 —— <b>静默漏扫</b>,没有任何报错。
    /// <para>造 5 件 <c>DueTime</c> 完全相同、且「永远推不动」(节点没配超时 → <c>timeoutNotConfigured</c>)
    /// 的待办:这类行只被检视、不占处理预算,于是一拍之内必须靠游标翻完三页。</para>
    /// </summary>
    [Fact]
    public async Task Tasks_sharing_one_due_time_are_all_scanned_across_pages()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pc-cursor-starter");
        var aId = await AddUser(admin, "wf-pc-cursor-a");
        // 刻意**不配** timeout:扫到就是死行,只检视不占预算,逼出翻页。
        var definitionId = await Publish(admin, "四库-同到期游标", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-pc-cursor-starter");
        var instanceIds = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
            Assert.Equal(0, start.GetProperty("code").GetInt32());
            instanceIds.Add(start.GetProperty("data").GetProperty("instanceId").GetInt64());
        }

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            // 5 件待办共用**同一个** DueTime —— 相等游标的用武之地。
            var due = DateTime.Now - TimeSpan.FromHours(1);
            var affected = await db.Updateable<WfTask>()
                .SetColumns(t => new WfTask { DueTime = due })
                .Where(t => instanceIds.Contains(t.InstanceId)).ExecuteCommandAsync();
            Assert.Equal(5, affected);

            // 页大小调到 2 → 5 行必须翻三页才看得完。
            scope.ServiceProvider.GetRequiredService<WorkflowOptions>().TimeoutScanBatchSize = 2;
        }

        var log = await RunTimeoutJob(f);

        Assert.Contains(log, line => line.Contains("命中 5", StringComparison.Ordinal));
    }

    // ───────────── F. 端到端幂等冒烟(为 SqlServer PR 腿而设,别当重复删掉) ─────────────

    /// <summary>
    /// F1 同一个 <c>requestId</c> 串行发两次发起 → 同一个 <c>instanceId</c>,且只建一件待办。
    /// <para><b>为什么与 <see cref="WfReceiptEngineTests"/> 形状相近却仍要有</b>:SqlServer 的 push/PR 腿
    /// 只跑 <c>TEST_FILTER</c> 列出的类,那份名单里没有任何 <c>Wf*</c>。本类在名单里,所以这是工作流幂等
    /// 在 SqlServer 上唯一的 PR 时刻信号。</para>
    /// </summary>
    [Fact]
    public async Task Replaying_one_request_id_returns_the_first_result()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pc-replay-starter");
        var aId = await AddUser(admin, "wf-pc-replay-a");
        var definitionId = await Publish(admin, "四库-重放", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-pc-replay-starter");
        var body = new { definitionId, requestId = "pc-replay-001" };

        var first = await PostEnvelope(starter, "/api/v1/workflow/instance/start", body);
        var second = await PostEnvelope(starter, "/api/v1/workflow/instance/start", body);

        Assert.Equal(0, first.GetProperty("code").GetInt32());
        Assert.Equal(0, second.GetProperty("code").GetInt32());
        var instanceId = first.GetProperty("data").GetProperty("instanceId").GetInt64();
        Assert.Equal(instanceId, second.GetProperty("data").GetProperty("instanceId").GetInt64());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.Equal(1, await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>().CountAsync());
        Assert.Equal(1, await db.Queryable<WfTask>().Where(t => t.InstanceId == instanceId).CountAsync());
    }

    /// <summary>
    /// F2 终态实例上重放同一个 <c>requestId</c>:拿回<b>第一次</b>的结果,不再二次推进。
    /// (存在理由同 <see cref="Replaying_one_request_id_returns_the_first_result"/>。)
    /// </summary>
    [Fact]
    public async Task Replaying_against_a_terminal_instance_still_returns_the_first_result()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pc-terminal-starter");
        var aId = await AddUser(admin, "wf-pc-terminal-a");
        var definitionId = await Publish(admin, "四库-终态重放", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-pc-terminal-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var instanceId = start.GetProperty("data").GetProperty("instanceId").GetInt64();
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        var a = await ClientFor(f, "wf-pc-terminal-a");
        var body = new { taskId, requestId = "pc-terminal-001" };
        var first = await PostEnvelope(a, "/api/v1/workflow/task/approve", body);
        Assert.Equal(0, first.GetProperty("code").GetInt32());

        // 实例此刻已终态:没有回执的话,这一次重发只会撞 TaskConflict。
        var second = await PostEnvelope(a, "/api/v1/workflow/task/approve", body);
        Assert.Equal(0, second.GetProperty("code").GetInt32());
        Assert.Equal(
            first.GetProperty("data").GetProperty("instanceId").GetInt64(),
            second.GetProperty("data").GetProperty("instanceId").GetInt64());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.Equal(WfInstanceStatus.Approved, await db.Queryable<WfInstance>().ClearFilter<IOrgScoped>()
            .Where(i => i.Id == instanceId).Select(i => i.Status).FirstAsync());
        Assert.Equal(1, await db.Queryable<WfHisTask>().Where(h => h.InstanceId == instanceId).CountAsync());
    }

    /// <summary>
    /// F3 业务失败 → 整事务回滚 → <b>回执一行都不留</b>,重试仍报同一个业务码而不是被幂等成「成功」。
    /// (存在理由同 <see cref="Replaying_one_request_id_returns_the_first_result"/>。)
    /// </summary>
    [Fact]
    public async Task A_failed_command_leaves_no_receipt_behind()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");
        await AddUser(admin, "wf-pc-fail-starter");
        var aId = await AddUser(admin, "wf-pc-fail-a");
        await AddUser(admin, "wf-pc-fail-b");
        var definitionId = await Publish(admin, "四库-业务失败", SingleApprovalModel(aId));

        var starter = await ClientFor(f, "wf-pc-fail-starter");
        var start = await PostEnvelope(starter, "/api/v1/workflow/instance/start", new { definitionId });
        var taskId = start.GetProperty("data").GetProperty("createdTaskId").GetInt64();

        // b 不是这件待办的处理人 → 业务失败。
        var b = await ClientFor(f, "wf-pc-fail-b");
        var body = new { taskId, requestId = "pc-fail-001" };
        var failed = await PostEnvelope(b, "/api/v1/workflow/task/approve", body);
        Assert.NotEqual(0, failed.GetProperty("code").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            Assert.Equal(0, await db.Queryable<WfOperationReceipt>().CountAsync());
        }

        // 同 key 再发:仍是同一个业务码,没有被「幂等」成功。
        var again = await PostEnvelope(b, "/api/v1/workflow/task/approve", body);
        Assert.Equal(failed.GetProperty("code").GetInt32(), again.GetProperty("code").GetInt32());
    }

    // ────────────────────────── 测试替身与脚手架 ──────────────────────────

    /// <summary>
    /// 只把<b>头几次</b> <see cref="WfOperationReceiptService.FindAsync"/> 蒙住(返回 <c>null</c>),
    /// 其余照常查库。伪造的仅仅是「这一次没查到」这个前提——现实里它由「查完之后赢家才提交」提供;
    /// 之后的唯一冲突与恢复查询都是真的。
    /// </summary>
    private sealed class BlindedReceiptService(IRepository<WfOperationReceipt> receipts, int blindCalls)
        : WfOperationReceiptService(receipts)
    {
        private int calls;

        protected override Task<WfOperationReceipt?> FindAsync(
            string identityHash,
            CancellationToken cancellationToken)
        {
            calls++;
            return calls <= blindCalls
                ? Task.FromResult<WfOperationReceipt?>(null)
                : base.FindAsync(identityHash, cancellationToken);
        }
    }

    private static WfOperationIdentity Identity(string requestKey) =>
        WfOperationIdentity.Create("org-1", WfCommandType.Approve, WfTargetType.Task, 1001L, 2002L, requestKey);

    /// <summary>绕开服务直接构造的回执行(用于测数据库自己的约束)。</summary>
    private static WfOperationReceipt RawReceipt(string identityHash) => new()
    {
        ScopeKey = "org-1",
        CommandType = WfCommandType.Approve,
        TargetType = WfTargetType.Task,
        TargetId = 1001L,
        ActorUserId = 2002L,
        RequestKey = "raw",
        IdentityHash = identityHash,
    };

    /// <summary>宿主起来 + 建表;返回作用域、SqlSugar 单例与回执仓储。</summary>
    private static (IServiceScope Scope, ISqlSugarClient Db, IRepository<WfOperationReceipt> Repo) Open(
        WorkflowAppFactory f)
    {
        _ = f.CreateClient(); // 触发宿主启动与 CodeFirst 建表
        var scope = f.Services.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<ISqlSugarClient>(),
            scope.ServiceProvider.GetRequiredService<IRepository<WfOperationReceipt>>());
    }

    /// <summary>手动触发一次超时扫描并收下它的日志行(不启调度器,与 <c>WfTimeoutTests</c> 同一姿势)。</summary>
    private static async Task<List<string>> RunTimeoutJob(WorkflowAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var job = scope.ServiceProvider.GetServices<IAdminJob>().OfType<WfTimeoutJob>().Single();
        var lines = new List<string>();
        var now = DateTime.Now;
        await job.ExecuteAsync(
            new JobExecutionContext
            {
                JobId = 1,
                JobCode = "wf-timeout-scan",
                JobName = "流程超时扫描",
                FireInstanceId = 1,
                ScheduledTime = now,
                FireTime = now,
                Log = lines.Add,
            },
            CancellationToken.None);
        return lines;
    }

    /// <summary>start → node1(any,[user],可选 timeout) → null。</summary>
    private static object SingleApprovalModel(long userId, object? timeout = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["assignee"] = new
            {
                provider = "user",
                @params = new Dictionary<string, object> { ["userIds"] = new[] { userId } },
            },
            ["mode"] = "any",
        };
        if (timeout is not null)
            props["timeout"] = timeout;

        return new
        {
            version = 1,
            root = new
            {
                id = "start",
                type = "start",
                name = "",
                next = new { id = "node1", type = "approval", name = "node1", props, next = (object?)null },
            },
        };
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.LoginToken(account, Password));
        return client;
    }

    private static async Task<long> AddUser(HttpClient admin, string account)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = 1,
            ["roleIds"] = new[] { 2L },
        };
        var env = await PostEnvelope(admin, "/api/v1/sys/user", body);
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<long> Publish(HttpClient admin, string name, object model)
    {
        var added = await PostEnvelope(admin, "/api/v1/workflow/definition/add", new { name, model });
        Assert.Equal(0, added.GetProperty("code").GetInt32());
        var id = added.GetProperty("data").GetInt64();
        var published = await PostEnvelope(admin, "/api/v1/workflow/definition/publish", new { id });
        Assert.Equal(0, published.GetProperty("code").GetInt32());
        return id;
    }

    private static async Task<JsonElement> PostEnvelope(HttpClient client, string path, object body) =>
        await (await client.PostJson(path, body)).ReadEnvelope();
}
