using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>T18 并行 token 与臂表的 CodeFirst 持久化契约。</summary>
public class WfParallelPersistenceContractTests
{
    [Fact]
    public async Task Legacy_token_rows_keep_parallel_columns_null()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var token = new WfToken { InstanceId = 1001, NodeId = "approval", Status = WfTokenStatus.Active };
        await db.Insertable(token).ExecuteCommandAsync();

        var loaded = await db.Queryable<WfToken>().InSingleAsync(token.Id);
        Assert.Null(loaded.ParentTokenId);
        Assert.Null(loaded.ForkId);
        Assert.Null(loaded.PendingArmCount);

        var columns = db.DbMaintenance.GetColumnInfosByTableName("wf_token", false)
            .ToDictionary(x => x.DbColumnName, StringComparer.OrdinalIgnoreCase);
        Assert.True(columns[nameof(WfToken.ParentTokenId)].IsNullable);
        Assert.True(columns[nameof(WfToken.ForkId)].IsNullable);
        Assert.True(columns[nameof(WfToken.PendingArmCount)].IsNullable);
    }

    [Fact]
    public async Task Parallel_arm_identity_is_unique_and_nullable_child_fields_round_trip()
    {
        using var factory = new WorkflowAppFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var arm = new WfParallelArm
        {
            ForkId = 2001,
            ArmId = "empty-arm",
            ParentTokenId = 3001,
            Status = WfParallelArmStatus.Completed,
            Version = 2,
            ParentNodeVisitId = 4001,
            Reason = "空臂直接完成",
        };
        await db.Insertable(arm).ExecuteCommandAsync();
        await db.Insertable(new WfParallelArm
        {
            ForkId = arm.ForkId,
            ArmId = "other-arm",
            ParentTokenId = arm.ParentTokenId,
            ChildTokenId = 3002,
            Status = WfParallelArmStatus.Active,
            ParentNodeVisitId = arm.ParentNodeVisitId,
            ChildEntryNodeVisitId = 4002,
        }).ExecuteCommandAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => db.Insertable(arm).ExecuteCommandAsync());

        var loaded = await db.Queryable<WfParallelArm>()
            .Where(x => x.ForkId == arm.ForkId && x.ArmId == arm.ArmId)
            .FirstAsync();
        Assert.Null(loaded.ChildTokenId);
        Assert.Null(loaded.ChildEntryNodeVisitId);
        Assert.Equal("空臂直接完成", loaded.Reason);
        Assert.Equal(WfParallelArmStatus.Completed, loaded.Status);
        Assert.Equal(2, loaded.Version);
        Assert.Equal(2, await db.Queryable<WfParallelArm>().Where(x => x.ForkId == arm.ForkId).CountAsync());
    }

    [Fact]
    public void Parallel_arm_declares_composite_primary_key_and_parent_status_index()
    {
        foreach (var propertyName in new[] { nameof(WfParallelArm.ForkId), nameof(WfParallelArm.ArmId) })
        {
            var column = typeof(WfParallelArm).GetProperty(propertyName)!
                .GetCustomAttribute<SugarColumn>();
            Assert.NotNull(column);
            Assert.True(column.IsPrimaryKey);
        }

        var indexes = typeof(WfParallelArm).GetCustomAttributesData()
            .Where(x => x.AttributeType == typeof(SugarIndexAttribute));
        var parentStatus = Assert.Single(indexes, x =>
            Equals(x.ConstructorArguments[0].Value, "idx_wf_parallel_arm_parent_status"));
        var fields = parentStatus.ConstructorArguments.Select(x => x.Value as string).OfType<string>().Skip(1);
        Assert.Equal([nameof(WfParallelArm.ParentTokenId), nameof(WfParallelArm.Status)], fields);
    }
}
