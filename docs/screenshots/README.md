# README 截图说明

这些图片来自本仓库当前代码运行出的真实页面，使用 `backend/samples/MinimalHost` 与 `backend/samples/IntegrationSample` 的临时 SQLite 数据库拍摄。截图不含真实凭据或个人信息。

| 文件 | 页面与演示数据 | 视口 |
| --- | --- | --- |
| `vue-admin.png` | Vue 模板，系统应用的用户管理页；机构树已展开，通过页面自带的列设置隐藏创建时间 | 1600 × 1000 |
| `react-admin.png` | React 模板，系统应用的用户管理页；机构树已展开，通过页面自带的列设置隐藏创建时间 | 1600 × 1000 |
| `workflow-designer.png` | Vue 模板，费用报销审批设计器；金额条件分支，高额路径为财务经理、总经理审批，默认路径为部门主管审批 | 1440 × 1050 |
| `workflow-approval.png` | Vue 模板，费用报销审批详情；报销金额 12,800 元，事由为客户现场交通与住宿，并展示真实审批结果与意见 | 1440 × 1650 |
| `org-management.png` | Vue 模板，机构管理树表；展示公司、部门和小组的完整层级 | 1600 × 1000 |
| `role-permissions.png` | Vue 模板，角色管理的授权菜单；展示应用、菜单和按钮级权限 | 1600 × 1000 |
| `dictionary.png` | Vue 模板，字典管理主从页；选中机构分类并展示公司、部门、小组字典项 | 1600 × 1000 |
| `file-management.png` | Vue 模板，文件管理页；通过真实上传接口保存品牌图片 `tenon-mark.png` | 1600 × 1000 |
| `scheduled-jobs.png` | Vue 模板，定时任务页；展示第三方接入、工作流与系统清理任务的实时执行统计 | 1600 × 1000 |
| `integration-apps.png` | Vue 模板，第三方应用页；通过管理 API 创建五个中文演示应用 | 1600 × 1000 |
| `delivery-tasks.png` | Vue 模板，可靠投递页；由 IntegrationSample 事务建单并经本地 Mock Partner 真实推进出成功、待确认、失败和待核对状态 | 1600 × 1000 |
| `workflow-pending.png` | Vue 模板，待我审批页；真实发起费用报销单并进入部门主管审批节点 | 1600 × 1000 |

页面路径依次为 `/system/user`、`/system/org`、`/system/role`、`/system/dict`、`/system/file`、`/system/job`、`/integration/app`、`/integration/delivery`、`/workflow/todo`。工作流治理页路径为 `/workflow/definition/designer?id={id}`，处理页路径为 `/workflow/instance/{id}/detail`。

`thumbs/` 保存 640 × 400 的等比缩略图，留边保持比例；README 中的链接指向原图。更新原图时需同步对应缩略图。
