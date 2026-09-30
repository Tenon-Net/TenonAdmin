namespace TenonAdmin.AspNetCore;

/// <summary>
/// 端点元数据标记:该端点已有<b>等价或更强的专用审计</b>,豁免 <see cref="OperationLogFilter"/> 的用户操作日志。
/// <para>操作日志是「用户做了什么」的审计(默认记录一切写操作,见 <see cref="OperationLogFilter"/>)。非用户身份调用的端点
/// (如第三方接入应用的开放接口,其调用记录单独落库)若仍进入 <c>sys_op_log</c>,会被记成「无操作人的用户操作」,
/// 反而混淆两类审计。只有端点自带专用审计时才应实现本接口——它是审计的<b>替代</b>,不是关闭审计的开关。</para>
/// </summary>
public interface ISkipOperationLogMetadata;
