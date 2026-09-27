using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 工作流六个控制器的登录与权限边界。办理、抄送、发起走 [ActiveSession]；
/// 监控、定义、委托、发件箱、加签走 [RolePermission]。成功路径在既有 Wf* 测试里。
/// </summary>
public class WorkflowSurfaceAuthTests
{
    [Fact]
    public async Task Anonymous_workflow_routes_are_401()
    {
        using var f = new WorkflowAppFactory();
        var anon = f.CreateClient();
        foreach (var path in new[]
        {
            "/api/v1/workflow/cc/page",
            "/api/v1/workflow/instance/startable",
            "/api/v1/workflow/instance/monitor",
            "/api/v1/workflow/task/todo",
            "/api/v1/workflow/definition/page",
            "/api/v1/workflow/delegation/page",
            "/api/v1/workflow/outbox/page",
        })
            await AuthProbe.AssertUnauthorized(await anon.GetAsync(path));
    }

    [Fact]
    public async Task Logged_in_user_can_open_own_queues_but_not_admin_routes()
    {
        using var f = new WorkflowAppFactory();
        var limited = await AuthProbe.PingOnly(f);

        foreach (var path in new[]
        {
            "/api/v1/workflow/cc/page",
            "/api/v1/workflow/instance/startable",
            "/api/v1/workflow/task/todo",
            "/api/v1/workflow/task/done",
        })
        {
            var body = await (await limited.GetAsync(path)).ReadEnvelope();
            Assert.Equal(0, body.GetProperty("code").GetInt32());
        }

        foreach (var path in new[]
        {
            "/api/v1/workflow/instance/monitor",
            "/api/v1/workflow/definition/page",
            "/api/v1/workflow/delegation/page",
            "/api/v1/workflow/outbox/page",
        })
            await AuthProbe.AssertForbidden(await limited.GetAsync(path));

        await AuthProbe.AssertForbidden(await limited.PostJson("/api/v1/workflow/task/add-sign", new { }));
        await AuthProbe.AssertForbidden(await limited.PostJson("/api/v1/workflow/task/remove-sign", new { }));
        await AuthProbe.AssertForbidden(await limited.PostJson("/api/v1/workflow/task/take-back", new { }));
    }
}
