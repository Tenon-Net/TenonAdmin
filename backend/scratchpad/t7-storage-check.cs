// T7 自检:LocalFileStorage —— 存/读/删往返 + 路径穿越防护(../、绝对路径一律拒绝)。
// 运行:dotnet run t7-storage-check.cs
// 引用 Services 项目(经 SqlSugar 传递)→ 必须关 AOT 模拟。
#:project ../src/TenonAdmin.Services/TenonAdmin.Services.csproj
#:property PublishAot=false

using System.Text;
using TenonAdmin.Core;
using TenonAdmin.Services;

int passed = 0, total = 0;
void Check(string name, bool ok)
{
    total++;
    if (ok) { passed++; Console.WriteLine($"  ✓ {name}"); }
    else Console.WriteLine($"  ✗ {name}  <<< 失败");
}
static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

// 临时存储根(隔离,跑完删)
var root = Path.Combine(Path.GetTempPath(), "tenon-t7-" + Guid.CreateVersion7().ToString("N")[..8]);
var storage = new LocalFileStorage(new AdminUploadOptions { RootPath = root });

static Stream Bytes(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

// 1) 存 → 读往返
var savedPath = await storage.SaveAsync(Bytes("hello-tenon"), "20260707/abc.txt");
Check("SaveAsync 返回原相对路径", savedPath == "20260707/abc.txt");
Check("物理文件已落在根内", File.Exists(Path.Combine(root, "20260707", "abc.txt")));

await using (var read = await storage.OpenReadAsync("20260707/abc.txt"))
{
    var text = read is null ? null : await new StreamReader(read).ReadToEndAsync();
    Check("OpenReadAsync 读回原内容", text == "hello-tenon");
}

// 2) 不存在 → null
Check("OpenReadAsync 不存在返回 null", await storage.OpenReadAsync("nope/none.txt") is null);

// 3) 删除
await storage.DeleteAsync("20260707/abc.txt");
Check("DeleteAsync 后文件消失", !File.Exists(Path.Combine(root, "20260707", "abc.txt")));
Check("DeleteAsync 删不存在不抛", !Throws(() => storage.DeleteAsync("nope/none.txt").GetAwaiter().GetResult()));

// 4) 路径穿越防护:相对 ../ 逃逸
Check("../ 逃逸被拒", Throws(() => storage.SaveAsync(Bytes("x"), "../evil.txt").GetAwaiter().GetResult()));
Check("多级 ../ 逃逸被拒", Throws(() => storage.SaveAsync(Bytes("x"), "a/b/../../../evil.txt").GetAwaiter().GetResult()));
// 绝对路径(Path.Combine 会丢弃根 → 绝对路径,前缀校验拦下)
Check("绝对路径被拒", Throws(() => storage.SaveAsync(Bytes("x"), "C:/Windows/evil.txt").GetAwaiter().GetResult()));
// 读/删同样受围栏保护
Check("../ 读被拒", Throws(() => storage.OpenReadAsync("../../secret").GetAwaiter().GetResult()));

// 清理
try { Directory.Delete(root, recursive: true); } catch { }

Console.WriteLine($"\n结果:{passed}/{total} 通过");
if (passed != total) Environment.Exit(1);
