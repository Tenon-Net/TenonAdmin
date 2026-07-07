// T6 自检:入参脱敏 SensitiveDataMasker —— 按字段名打码、递归嵌套/数组、不可序列化兜底。
// 运行:dotnet run t6-mask-check.cs
// 引用 AspNetCore 项目(内部经 SqlSugar 传递依赖)→ 必须关 AOT 模拟,否则 STJ 反射报错。
#:project ../src/TenonAdmin.AspNetCore/TenonAdmin.AspNetCore.csproj
#:property PublishAot=false

using TenonAdmin.AspNetCore;

int passed = 0, total = 0;
void Check(string name, bool ok)
{
    total++;
    if (ok) { passed++; Console.WriteLine($"  ✓ {name}"); }
    else Console.WriteLine($"  ✗ {name}  <<< 失败");
}

// 动作入参字典:参数名 → 值(模拟 MVC 的 ActionArguments)
static Dictionary<string, object?> Args(object? v) => new() { ["input"] = v };

// 1) 顶层密码打码,普通字段保留
var r1 = SensitiveDataMasker.Mask(Args(new { Account = "admin", Password = "secret123" }));
Check("Password 被脱敏为 ***", r1.Contains("***") && !r1.Contains("secret123"));
Check("Account 原样保留", r1.Contains("admin"));

// 2) 递归:嵌套对象 + 数组元素里的敏感字段都要打码
var r2 = SensitiveDataMasker.Mask(Args(new
{
    Detail = new { AccessSecret = "topsecret", Public = "visible" },
    Items = new object[] { new { Token = "tok123" }, new { Name = "keep" } },
}));
Check("嵌套 AccessSecret 打码", !r2.Contains("topsecret"));
Check("嵌套 Public 保留", r2.Contains("visible"));
Check("数组内 Token 打码", !r2.Contains("tok123"));
Check("数组内 Name 保留", r2.Contains("keep"));

// 3) 命名变体(camelCase / snake_case)子串匹配
var r3 = SensitiveDataMasker.Mask(Args(new Dictionary<string, object?>
{
    ["newPassword"] = "np1", ["access_token"] = "at1", ["userName"] = "alice",
}));
Check("newPassword 打码", !r3.Contains("np1"));
Check("access_token 打码", !r3.Contains("at1"));
Check("userName 保留", r3.Contains("alice"));

// 4) 不可序列化入参(循环引用)不抛,记占位串
var node = new Node { Name = "a" };
node.Child = node;                       // 自引用 → STJ 序列化抛异常 → 走 catch
var r4 = SensitiveDataMasker.Mask(Args(node));
Check("循环引用兜底为占位串", r4 == "<unserializable>");

Console.WriteLine($"\n结果:{passed}/{total} 通过");
if (passed != total) Environment.Exit(1);

sealed class Node { public string Name { get; set; } = ""; public Node? Child { get; set; } }
