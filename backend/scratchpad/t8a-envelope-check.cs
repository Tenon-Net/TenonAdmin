// T8a 自检:ResultEnvelopeFilter.TryWrap —— 只包成功裸 ObjectResult,已信封/File/StatusCode/错误结果放行。
// 运行:dotnet run t8a-envelope-check.cs
#:project ../src/TenonAdmin.AspNetCore/TenonAdmin.AspNetCore.csproj
#:property PublishAot=false

using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

int passed = 0, total = 0;
void Check(string name, bool ok)
{
    total++;
    if (ok) { passed++; Console.WriteLine($"  ✓ {name}"); }
    else Console.WriteLine($"  ✗ {name}  <<< 失败");
}

// 1) 裸 DTO(StatusCode 未定)→ 包,且 Code=0、Data 是原值
var dto = new { Name = "alice" };
Check("裸 ObjectResult 被包", ResultEnvelopeFilter.TryWrap(new ObjectResult(dto), out var w1)
    && w1.Value is Result<object?> { Code: 0 } r && ReferenceEquals(r.Data, dto));

// 2) OkObjectResult(StatusCode=200)→ 仍属成功,照包
Check("200 OkObjectResult 被包", ResultEnvelopeFilter.TryWrap(new OkObjectResult(dto), out _));

// 3) 已是信封 → 不重复包
Check("已是 Result<T> 不重复包", !ResultEnvelopeFilter.TryWrap(new ObjectResult(Result<string>.Ok("x")), out _));

// 4) 错误结果(400 校验/ProblemDetails)→ 不包
Check("400 BadRequest 不包", !ResultEnvelopeFilter.TryWrap(new BadRequestObjectResult(new { error = "bad" }), out _));

// 5) 非 ObjectResult:File / StatusCode / Empty → 不包
Check("FileContentResult 不包", !ResultEnvelopeFilter.TryWrap(new FileContentResult([1, 2, 3], "application/octet-stream"), out _));
Check("StatusCodeResult(204) 不包", !ResultEnvelopeFilter.TryWrap(new StatusCodeResult(204), out _));
Check("EmptyResult 不包", !ResultEnvelopeFilter.TryWrap(new EmptyResult(), out _));

// 6) 值为 null 的 200 → 仍包成 Result.Ok(null)
Check("null 值 200 被包", ResultEnvelopeFilter.TryWrap(new ObjectResult(null) { StatusCode = 200 }, out var w6)
    && w6.Value is Result<object?> { Code: 0, Data: null });

Console.WriteLine($"\n结果:{passed}/{total} 通过");
if (passed != total) Environment.Exit(1);
