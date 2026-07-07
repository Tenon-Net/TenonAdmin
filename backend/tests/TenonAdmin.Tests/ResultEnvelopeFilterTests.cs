using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>统一返回兜底过滤器(§12)——由 scratchpad t8a-envelope-check 转正。只包成功裸 ObjectResult。</summary>
public class ResultEnvelopeFilterTests
{
    [Fact]
    public void Wraps_bare_object_result_as_result_envelope()
    {
        var dto = new { Name = "alice" };
        Assert.True(ResultEnvelopeFilter.TryWrap(new ObjectResult(dto), out var w));
        var env = Assert.IsType<Result<object?>>(w.Value);
        Assert.Equal(0, env.Code);
        Assert.Same(dto, env.Data);
    }

    [Fact]
    public void Wraps_200_ok_object_result() =>
        Assert.True(ResultEnvelopeFilter.TryWrap(new OkObjectResult(new { X = 1 }), out _));

    [Fact]
    public void Does_not_double_wrap_existing_envelope() =>
        Assert.False(ResultEnvelopeFilter.TryWrap(new ObjectResult(Result<string>.Ok("x")), out _));

    [Fact]
    public void Does_not_wrap_error_result() =>
        Assert.False(ResultEnvelopeFilter.TryWrap(new BadRequestObjectResult(new { error = "bad" }), out _));

    [Fact]
    public void Does_not_wrap_non_object_results()
    {
        Assert.False(ResultEnvelopeFilter.TryWrap(new FileContentResult([1, 2, 3], "application/octet-stream"), out _));
        Assert.False(ResultEnvelopeFilter.TryWrap(new StatusCodeResult(204), out _));
        Assert.False(ResultEnvelopeFilter.TryWrap(new EmptyResult(), out _));
    }

    [Fact]
    public void Wraps_null_value_success()
    {
        Assert.True(ResultEnvelopeFilter.TryWrap(new ObjectResult(null) { StatusCode = 200 }, out var w));
        var env = Assert.IsType<Result<object?>>(w.Value);
        Assert.Equal(0, env.Code);
        Assert.Null(env.Data);
    }
}
