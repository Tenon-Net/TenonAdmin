using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>钉 <c>props.buttonLabels</c> 经 <see cref="WfModelJson"/> camelCase 往返。</summary>
public class WfButtonLabelsTests
{
    [Fact]
    public void WfButtonLabels_round_trips_through_WfModelJson()
    {
        var model = new WfModel
        {
            Root = new WfNode
            {
                Id = "start",
                Type = WfNodeType.Start,
                Name = "发起",
                Next = new WfNode
                {
                    Id = "ap1",
                    Type = WfNodeType.Approval,
                    Name = "审批",
                    Props = new WfNodeProps
                    {
                        ReturnPolicy = WfReturnPolicy.Prev,
                        ButtonLabels = new WfButtonLabels
                        {
                            Approve = "准了",
                            Reject = "驳回",
                            Return = "打回",
                            Transfer = "转给",
                            Delegate = "代办",
                            Urge = "催一下",
                        },
                    },
                },
            },
        };

        var json = WfModelJson.Serialize(model);
        Assert.Contains("\"buttonLabels\"", json, StringComparison.Ordinal);
        Assert.Contains("\"approve\"", json, StringComparison.Ordinal);
        Assert.Contains("\"returnPolicy\":\"prev\"", json, StringComparison.Ordinal);

        var round = WfModelJson.Deserialize(json);
        Assert.NotNull(round?.Root.Next?.Props?.ButtonLabels);
        var labels = round!.Root.Next!.Props!.ButtonLabels!;
        Assert.Equal("准了", labels.Approve);
        Assert.Equal("驳回", labels.Reject);
        Assert.Equal("打回", labels.Return);
        Assert.Equal("转给", labels.Transfer);
        Assert.Equal("代办", labels.Delegate);
        Assert.Equal("催一下", labels.Urge);
        Assert.Equal(WfReturnPolicy.Prev, round.Root.Next.Props.ReturnPolicy);
    }
}
