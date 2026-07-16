# 多副本部署

水平扩容。起第二个副本之前,下面**四条一条都不能少**。少任何一条,系统不会报错,只会开始悄悄做错事。

```bash
# 仓库里有现成的双副本叠加层,也是 CI 里真跑的那套
docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
bash scripts/smoke-multi-replica.sh http://localhost:8080   # 逐条验证下面这些保证
```

## ① Redis 是**前置条件**,不是可选优化

进程内缓存意味着副本 A 的失效**永远传不到**副本 B。后果不是"慢一点",是安全功能直接失灵:

| 表现 | 细节 |
|---|---|
| **强制下线失灵(最严重)** | 会话缓存的 TTL 是**刷新令牌寿命**(天级)。A 上强退 → DB 写了吊销、A 清了自己的内存;**B 的那份还在**,继续判定"活跃",于是经负载均衡时约一半请求照常放行,而且一放就是**好几天**。 |
| **撤权后仍有权限** | 权限/数据范围缓存默认 20 分钟。被撤权的人在另一副本上照旧有权限;数据范围还喂着 SqlSugar 全局过滤器 —— 他**继续看得见别的机构的数据**。 |
| **锁定/限流阈值翻 N 倍** | 登录失败计数、限流计数各副本各数各的:`MaxFailCount=5` 两副本就成了 10,认证桶 20/min 成了 40/min。 |
| **验证码必失败** | 一次性票据发在 A、验在 B,B 上没有这个键。 |

配上 `Cache:Provider=Redis` + `Cache:RedisConnectionString`,以上**全部自动修好**(失效走的是缓存键空间,不是事件总线),业务代码零改动。

## ② 每个副本必须有**不同的 `WorkerId`**

同号 = 同毫秒发号撞主键(数据损坏级)。内核对此**不再沉默**:配了 Redis(= 明显的多实例意图)却没显式给 `TenonAdmin:Id:WorkerId` → **启动直接报错**。

- **compose**:`--scale app=2` 给不了各副本不同的环境变量,所以要写**多个显式的 app 服务**(见 `docker-compose.scale.yml`,各配各的 WorkerId)。
- **k8s**:用 **StatefulSet**,从 Pod 名字的序号(`app-0`/`app-1`)注入 `WorkerId`。Deployment 的随机 Pod 名给不了稳定序号。

## ③ 反向代理必须配 `ForwardedHeaders`

见上面那一节。不配的话,两个副本都只看得见负载均衡器那一个 IP —— 按 IP 限流形同虚设,审计日志里的 IP 全是代理地址。

## ④ 冷启动**先起一个副本**

CodeFirst 建表 + 写种子是"检查后插入",**不是原子的**:两个副本同时首启,会有一个撞唯一键崩掉。

- **compose**:第二个副本 `depends_on: app: condition: service_healthy`(`docker-compose.scale.yml` 就是这么写的),零代码解决。
- **k8s**:用 init job / migration job 先把库建好,再放开副本。

## 还没解决的:上传目录必须是**共享可写卷**

`LocalFileStorage` / `ChunkStorage` 写的是**本地盘**。compose 用具名卷,两个副本天然共享;但 **k8s 上如果每个 Pod 一个独立 PVC,A 传的文件在 B 上就是 404**,分片上传更是直接 `ChunkMissing`(分片散落在不同 Pod 上,合并必然缺片)。多副本必须给上传根挂 **RWX(ReadWriteMany)** 的共享卷,或前置替换 `IFileStorage` 成对象存储(S3/OSS)。

**上一节:** [路线 D:Docker](/zh/guide/deployment/route-d)
**下一节:** [部署后自检](/zh/guide/deployment/post-deploy-check)
