# 新建业务模块开发指南

> 以现有「字典」模块为蓝本,一步步走完"加一个业务(如 `Product` 商品)"的端到端流程。
> 规范细则见[代码规范](/zh/standard/backend)。
> 两条路线:**A. 在内核内加**(本仓库直接加 `Sys*`/内置控制器);**B. 消费方在自己的业务程序集里加**(推荐给使用方,靠 `ApplicationAssemblies` 挂载,不改内核)。步骤基本一致,差异见 A11。

以下以 `Product` 为例(把 `Product` 换成你的实体名)。

---

## 0. 快速开始(消费方脚手架,路线 B)

消费方(装 NuGet 包的使用方)可用官方模板一键生成**可运行的后台 host**,已预接线一个机构隔离示例业务模块(`Modules/SampleDoc*`):

```bash
dotnet new install TenonAdmin.Templates      # 安装模板(一次)
dotnet new tenon-app -n Shop                 # 生成 host + 示例模块
cd Shop && dotnet run                        # 直接起;控制台打印随机超管密码
```

- 零配置默认 SQLite,自动建表 + 种子;换库改 `appsettings.json` 的 `TenonAdmin:Database`。
- 生成的 `Modules/SampleDoc*` 四件套(实体 / 接口 / 实现 / 控制器)就是「加下一个业务模块」的**复制范本**:复制改名,再在 `Program.cs` 追加一行 `TryAddScoped<I你的Service, 你的Service>()`(实体自动建表、控制器自动挂路由)。
- 需要从零手写、或在内核内加(路线 A),继续看下文。

## 本节内容

- [A. 后端](/zh/guide/new-business/backend) —— 实体、DTO、服务、控制器、错误码、缓存、种子数据、菜单与授权、测试,以及消费方路线(路线 B)
- [B. 前端](/zh/guide/new-business/frontend) —— 重新生成 API 类型、封装接口、CRUD 视图、挂载菜单、路由、i18n
- [C. 端到端清单](/zh/guide/new-business/checklist) —— 完整的后端/前端/权限配置清单

**下一节:** [A. 后端](/zh/guide/new-business/backend)
