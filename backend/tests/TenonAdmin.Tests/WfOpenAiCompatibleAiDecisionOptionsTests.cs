using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>OpenAI-compatible v0 选项的默认安全值与仅启用时 model 必填契约。</summary>
public class WfOpenAiCompatibleAiDecisionOptionsTests
{
    [Fact]
    public void Defaults_are_safe_and_model_is_required_only_when_enabled()
    {
        var options = new OpenAiCompatibleAiDecisionOptions
        {
            ApiKey = "tenon-test-options-api-key",
        };

        Assert.False(options.Enabled);
        Assert.Equal("https://api.openai.com/v1/chat/completions", options.Endpoint);
        Assert.Equal("", options.Model);
        Assert.Equal(30, options.TimeoutSeconds);
        Assert.Equal(64 * 1024, options.ResponseByteCap);
        Assert.False(options.AllowInsecureHttp);
        options.Validate();

        options.Enabled = true;
        var exception = Assert.Throws<ArgumentException>(options.Validate);

        Assert.DoesNotContain("tenon-test-options-api-key", exception.ToString(), StringComparison.Ordinal);
    }
}
