using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace TenonAdmin.Integration;

/// <summary>
/// 开放控制器的 MVC 约定:为每个带 <see cref="OpenApiAttribute"/> 的动作恰好挂一份授权、调用记录与统一错误出口过滤器
/// (类级与方法级同时标注也不会重复),并把 ApiExplorer 分组设为 <c>open-v{n}</c>——内核后台文档(默认分组)
/// 因此不再包含开放端点,前端生成的类型也不会混入外部契约。
/// </summary>
public sealed class OpenApiConvention : IApplicationModelConvention
{
    /// <summary>开放文档分组名前缀(<c>open-v1</c>)。</summary>
    public const string GroupNamePrefix = "open-";

    /// <inheritdoc />
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);
        foreach (var controller in application.Controllers)
        {
            var controllerOpen = controller.Attributes.OfType<OpenApiAttribute>().Any();
            foreach (var action in controller.Actions)
            {
                if (!controllerOpen && !action.Attributes.OfType<OpenApiAttribute>().Any()) continue;

                action.Filters.Add(new TypeFilterAttribute(typeof(OpenApiAuthorizationFilter)));
                action.Filters.Add(new TypeFilterAttribute(typeof(OpenApiCallLogFilter)));
                action.Filters.Add(new TypeFilterAttribute(typeof(OpenApiExceptionFilter)) { Order = int.MinValue });
                action.Filters.Add(new OpenApiProblemEnvelopeFilter());

                var template = CombinedTemplate(controller, action);
                action.ApiExplorer.GroupName = GroupNamePrefix + (OpenApiCatalog.VersionOf(template) ?? "invalid");
            }
        }
    }

    private static string? CombinedTemplate(ControllerModel controller, ActionModel action)
    {
        var controllerRoute = controller.Selectors.FirstOrDefault(s => s.AttributeRouteModel is not null)?.AttributeRouteModel;
        var actionRoute = action.Selectors.FirstOrDefault(s => s.AttributeRouteModel is not null)?.AttributeRouteModel;
        return AttributeRouteModel.CombineAttributeRouteModel(controllerRoute, actionRoute)?.Template;
    }
}
