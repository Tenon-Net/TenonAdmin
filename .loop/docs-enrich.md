# Goal: 扩充 TenonAdmin 文档站(深入/FAQ/规范/教程/参与),源码为准
Done-condition: Tasks 全 [x] 且 site 构建通过无死链
round: 2 / max: 30
执行方式:由主 agent 直接驱动,5 个子 agent 按板块并行写页,主 agent 收口 config.ts + 构建。

## Tasks
- [x] site/deep/architecture.md 架构分层与包依赖
- [x] site/deep/request-pipeline.md 请求管线
- [x] site/deep/data-scope.md 多组织数据权限五种范围
- [x] site/deep/auth-security.md 认证与安全
- [x] site/deep/replaceability.md 可替换性 TryAdd/virtual/六件套
- [x] site/deep/data-layer.md 数据层与审计字段/雪花ID
- [x] site/faq.md 常见问题(soybean 式:前言 + 问题背景/原因/解决)
- [x] site/standard/backend.md 后端规范
- [x] site/standard/frontend.md 前端规范
- [x] site/standard/commit.md 提交规范
- [x] site/tutorial/quickstart.md 从零跑通第一个接口
- [x] site/tutorial/business-module.md 端到端加一个业务模块
- [x] site/tutorial/frontend-page.md 前端加一个页面
- [x] site/tutorial/docker-deploy.md 容器化部署一条龙
- [x] site/community/contributing.md 贡献指南
- [x] site/community/agent-skills.md Agent Skills 与 AI 辅助开发
- [x] config.ts 接入所有新页(nav + sidebar),guide 侧栏移除 coding-standards
- [x] 收尾:cd site && npm run docs:build 通过、无死链

## Round log
### Round 1 — 建账本、backend 源码树摸底、config/风格参考已读。NEXT: 起 5 个子 agent 并行写页。
### Round 2 — 5 个子 agent 并行写完全部 16 页(均逐页核对真实源码,含 DataScopeType/ReplaceabilityTests/SnowflakeIdGenerator 等);主 agent 重写 config.ts(nav 补深入/教程/规范/参与,sidebar 建 5 组,guide 侧栏移除 coding-standards、FAQ 归帮助组);npm run docs:build 通过、无死链。✅ DONE。
备注:guide/coding-standards.md 文件保留但已从侧栏移除(内容并入 standard/),未删。未 commit,未碰 admin.db。
