using System.Net.Http.Headers;
using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// M1 验收线:请假流程「定义发布 → 发起 → 主管审批 → 完结」HTTP 端到端。
/// 定义 fixture 写在测试内(不扩菜单种子);审批人走内置 <c>leader</c> Provider。
/// </summary>
public class LeaveWorkflowE2ETests
{
    private const string Password = "Test@123456";

    [Fact]
    public async Task Leave_start_approve_completes()
    {
        using var f = new WorkflowAppFactory();
        var admin = await ClientFor(f, "superAdmin");

        // 主管 + 下属(DirectorId → leader Provider)。
        // 挂 org=1 + 种子角色「全部数据」(Id=2):否则 DataEntity 定义对无范围用户不可见 → 发起 48011。
        const long orgId = 1;
        const long allScopeRoleId = 2;
        var managerId = await AddUser(admin, "leave-mgr", orgId, allScopeRoleId);
        var employeeId = await AddUser(admin, "leave-emp", orgId, allScopeRoleId, directorId: managerId);
        Assert.NotEqual(0, managerId);
        Assert.NotEqual(0, employeeId);

        // 建「请假审批」草稿并发布(串行:发起人 → 部门审批)
        var defId = await PublishLeaveDefinition(admin);
        Assert.True(defId > 0);

        // 员工发起
        var emp = await ClientFor(f, "leave-emp");
        var start = await (await emp.PostJson("/api/v1/workflow/instance/start", new
        {
            definitionId = defId,
            businessKey = "LEAVE-001",
            variablesJson = """{"days":3,"type":"annual"}""",
        })).ReadEnvelope();
        Assert.Equal(0, start.GetProperty("code").GetInt32());
        var startData = start.GetProperty("data");
        var instanceId = startData.GetProperty("instanceId").GetInt64();
        Assert.Equal((int)WfInstanceStatus.Running, startData.GetProperty("instanceStatus").GetInt32());
        Assert.True(startData.GetProperty("createdTaskId").GetInt64() > 0);
        Assert.Contains(managerId, startData.GetProperty("newAssigneeUserIds").EnumerateArray().Select(e => e.GetInt64()));

        // 主管待办含该单
        var mgr = await ClientFor(f, "leave-mgr");
        var todo = await (await mgr.GetAsync("/api/v1/workflow/task/todo?Current=1&Size=20")).ReadEnvelope();
        Assert.Equal(0, todo.GetProperty("code").GetInt32());
        var todoItem = todo.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("instanceId").GetInt64() == instanceId);
        var taskId = todoItem.GetProperty("taskId").GetInt64();
        Assert.Equal("部门审批", todoItem.GetProperty("nodeName").GetString());
        Assert.Equal("LEAVE-001", todoItem.GetProperty("businessKey").GetString());

        // 同意 → 完结
        var approve = await (await mgr.PostJson("/api/v1/workflow/task/approve", new
        {
            taskId,
            comment = "同意请假",
        })).ReadEnvelope();
        Assert.Equal(0, approve.GetProperty("code").GetInt32());
        Assert.Equal((int)WfInstanceStatus.Approved, approve.GetProperty("data").GetProperty("instanceStatus").GetInt32());

        // 详情:Approved + 意见时间线
        var detail = await (await emp.GetAsync($"/api/v1/workflow/instance/{instanceId}")).ReadEnvelope();
        Assert.Equal(0, detail.GetProperty("code").GetInt32());
        var detailData = detail.GetProperty("data");
        Assert.Equal((int)WfInstanceStatus.Approved, detailData.GetProperty("status").GetInt32());
        Assert.Equal("LEAVE-001", detailData.GetProperty("businessKey").GetString());
        Assert.True(detailData.TryGetProperty("myPendingTask", out var pending) &&
                    pending.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
        var his = detailData.GetProperty("hisTasks").EnumerateArray().ToList();
        Assert.Contains(his, h =>
            h.GetProperty("action").GetInt32() == (int)WfTaskAction.Approve &&
            h.GetProperty("userId").GetInt64() == managerId &&
            h.GetProperty("comment").GetString() == "同意请假");

        // 事件流含完结
        var history = await (await emp.GetAsync($"/api/v1/workflow/instance/history/{instanceId}")).ReadEnvelope();
        Assert.Equal(0, history.GetProperty("code").GetInt32());
        Assert.Contains(
            history.GetProperty("data").EnumerateArray(),
            e => e.GetProperty("eventType").GetInt32() == (int)WfHistoryEventType.InstanceCompleted);

        // 主管待办已清空该单;已办可见
        var todoAfter = await (await mgr.GetAsync("/api/v1/workflow/task/todo?Current=1&Size=20")).ReadEnvelope();
        Assert.DoesNotContain(
            todoAfter.GetProperty("data").GetProperty("items").EnumerateArray(),
            i => i.GetProperty("instanceId").GetInt64() == instanceId);
        var done = await (await mgr.GetAsync("/api/v1/workflow/task/done?Current=1&Size=20")).ReadEnvelope();
        Assert.Contains(
            done.GetProperty("data").GetProperty("items").EnumerateArray(),
            i => i.GetProperty("instanceId").GetInt64() == instanceId &&
                 i.GetProperty("action").GetInt32() == (int)WfTaskAction.Approve);
    }

    /// <summary>设计草案样例串行链:start → approval(leader/level=1)。</summary>
    private static async Task<long> PublishLeaveDefinition(HttpClient admin)
    {
        var add = await (await admin.PostJson("/api/v1/workflow/definition/add", new
        {
            name = "请假审批",
            groupName = "人事",
            model = new
            {
                version = 1,
                root = new
                {
                    id = "n1",
                    type = "start",
                    name = "发起人",
                    props = new { initiatorScope = Array.Empty<object>() },
                    next = new
                    {
                        id = "n2",
                        type = "approval",
                        name = "部门审批",
                        props = new
                        {
                            assignee = new
                            {
                                provider = "leader",
                                @params = new Dictionary<string, object> { ["level"] = 1 },
                            },
                            mode = "any",
                            formPerms = Array.Empty<object>(),
                        },
                        next = (object?)null,
                    },
                },
                formComponent = "views/biz/leave/form",
            },
        })).ReadEnvelope();
        Assert.Equal(0, add.GetProperty("code").GetInt32());
        var defId = add.GetProperty("data").GetInt64();

        var pub = await (await admin.PostJson("/api/v1/workflow/definition/publish", new { id = defId }))
            .ReadEnvelope();
        Assert.Equal(0, pub.GetProperty("code").GetInt32());
        Assert.True(pub.GetProperty("data").GetInt32() >= 1);
        return defId;
    }

    private static async Task<HttpClient> ClientFor(WorkflowAppFactory f, string account)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, Password));
        return c;
    }

    private static async Task<long> AddUser(
        HttpClient admin,
        string account,
        long orgId,
        long roleId,
        long? directorId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["account"] = account,
            ["password"] = Password,
            ["name"] = account,
            ["enabled"] = true,
            ["orgId"] = orgId,
            ["roleIds"] = new[] { roleId },
        };
        if (directorId is long did)
            body["directorId"] = did;

        var env = await (await admin.PostJson("/api/v1/sys/user", body)).ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }
}
