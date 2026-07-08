- 你现在进入「TenonAdmin 后端 review loop」模式，持续审查并推进这个仓库的后端质量。

  本轮必须遵守：

  1. 先读以下文档，再开始：
     - `docs/rebuild-design.md`（设计单源）
     - `docs/dev-plan.md`（当前实现边界/后置项）
     - `docs/phase2-review.md`（历史审查参考）

  2. 默认只审后端：
     - `backend/src/TenonAdmin.Core`
     - `backend/src/TenonAdmin.SqlSugar`
     - `backend/src/TenonAdmin.Services`
     - `backend/src/TenonAdmin.AspNetCore`
     - `backend/src/TenonAdmin`
     - `backend/tests/TenonAdmin.Tests`
     - `backend/tests/TenonAdmin.TestHost`
     - `backend/samples/MinimalHost`
     - 必要时可看 `.github/workflows/backend-ci.yml`

  3. 目标：
     - 找出隐藏 bug 和明显 bug；
     - 小问题直接修；
     - 拿不准、影响面大、需要设计取舍的问题不要硬改，记录下来等我看；
     - 每轮执行都必须输出一份记录文件。

  4. 优先关注：
     - Auth / JWT / Session / RefreshToken
     - RBAC / RolePermission / Module / Menu
     - DataScope / Org / Query Filter / CurrentUser
     - Result 信封 / 异常处理 / OpenAPI / Health / CORS / RateLimit
     - SqlSugar / Repository / Seed / CodeFirst / Transaction
     - Upload / FileStorage / 路径处理 / 安全边界
     - 并发 / 缓存失效 / 大小写与 trim / 环境差异 / 测试缺口

  5. 工作方式：
     - 先看 `git status --short`；
     - 建立当前 build/test 基线，不要想当然；
     - 每轮只聚焦一个子域；
     - 先找会出错的问题，不要先做风格评论；
     - 能补测试就先补测试，再修；
     - 改动必须小、集中、可验证；
     - 不新增依赖，不做大重构，不改前端。

  6. 可直接修的问题：
     - 明显边界判断缺失；
     - 返回码/状态码/统一信封不一致；
     - 配置默认值与设计不一致；
     - 大小写、trim、规范化遗漏；
     - 小范围事务/守卫/错误分支遗漏；
     - 本地与 CI、Windows 与 Linux 差异导致的 bug；
     - 小范围并发问题；
     - 已有明确测试缺口且可最小修复的问题。

  7. 只记录、不直接改的问题：
     - 涉及设计口径变化；
     - 涉及数据库模型或历史数据处理；
     - 大范围 API 契约调整；
     - 跨多个模块的大改；
     - 证据还不够、只能怀疑的问题。

  8. 每轮结束前，必须写 Markdown 记录文件到：
     `docs/review-runs/`

     文件名格式：
     `YYYY-MM-DD-HHMM-backend-review-loop-<topic>.md`

  9. 记录文件必须包含：
     - 本轮范围
     - 当前基线（git/build/test）
     - 已确认并已修复的问题
     - 已确认但暂不修改的问题
     - 验证证据
     - 本轮改动文件
     - 下一轮建议

  10. 对我的最终输出必须简洁，只包含：
     - 本轮聚焦了什么
     - 修了哪些小问题
     - 记录了哪些待确认问题
     - 跑了哪些验证
     - 记录文件路径

  额外要求：
  - 以 `docs/rebuild-design.md` 为设计最高约束；
  - 以 `docs/dev-plan.md` 为实现边界约束；
  - 以当前代码和当前测试输出为事实依据；
  - 不要把猜测包装成已确认事实；
  - 不问我要不要继续，直接完成本轮 review、小修和记录；
  - 如果本轮没发现可修问题，也必须照样产出记录文件并如实汇报。
