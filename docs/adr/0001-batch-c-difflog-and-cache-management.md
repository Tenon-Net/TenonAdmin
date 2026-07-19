# ADR 0001 — 批次 C:不做内核内实体 DiffLog;缓存管理是"失效"而非"浏览"

- 状态:已采纳(2026-07-18)
- 相关:`docs/refinement-ledger.md` 批次 C;`skills/replace-service`(消费者替换成法)

## 背景

精致化台账批次 C 原设想两件:C1 实体变更差异日志(SqlSugar DiffLog),C2 缓存管理页(键浏览 + 清除)。开工前用 `/grill-with-docs` 做了一轮设计审问 + 代码探查 + SqlSugar 官方文档核对,结论与原设想差距很大,记两条决策防反复。

## 决策一:不做内核内实体 DiffLog(C1)

**不做。** 消费者真需要时通过替换点自建。

理由:

1. **与操作日志重叠。** `SysOpLog` 已记每个写请求的完整参数 JSON + 操作人 + 时间 + 结果码;审计字段(CreateUser/UpdateUser/Time)盖每行;软删行保留可查。DiffLog 唯一增量是**字段级前像** + **非 HTTP 写入**,面窄。
2. **顶架构。** ① `SqlSugarRepository<>` 无单一写咽喉,Insert/Update/Delete 各自直调 `db.Insertable/Updateable/Deleteable`,逐命令挂要动 6 个方法;② `Aop.OnDiffLogEvent` 在 SqlSugar 层,而日志表实体在 Services 层 → 只能匿名字典 insert 绕分层违规;③ 软删是 `SetColumns` 列更新(现有审计 AOP 都不在此触发),"删除差异"只能记 IsDelete/UpdateTime 两列,残缺且吵;④ update 前像要多一次 SELECT。
3. **不丢扩展性。** 替换性模型已兜住:消费者可子类化 `SqlSugarRepository<>`,或自行挂 `client.Aop.OnDiffLogEvent`。SqlSugar 原生支持 `.EnableDiffLogEvent(biz)` 逐命令启用 + `StaticConfig.CompleteUpdateableFunc` 全局按 marker 接口 opt-in(后者是进程全局静态,内核刻意不碰)。

## 决策二:缓存管理是"定向失效"而非"键浏览器"(C2 → C2-lite)

**做,但只做失效操作页,不做键浏览/取值。**

理由(经代码核对):

1. **默认内存 provider 无法枚举键。** `MemoryCacheProvider` 包 `IMemoryCache`,无受支持的键枚举、无侧键集 → 键浏览在每个零配置部署上都是空操作。
2. **键与值都敏感。** 键内嵌 PII(`sms:cd:{phone}` / `sms:day:{phone}` 手机号、`rl:{ip}` 客户端 IP),值含明文验证码 / OTP + MFA 绑定 userId → 列键即泄露、读值等于给管理员开旁路读 OTP 的口子。

故:`ICacheProvider` **不加** `SearchKeysAsync` 之类枚举方法(接口零改动,替换性契约不动)。改为 `ICacheAdminService` 提供 4 个定向失效动作(清授权 / 字典 / 配置缓存、重建门户代际),全是**单键 `RemoveAsync`/`IncrementAsync`、由 DB 已知 ID 驱动、零缓存枚举**,内存 / Redis 皆可用。用途是补自动失效(授权变更 `RbacService` 已自动 bump 代际 + 清 per-user 键)覆盖不到的场景:直接改库致缓存陈旧的运维逃生舱。

## 后果

- 内核审计能力 = 操作日志 + 登录日志 + 异常日志 + 审计字段;字段级前像审计交由消费者按需扩展。
- 缓存管理页只清不看;若消费者装 Redis 并要键级可观测,应走其观测栈(如 `redis-cli` / RedisInsight),不是内核端点。
