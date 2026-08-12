# 创建实体 (Create Entity)

在 TenonAdmin 中创建一个 SqlSugar 实体类。

## 第一步：确定模式

- **系统模块（内核维护者）**：文件放 `backend/src/TenonAdmin.Services/Entities/Sys{Name}.cs`，命名空间 `TenonAdmin.Services`，表名 `sys_xxx`
- **业务模块（消费者二开）**：文件放消费者自己的 Assembly 中，命名空间自定，表名 `biz_xxx` 或自定前缀。最简单的形态就是宿主项目本身（`Program.cs` 所在项目）——文件直接放进项目内的文件夹即可，SDK 项目默认收纳所有 `.cs`，不用改 csproj、不用单独建类库

## 第二步：选择基类

| 基类 | 何时用 | 自带字段 |
|---|---|---|
| `BaseEntity` (命名空间 `TenonAdmin.SqlSugar`) | 无需按组织隔离的表（字典、配置、全局数据） | `Id`, `CreateTime`, `CreateUserId`, `UpdateTime`, `UpdateUserId`, `IsDelete` |
| `DataEntity` (命名空间 `TenonAdmin.SqlSugar`) | 需要按组织数据权限隔离的业务表 | 继承 BaseEntity 全部 + `CreateOrgId`（归属机构锚点） |

**注意：以上审计字段由 SqlSugar AOP 自动填充，实体类中不要重复定义。**

## 第三步：编写实体

### 规则

1. **表名**：snake_case，用 `[SugarTable("表名", TableDescription = "中文描述")]`
2. **索引**：唯一字段加 `[SugarIndex("idx_表名_字段名", nameof(字段), OrderByType.Asc, IsUnique = true)]`
3. **属性**：每个属性标 `[SugarColumn]`，常用参数：
   - `Length = N`：字符串长度（不标默认 nvarchar(max)）
   - `IsNullable = true`：可空字段
   - `ColumnDescription = "中文描述"`：列注释
   - `ColumnDataType = "text"`：大文本等特殊类型
4. **命名**：属性用 PascalCase，字符串默认值 `= ""`，布尔默认值按业务需要
5. **using**：`using SqlSugar;` + `using TenonAdmin.SqlSugar;`

### 参考模板

```csharp
// 文件: backend/src/TenonAdmin.Services/Entities/SysPosition.cs
using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

[SugarTable("sys_position", TableDescription = "职位")]
[SugarIndex("idx_sys_position_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class SysPosition : BaseEntity
{
    [SugarColumn(Length = 64, ColumnDescription = "职位名称")]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64, ColumnDescription = "职位编码(唯一)")]
    public string Code { get; set; } = "";

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;
}
```

### 业务模块示例

```csharp
// 文件: 消费者项目/Entities/BizProduct.cs
using SqlSugar;
using TenonAdmin.SqlSugar;

namespace MyApp;

[SugarTable("biz_product", TableDescription = "产品")]
[SugarIndex("idx_biz_product_code", nameof(Code), OrderByType.Asc, IsUnique = true)]
public class BizProduct : DataEntity  // 继承 DataEntity 获得组织数据隔离
{
    [SugarColumn(Length = 128, ColumnDescription = "产品名称")]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64, ColumnDescription = "产品编码(唯一)")]
    public string Code { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 512, ColumnDescription = "产品描述")]
    public string? Description { get; set; }

    [SugarColumn(ColumnDescription = "单价(分)")]
    public int Price { get; set; }

    [SugarColumn(ColumnDescription = "是否上架")]
    public bool Enabled { get; set; } = true;

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }
}
```

## 常见字段类型速查

| C# 类型 | SugarColumn 设置 | 用途 |
|---|---|---|
| `string` | `Length = N` | 短文本（名称、编码等） |
| `string?` | `Length = N, IsNullable = true` | 可选文本 |
| `string` | `ColumnDataType = "text"` | 长文本（描述、备注） |
| `int` | 默认 | 整数（排序、数量等） |
| `decimal` | `DecimalDigits = 2` | 金额 |
| `bool` | 默认 | 开关（启用/禁用） |
| `DateTime` | 默认 | 时间 |
| `DateTime?` | `IsNullable = true` | 可选时间 |
| `long?` | `IsNullable = true` | 外键关联（如 CategoryId） |
| `int`（枚举） | `ColumnDescription = "..."` | 枚举值存整数，C# 定义 `enum XxxType { ... }` |

## 容易忽略的点

### 外键/关联字段

命名用 `{关联实体}Id`，类型 `long?`（可空——允许不关联）：

```csharp
[SugarColumn(IsNullable = true, ColumnDescription = "所属分类 Id")]
public long? CategoryId { get; set; }
```

SqlSugar 不自动创建外键约束（CodeFirst 只建表+索引），关联完整性靠业务层保证。如果需要频繁按此字段查询，加索引：

```csharp
[SugarIndex("idx_biz_product_category", nameof(CategoryId), OrderByType.Asc)]
```

### 复合索引

多字段联合唯一：

```csharp
[SugarIndex("idx_biz_order_item_unique", nameof(OrderId), OrderByType.Asc, nameof(ProductId), OrderByType.Asc, IsUnique = true)]
```

### 枚举字段

存 `int`，C# 侧定义枚举类型供业务代码使用（不标 `[SugarColumn]` 特殊设置，SqlSugar 自动存整数）：

```csharp
public enum OrderStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3 }

[SugarColumn(ColumnDescription = "订单状态")]
public OrderStatus Status { get; set; }
```

### DataEntity 的写路径守卫

继承 `DataEntity` 的实体，`IRepository` 的 `UpdateAsync`/`DeleteAsync` 已内置数据范围守卫——越权改删他机构行会被拒。业务服务仍建议改/删前先 `GetAsync`（经范围过滤），以返回准确的"未找到"错误。

## 已有表加列（演进列 / 升级补列）

CodeFirst **只加列、不删列、不改窄**。对**已经发过版、库里可能有数据**的表再加字段时：

| 情况 | 怎么建 |
|---|---|
| **新表**（首次 `InitTables` 时还不存在） | 可用 `NOT NULL`：`bool` / `DateTime` / 非空 `string` 均可 |
| **已有表加列** | **数据库列必须可空**：`[SugarColumn(IsNullable = true)]`；新增属性可用 `T?` |
| 业务默认 false / 未绑定 / 未设置 | DB 允许 `NULL`；**读侧**把它当默认；已发布公共属性保留 CLR 类型并锁定 ORM 默认值物化 |

**为什么**：MSSQL 不能对「有数据的表」`ADD` 无 `DEFAULT` 的 `NOT NULL` 列；SQLite 开发库往往不炸，生产 SqlServer 升级会挂。本仓不做自动改窄 / 回填后改 `NOT NULL`。

错误示例（已有表上禁止）：

```csharp
public bool ForceTotp { get; set; }           // 非空 bool 新列
public DateTime AbsoluteExpiresAt { get; set; } // 非空时间新列
```

正确示例（新增属性；对齐 `SysUser.ForceTotp` / `SysSession.AbsoluteExpiresAt` 的数据库列）：

```csharp
[SugarColumn(IsNullable = true, ColumnDescription = "是否强制 TOTP")]
public bool? ForceTotp { get; set; }   // null = 补列前存量行；读侧 ForceTotp == true

[SugarColumn(IsNullable = true, ColumnDescription = "绝对过期时刻")]
public DateTime? AbsoluteExpiresAt { get; set; }  // 读侧 AbsoluteExpiresAt ?? ExpiresAt
```

已发布的公共属性不能仅为迁移改成 `T?`：保留原 CLR 类型，用测试锁住存量 `NULL` 的默认值物化和业务回退，见 `SysUser.ForceTotp` / `SysSession.AbsoluteExpiresAt`。

回归锁：`CodeFirstNullableUpgradeTests`（非空表砍列再 InitTables 补回）。

## 种子数据的 Id 区间：消费者一律 `>= 1000`

字典类型 / 字典项 / 配置项的删除接口按 **`Id < 1000` 判定为内核种子并拒删**（`ErrorCode.SeedDataProtected`，QA13）。判定只看 Id，不看 code，所以新增内核种子会自动受保护——代价是消费者写种子时必须避开这个区间：

```csharp
// ❌ 消费者种子用小 Id：建出来就删不掉,管理端点「删除」永远报 SeedDataProtected
new SysDictType { Id = 42, Code = "crm_customer_level", ... }

// ✅ 消费者种子从 1000 起（雪花 Id 天然远大于 1000,手写才需要注意）
new SysDictType { Id = 1001, Code = "crm_customer_level", ... }
```

只影响**手写 Id** 的种子；走 AOP 自动填充雪花 Id 的业务数据不受影响。

## 注意事项

- 实体建完后，CodeFirst 会在应用启动时自动建表（`DatabaseInitializer`）
- 业务模块需确保消费者的 `Program.cs` 中已配置 `ApplicationAssemblies`：
  ```csharp
  builder.Services.AddTenonAdmin(builder.Configuration, o =>
      o.ApplicationAssemblies.Add(typeof(Program).Assembly));
  ```
- 不需要手动建表或写迁移脚本
- **已有表加列**见上一节，勿在演进列上使用非空无默认值
