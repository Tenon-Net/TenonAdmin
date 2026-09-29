using Microsoft.AspNetCore.Mvc;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;

namespace TenonAdmin.Integration;

/// <summary>
/// 可靠投递管理(后台用户接口,<c>[RolePermission]</c>):列表、状态汇总、详情(含服务端判定的可用操作与尝试历史)与人工操作。
/// 人工操作逐项独立授权、进入用户操作审计;执行前在服务端复检可用性,保持原幂等标识,不绕过出站安全限制。
/// </summary>
[NonController]   // 默认发现跳过;由 AddTenonAdminIntegration 注册的特性提供者挂回(只引用本包不暴露端点)
[ApiController]
[Route("api/v1/integration/delivery")]
[Module("Integration")]
public class IntegrationDeliveryController(IDeliveryAdminService deliveries) : ControllerBase
{
    /// <summary>分页查询投递记录(新在前)</summary>
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<DeliveryListItem>>> Page([FromQuery] DeliveryPageInput input, CancellationToken cancellationToken) =>
        Result<PagedList<DeliveryListItem>>.Ok(await deliveries.PageAsync(input, cancellationToken));

    /// <summary>按状态计数</summary>
    [HttpGet("summary")]
    [RolePermission]
    public async Task<Result<IReadOnlyList<DeliveryStatusCount>>> Summary(CancellationToken cancellationToken) =>
        Result<IReadOnlyList<DeliveryStatusCount>>.Ok(await deliveries.SummaryAsync(cancellationToken));

    /// <summary>投递详情(含可用操作与尝试历史)</summary>
    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<DeliveryDetail>> Get(long id, CancellationToken cancellationToken) =>
        Result<DeliveryDetail>.Ok(await deliveries.GetAsync(id, cancellationToken));

    /// <summary>重试(失败,或所有尝试都可安全重发的耗尽):重置预算,幂等标识不变</summary>
    [HttpPost("{id}/retry")]
    [RolePermission]
    [OperationLog("重试可靠投递")]
    public Task<Result<DeliveryDetail>> Retry(long id, [FromBody] DeliveryActionInput input, CancellationToken cancellationToken) =>
        Execute(id, DeliveryActions.Retry, input, cancellationToken);

    /// <summary>立即查询对方结果并按结果迁移</summary>
    [HttpPost("{id}/query")]
    [RolePermission]
    [OperationLog("查询可靠投递结果")]
    public Task<Result<DeliveryDetail>> Query(long id, [FromBody] DeliveryActionInput input, CancellationToken cancellationToken) =>
        Execute(id, DeliveryActions.Query, input, cancellationToken);

    /// <summary>人工确认对方已成功(须填写说明)</summary>
    [HttpPost("{id}/confirm-succeeded")]
    [RolePermission]
    [OperationLog("确认可靠投递已成功")]
    public Task<Result<DeliveryDetail>> ConfirmSucceeded(long id, [FromBody] DeliveryActionInput input, CancellationToken cancellationToken) =>
        Execute(id, DeliveryActions.ConfirmSucceeded, input, cancellationToken);

    /// <summary>人工确认对方未执行(须填写说明):重新进入待处理</summary>
    [HttpPost("{id}/confirm-not-executed")]
    [RolePermission]
    [OperationLog("确认可靠投递未执行")]
    public Task<Result<DeliveryDetail>> ConfirmNotExecuted(long id, [FromBody] DeliveryActionInput input, CancellationToken cancellationToken) =>
        Execute(id, DeliveryActions.ConfirmNotExecuted, input, cancellationToken);

    /// <summary>取消(非处理中、非终态)</summary>
    [HttpPost("{id}/cancel")]
    [RolePermission]
    [OperationLog("取消可靠投递")]
    public Task<Result<DeliveryDetail>> Cancel(long id, [FromBody] DeliveryActionInput input, CancellationToken cancellationToken) =>
        Execute(id, DeliveryActions.Cancel, input, cancellationToken);

    private async Task<Result<DeliveryDetail>> Execute(long id, string action, DeliveryActionInput input, CancellationToken cancellationToken) =>
        Result<DeliveryDetail>.Ok(await deliveries.ExecuteAsync(id, action, input ?? new DeliveryActionInput(), cancellationToken));
}
