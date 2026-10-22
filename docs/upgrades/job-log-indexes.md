# 任务日志调度查询索引升级

适用于使用旧版 `sys_job_log` 表结构的数据库，修复 [issue #85](https://github.com/Tenon-Net/TenonAdmin/issues/85) 中的任务调度扫描问题。

实体新增两个索引，原有的 `idx_sys_job_log(JobId, CreateTime DESC)` 继续用于任务历史日志查询：

| 索引 | 列顺序 | 对应查询 |
| --- | --- | --- |
| `idx_sys_job_log_job_end` | `JobId, EndTime` | 串行任务检查 `JobId = ? AND EndTime IS NULL` |
| `idx_sys_job_log_end_start` | `EndTime, StartTime` | 失联执行回收 `EndTime IS NULL AND StartTime < ?` |

## 使用 CodeFirst 的数据库

升级内核后，在允许 CodeFirst 的环境启动应用，会创建新索引；已有数据和原索引保留。再次启动不会重复创建同名索引。

生产环境除 `TenonAdmin:Database:EnableCodeFirst=true` 外，还需显式启用 `TenonAdmin:Database:EnableCodeFirstInProduction=true` 才会执行 CodeFirst。这个开关会对已登记的全部实体执行结构同步；只准备补本次索引的 MySQL 库，可使用下面的定向脚本。

CodeFirst 按实体声明的索引名检查。如果已用其他名字手工建立了同类索引，建议使用定向脚本识别已有索引，避免本次升级重复建立。后续开启 CodeFirst 前，应由 DBA 统一现有索引名与实体声明。

## 由 DBA 维护的 MySQL 数据库

在已存在 `sys_job_log` 表的目标库中执行 [MySQL 升级脚本](../../scripts/sql/mysql/job-log-indexes.sql)。例如在仓库根目录执行，替换连接参数和库名，密码通过交互输入：

```bash
mysql --host=127.0.0.1 --user=admin --password tenon_admin < scripts/sql/mysql/job-log-indexes.sql
```

脚本适用于 MySQL 8.0 / InnoDB，在当前数据库检查索引的前两列。已有同类可见 B-tree 索引时，即使用了其他索引名也会跳过；缺失时通过 `ALGORITHM=INPLACE, LOCK=NONE` 添加，最后更新表统计信息。可串行重复执行。索引创建仍需要读取现有数据、使用磁盘空间并获取元数据锁，请按数据库维护安排执行。

完成后检查索引与执行计划，不需要重启调度器：

```sql
SHOW INDEX FROM sys_job_log;
EXPLAIN SELECT Id FROM sys_job_log
WHERE JobId = 1 AND EndTime IS NULL LIMIT 1;
EXPLAIN SELECT Id, NodeName, NodeInstanceId FROM sys_job_log
WHERE EndTime IS NULL AND StartTime < NOW() - INTERVAL 60 SECOND;
```

将 `JobId` 替换为实际任务 Id，使用接近现场的数据量和运行中记录比例核对计划及扫描行数。预期这两类查询使用匹配的复合索引；具体访问方式与估算行数由优化器、统计信息和数据分布决定。补索引不改变串行跳过、节点判活或失联执行回收的语义。
