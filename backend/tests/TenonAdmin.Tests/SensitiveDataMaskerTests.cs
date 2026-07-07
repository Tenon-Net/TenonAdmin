using TenonAdmin.AspNetCore;

namespace TenonAdmin.Tests;

/// <summary>入参脱敏(§14)——由 scratchpad t6-mask-check 转正。按字段名递归打码、不可序列化兜底。</summary>
public class SensitiveDataMaskerTests
{
    private static Dictionary<string, object?> Args(object? v) => new() { ["input"] = v };

    [Fact]
    public void Masks_top_level_sensitive_keeps_others()
    {
        var r = SensitiveDataMasker.Mask(Args(new { Account = "admin", Password = "secret123" }));
        Assert.Contains("***", r);
        Assert.DoesNotContain("secret123", r);
        Assert.Contains("admin", r);
    }

    [Fact]
    public void Masks_nested_object_and_array_elements()
    {
        var r = SensitiveDataMasker.Mask(Args(new
        {
            Detail = new { AccessSecret = "topsecret", Public = "visible" },
            Items = new object[] { new { Token = "tok123" }, new { Name = "keep" } },
        }));
        Assert.DoesNotContain("topsecret", r);
        Assert.Contains("visible", r);
        Assert.DoesNotContain("tok123", r);
        Assert.Contains("keep", r);
    }

    [Theory]
    [InlineData("newPassword", "np1")]
    [InlineData("access_token", "at1")]
    [InlineData("clientSecret", "cs1")]
    public void Masks_naming_variants(string key, string value)
    {
        var r = SensitiveDataMasker.Mask(new Dictionary<string, object?> { [key] = value, ["userName"] = "alice" });
        Assert.DoesNotContain(value, r);
        Assert.Contains("alice", r);
    }

    [Fact]
    public void Unserializable_input_falls_back_to_placeholder()
    {
        var node = new Cyclic { Name = "a" };
        node.Child = node;   // 循环引用 → STJ 抛 → 走兜底
        Assert.Equal("<unserializable>", SensitiveDataMasker.Mask(Args(node)));
    }

    private sealed class Cyclic { public string Name { get; set; } = ""; public Cyclic? Child { get; set; } }
}
