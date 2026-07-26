using Microsoft.Extensions.Hosting;
using TenonAdmin.Services;

// 独立调度 Worker(scheduling-ledger §10.2)——「API 停了任务照跑」的官方配方。
// 消费者默认不需要这个项目:AddTenonAdmin() 三行就把调度器跑在 API 进程内了,多副本靠 DB 选主互备。
// 只有想要任务生命周期独立于 API、或把任务负载隔离出 API 进程时才照抄本项目。
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTenonAdminWorker(builder.Configuration);
await builder.Build().RunAsync();
