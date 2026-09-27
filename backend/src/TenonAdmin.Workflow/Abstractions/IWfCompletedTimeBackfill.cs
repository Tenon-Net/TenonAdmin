using Microsoft.Extensions.Hosting;

namespace TenonAdmin.Workflow;

/// <summary>工作流升级回填后台服务扩展点；消费者可在工作流注册前替换。</summary>
public interface IWfCompletedTimeBackfill : IHostedService
{
}
