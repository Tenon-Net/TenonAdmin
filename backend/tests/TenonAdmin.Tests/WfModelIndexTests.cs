using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WfModelIndex"/> 纯单测——不起宿主,手搭 <see cref="WfModel"/> 对象树。
/// 覆盖主链/臂内查找、封闭 branch 查找、单层与嵌套分支的汇合、以及 Conditions 为 null/空数组不抛。
/// </summary>
public class WfModelIndexTests
{
    private static WfNode Node(
        string id,
        WfNodeType type = WfNodeType.Approval,
        WfNode? next = null,
        List<WfBranchArm>? conditions = null) => new()
    {
        Id = id,
        Type = type,
        Name = id,
        Next = next,
        Conditions = conditions,
    };

    private static WfBranchArm Arm(string id, WfNode? next = null, bool isDefault = false) => new()
    {
        Id = id,
        Name = id,
        Next = next,
        IsDefault = isDefault,
    };

    [Fact]
    public void Find_locates_main_chain_node()
    {
        var merge = Node("merge");
        var branch = Node("branch1", WfNodeType.Branch, next: merge,
            conditions: [Arm("armA", Node("armA-1")), Arm("default", isDefault: true)]);
        var root = Node("start", WfNodeType.Start, next: branch);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Same(root, index.Find("start"));
        Assert.Same(branch, index.Find("branch1"));
        Assert.Same(merge, index.Find("merge"));
    }

    [Fact]
    public void Find_locates_node_inside_branch_arm()
    {
        var armNode = Node("armA-1");
        var branch = Node("branch1", WfNodeType.Branch,
            conditions: [Arm("armA", armNode), Arm("default", isDefault: true)]);
        var root = Node("start", WfNodeType.Start, next: branch);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Same(armNode, index.Find("armA-1"));
    }

    [Fact]
    public void FindEnclosingBranch_returns_branch_for_node_inside_arm()
    {
        var armNode = Node("armA-1");
        var branch = Node("branch1", WfNodeType.Branch,
            conditions: [Arm("armA", armNode), Arm("default", isDefault: true)]);
        var root = Node("start", WfNodeType.Start, next: branch);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Same(branch, index.FindEnclosingBranch("armA-1"));
    }

    [Fact]
    public void FindEnclosingBranch_returns_null_for_main_chain_node()
    {
        var branch = Node("branch1", WfNodeType.Branch,
            conditions: [Arm("armA", Node("armA-1")), Arm("default", isDefault: true)]);
        var root = Node("start", WfNodeType.Start, next: branch);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Null(index.FindEnclosingBranch("start"));
        Assert.Null(index.FindEnclosingBranch("branch1"));
    }

    [Fact]
    public void ResolveMergeTarget_returns_branch_next_at_arm_end()
    {
        var merge = Node("merge");
        var armEnd = Node("armA-1");
        var branch = Node("branch1", WfNodeType.Branch, next: merge,
            conditions: [Arm("armA", armEnd), Arm("default", isDefault: true)]);
        var root = Node("start", WfNodeType.Start, next: branch);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Same(merge, index.ResolveMergeTarget(armEnd));
    }

    [Fact]
    public void ResolveMergeTarget_climbs_to_outer_branch_when_nested_branch_has_no_next()
    {
        var outerMerge = Node("outer-merge");
        var innerArmEnd = Node("inner-armC-1");
        var innerBranch = Node("inner-branch", WfNodeType.Branch, next: null,
            conditions: [Arm("innerArmC", innerArmEnd), Arm("inner-default", isDefault: true)]);
        var outerBranch = Node("outer-branch", WfNodeType.Branch, next: outerMerge,
            conditions: [Arm("outerArmB", innerBranch), Arm("outer-default", isDefault: true)]);
        var root = Node("start", WfNodeType.Start, next: outerBranch);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Same(outerMerge, index.ResolveMergeTarget(innerArmEnd));
    }

    [Fact]
    public void ResolveMergeTarget_returns_null_when_no_merge_all_the_way_up()
    {
        var innerArmEnd = Node("inner-armC-1");
        var innerBranch = Node("inner-branch", WfNodeType.Branch, next: null,
            conditions: [Arm("innerArmC", innerArmEnd), Arm("inner-default", isDefault: true)]);
        var outerBranch = Node("outer-branch", WfNodeType.Branch, next: null,
            conditions: [Arm("outerArmB", innerBranch), Arm("outer-default", isDefault: true)]);
        var root = Node("start", WfNodeType.Start, next: outerBranch);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Null(index.ResolveMergeTarget(innerArmEnd));
    }

    [Fact]
    public void Nodes_includes_node_inside_branch_arm()
    {
        var armNode = Node("armA-1");
        var branch = Node("branch1", WfNodeType.Branch,
            conditions: [Arm("armA", armNode), Arm("default", isDefault: true)]);
        var root = Node("start", WfNodeType.Start, next: branch);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Contains(index.Nodes, n => ReferenceEquals(n, armNode));
    }

    [Fact]
    public void Build_does_not_throw_on_null_or_empty_conditions()
    {
        var branchWithNullConditions = Node("branch-null", WfNodeType.Branch, next: null, conditions: null);
        var branchWithEmptyConditions = Node("branch-empty", WfNodeType.Branch, next: branchWithNullConditions,
            conditions: []);
        var root = Node("start", WfNodeType.Start, next: branchWithEmptyConditions);
        var model = new WfModel { Root = root };

        var index = WfModelIndex.Build(model);

        Assert.Same(branchWithEmptyConditions, index.Find("branch-empty"));
        Assert.Same(branchWithNullConditions, index.Find("branch-null"));
    }
}
