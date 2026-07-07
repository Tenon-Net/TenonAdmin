using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TenonAdmin.AspNetCore;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 权限码一致性回归锁(P2-14):DefaultMenuSeed 里手写的每个权限码,必须对应一个真实存在、
/// 挂了 [RolePermission] 的内置控制器端点。种子码与路由是两处分离的手写串,靠人肉同步;
/// 改错一个字符即"授了也匹配不上",且无编译/测试报错——本测试用反射按 BuildPermissionCode 同规则
/// 从控制器算码,锁死漂移。
/// </summary>
public class PermissionCodeConsistencyTests
{
    [Fact]
    public void Every_seeded_permission_code_maps_to_a_real_endpoint()
    {
        var endpointCodes = BuiltInEndpointCodes();
        var seededCodes = SeededPermissionCodes();

        Assert.NotEmpty(seededCodes);   // 防呆:种子真被读到
        foreach (var code in seededCodes)
            Assert.Contains(code, endpointCodes);
    }

    /// <summary>反射内置控制器,按 {大写Method}:/{小写路由模板} 生成所有 [RolePermission] 端点的权限码。</summary>
    private static HashSet<string> BuiltInEndpointCodes()
    {
        var codes = new HashSet<string>();
        var controllers = typeof(TenonAdminSetup).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var hasPermission = method.GetCustomAttribute<RolePermissionAttribute>() is not null
                    || controller.GetCustomAttribute<RolePermissionAttribute>() is not null;
                if (!hasPermission) continue;

                foreach (var http in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var template = Combine(controllerRoute, http.Template);
                    foreach (var verb in http.HttpMethods)
                        codes.Add($"{verb.ToUpperInvariant()}:/{template.TrimStart('/').ToLowerInvariant()}");
                }
            }
        }
        return codes;
    }

    /// <summary>取 DefaultMenuSeed 里全部非空权限码(internal 类,反射实例化后经公共 ISeedData&lt;SysMenu&gt; 取数据)。</summary>
    private static List<string> SeededPermissionCodes()
    {
        var seedType = typeof(RbacService).Assembly.GetType("TenonAdmin.Services.DefaultMenuSeed")!;
        var seed = (ISeedData<SysMenu>)Activator.CreateInstance(seedType, nonPublic: true)!;
        return seed.HasData().Select(m => m.Permission).Where(p => !string.IsNullOrEmpty(p)).ToList();
    }

    private static string Combine(string controllerRoute, string? actionTemplate) =>
        string.IsNullOrEmpty(actionTemplate)
            ? controllerRoute
            : $"{controllerRoute.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";
}
