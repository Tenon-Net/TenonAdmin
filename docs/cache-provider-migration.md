# 自定义缓存原子操作迁移

`ICacheProvider.IncrementAsync` 与 `GetAndRemoveAsync` 被登录失败计数、一次性验证码、OAuth/MFA 票据和权限版本失效直接使用。原先的默认读改写、先读后删不能满足并发安全契约，现已停止提供这种降级行为。

使用内置 `MemoryCacheProvider` 或 `RedisCacheProvider` 的应用无需调整。自定义提供者必须实现：

- `IncrementAsync`：原子增加计数并返回新值；指定过期时间时，计数与滑动过期设置须一起完成。
- `GetAndRemoveAsync<T>`：原子取值并删除；同一票据最多一个调用者能取到值。

单进程内存实现应让这些操作与普通写入、删除使用同一把锁；分布式实现必须使用存储端原语或事务，进程内锁不能保护其它副本。Redis 默认实现使用 Lua 完成计数和过期设置，使用 `GETDEL` 消费票据。

方法签名保留兼容。通过 `AddTenonAdminServices`（包括默认 API 和 Worker 宿主）启动时，会检查实际注入类型是否实现两个方法；缺失实现会在处理业务前给出包含类型和方法名的启动错误。继承实现和显式接口实现均支持。该检查不连接 Redis，也不能证明自定义方法内部的原子性，提供者仍须自行验证并发语义。

只拼装 DI、未启动 Generic Host 的调用方不会运行启动检查；调用未实现的方法会抛出 `NotSupportedException`。升级前请检查所有自定义缓存替换，并用并发测试验证计数不丢失、票据只消费一次。
