using System.Collections.Frozen;
using System.Reflection;

namespace TenonAdmin.Core;

/// <summary>
/// 业务错误码(设计 §13.2)。
/// <para>
/// 核心纪律:<b>后端只抛码、不写文案</b>。每个码携带一个语义 msgKey(如 <c>error.auth.passwordWrong</c>),
/// 前端用 msgKey 查本地语言包渲染提示;后端返回的 Message 字段仅作非浏览器调用方的兜底降级。
/// 这样文案单源在前端,切语言零后端参与。
/// </para>
/// <para>分段规划(新增错误码时按段落位,勿跨段挪用):</para>
/// <list type="table">
///   <item><term>0</term><description>成功</description></item>
///   <item><term>40000–40999</term><description>认证与登录</description></item>
///   <item><term>41000–41999</term><description>权限与数据范围</description></item>
///   <item><term>42000–42999</term><description>用户 / 组织 / 角色 / 菜单</description></item>
///   <item><term>43000–43999</term><description>字典 / 配置</description></item>
///   <item><term>44000–44999</term><description>文件上传</description></item>
///   <item><term>45000–45999</term><description>消息通知</description></item>
///   <item><term>46000–46999</term><description>导入 / 导出</description></item>
///   <item><term>47000–47999</term><description>定时任务</description></item>
///   <item><term>50000–50999</term><description>系统内部错误</description></item>
/// </list>
/// </summary>
public enum ErrorCode
{
    /// <summary>成功(统一返回的默认码)</summary>
    [MsgKey("common.success")]
    Success = 0,

    // ── 40xxx 认证与登录 ─────────────────────────────────────────────

    /// <summary>账号或密码错误。故意不区分"账号不存在/密码错",避免账号枚举攻击。</summary>
    [MsgKey("error.auth.passwordWrong")]
    PasswordWrong = 40001,

    /// <summary>验证码已过期(需刷新重取)</summary>
    [MsgKey("error.auth.captchaExpired")]
    CaptchaExpired = 40002,

    /// <summary>验证码不正确</summary>
    [MsgKey("error.auth.captchaWrong")]
    CaptchaWrong = 40003,

    /// <summary>账号因连续登录失败被临时锁定;args 可携带 lockMinutes</summary>
    [MsgKey("error.auth.accountLocked")]
    AccountLocked = 40004,

    /// <summary>账号已被停用(管理员操作)</summary>
    [MsgKey("error.auth.accountDisabled")]
    AccountDisabled = 40005,

    /// <summary>访问令牌无效或已过期(会话被强退也归此类)</summary>
    [MsgKey("error.auth.tokenInvalid")]
    TokenInvalid = 40006,

    /// <summary>刷新令牌无效 / 已轮换吊销 / 检测到重放</summary>
    [MsgKey("error.auth.refreshTokenInvalid")]
    RefreshTokenInvalid = 40007,

    /// <summary>请求过于频繁,已触发限流(RateLimiter,§12/§14);args 可携带 retryAfterSeconds。短信发送冷却/日上限也复用此码。</summary>
    [MsgKey("error.auth.tooManyRequests")]
    TooManyRequests = 40008,

    /// <summary>
    /// 密码校验已通过,需短信二次验证(全局 MFA 开且用户绑了手机号)。
    /// <b>信令而非失败</b>:args 携带 challengeId/phoneMask/expiresSeconds/resendSeconds,
    /// 前端据此切到验证码输入页,凭 challengeId + 短信码调 <c>POST /auth/login/sms</c> 完成登录。
    /// </summary>
    [MsgKey("error.auth.smsCodeRequired")]
    SmsCodeRequired = 40009,

    /// <summary>短信验证码不正确;args 携带 attemptsLeft(剩余尝试次数)</summary>
    [MsgKey("error.auth.smsCodeWrong")]
    SmsCodeWrong = 40010,

    /// <summary>
    /// 短信验证码已失效(缺失/过期/已消费/尝试次数耗尽/挑战无效统一归此,防探测)。
    /// 免密登录(手机号+码换令牌)还把错码(本应是 <see cref="SmsCodeWrong"/>)也归一到此码且不带
    /// attemptsLeft——否则"错码回 40010"与"未知手机号回 40011"本身就泄露了手机号是否已注册。
    /// </summary>
    [MsgKey("error.auth.smsCodeExpired")]
    SmsCodeExpired = 40011,

    /// <summary>短信验证码登录未启用(全局开关 <c>sys.security.smsLogin.enabled</c> 关闭)</summary>
    [MsgKey("error.auth.smsLoginDisabled")]
    SmsLoginDisabled = 40012,

    /// <summary>外部登录 provider 未启用或不存在(code 未配置 / 运营开关 <c>sys.externalauth.{code}.enabled</c> 关闭)</summary>
    [MsgKey("error.auth.oauthProviderDisabled")]
    OAuthProviderDisabled = 40013,

    /// <summary>外部登录回调 state 无效(缺失 / 过期 / 已消费——CSRF 防护;统一归此,防探测)</summary>
    [MsgKey("error.auth.oauthStateInvalid")]
    OAuthStateInvalid = 40014,

    /// <summary>外部登录令牌交换失败(授权码无效 / IdP 拒绝 / id_token 签名或声明校验不过)</summary>
    [MsgKey("error.auth.oauthExchangeFailed")]
    OAuthExchangeFailed = 40015,

    /// <summary>该外部身份尚未绑定任何本地账号(默认未绑定策略=拒绝;需先登录后在个人中心绑定,或该 provider 开启自动开户)</summary>
    [MsgKey("error.auth.oauthAccountNotBound")]
    OAuthAccountNotBound = 40016,

    /// <summary>外部身份绑定冲突:该外部身份已绑定其他账号,或当前账号已绑定同一 provider(绑定唯一)</summary>
    [MsgKey("error.auth.oauthAlreadyBound")]
    OAuthAlreadyBound = 40017,

    /// <summary>密码已通过,需 TOTP 二次验证(Level3 MFA;args 可携带 challenge 信息)</summary>
    [MsgKey("error.auth.totpRequired")]
    TotpRequired = 40018,

    /// <summary>TOTP 动态口令不正确</summary>
    [MsgKey("error.auth.totpWrong")]
    TotpWrong = 40019,

    /// <summary>账号尚未绑定 TOTP(强制 MFA 时须先完成自助绑定)</summary>
    [MsgKey("error.auth.totpNotBound")]
    TotpNotBound = 40020,

    /// <summary>
    /// TOTP 绑定无效:挑战缺失/过期/已消费、账号已绑定等(统一归此,防探测)。
    /// 数值 40021 保持兼容;历史名 <see cref="BindInviteInvalid"/> 同码。
    /// </summary>
    [MsgKey("error.auth.mfaBindInvalid")]
    MfaBindInvalid = 40021,

    /// <summary>历史别名,同 <see cref="MfaBindInvalid"/>。</summary>
    BindInviteInvalid = MfaBindInvalid,

    /// <summary>恢复码不正确或已使用</summary>
    [MsgKey("error.auth.recoveryCodeInvalid")]
    RecoveryCodeInvalid = 40022,

    /// <summary>CSRF 校验失败(双提交 Cookie 与请求头不一致/缺失)</summary>
    [MsgKey("error.auth.csrfInvalid")]
    CsrfInvalid = 40023,

    /// <summary>高风险操作需要短时再次认证(约 5 分钟窗口内的 reauth grant 缺失或已过期)</summary>
    [MsgKey("error.auth.reauthRequired")]
    ReauthRequired = 40024,

    /// <summary>Level3 强制档配置不完整(如缺 Redis TLS/认证、缺数据保护密钥等);args 可携带 checkId</summary>
    [MsgKey("error.auth.level3Misconfigured")]
    Level3Misconfigured = 40025,

    /// <summary>绑定 TOTP 时必须先验证目标用户当前密码</summary>
    [MsgKey("error.auth.mfaBindPasswordRequired")]
    MfaBindPasswordRequired = 40026,

    // ── 41xxx 权限与数据范围 ─────────────────────────────────────────

    /// <summary>无接口访问权限(权限码不在当前用户 PermissionCodeList 内)</summary>
    [MsgKey("error.perm.denied")]
    NoPermission = 41001,

    /// <summary>演示模式下禁止写操作</summary>
    [MsgKey("error.perm.demoReadOnly")]
    DemoModeReadOnly = 41002,

    /// <summary>该操作仅限超级管理员执行(角色定义/授权菜单/数据范围配置等);超出路由权限之外的强约束,QA09/QA36</summary>
    [MsgKey("error.perm.superAdminRequired")]
    SuperAdminRequired = 41003,

    /// <summary>目标角色不可转授(未启用/已删除/未标记 IsDelegatable):非超管只能授予"启用+未删除+可转授"的角色,QA36</summary>
    [MsgKey("error.role.notDelegatable")]
    RoleNotDelegatable = 41004,

    /// <summary>目标用户超出当前用户的数据范围,不能为其授予角色,QA36</summary>
    [MsgKey("error.user.outOfDataScope")]
    UserOutOfDataScope = 41005,

    // ── 42xxx 用户 / 组织 / 角色 / 菜单 ──────────────────────────────

    /// <summary>目标用户不存在;args 可携带 name</summary>
    [MsgKey("error.user.notFound")]
    UserNotFound = 42001,

    /// <summary>目标角色不存在</summary>
    [MsgKey("error.role.notFound")]
    RoleNotFound = 42002,

    /// <summary>目标机构不存在</summary>
    [MsgKey("error.org.notFound")]
    OrgNotFound = 42003,

    /// <summary>机构下仍有子机构,不能删除(先移除或迁移子机构)</summary>
    [MsgKey("error.org.hasChildren")]
    OrgHasChildren = 42004,

    /// <summary>目标职位不存在</summary>
    [MsgKey("error.position.notFound")]
    PositionNotFound = 42005,

    /// <summary>登录账号已存在(账号唯一)</summary>
    [MsgKey("error.user.accountExists")]
    AccountExists = 42006,

    /// <summary>超级管理员受保护:不可删除/停用(防自锁死、防提权面被破坏)</summary>
    [MsgKey("error.user.superAdminProtected")]
    SuperAdminProtected = 42007,

    /// <summary>非法父机构:父节点不能是自身(也不应是自身子孙)</summary>
    [MsgKey("error.org.invalidParent")]
    OrgInvalidParent = 42008,

    /// <summary>机构编码已存在(编码唯一)</summary>
    [MsgKey("error.org.codeExists")]
    OrgCodeExists = 42009,

    /// <summary>职位编码已存在(编码唯一)</summary>
    [MsgKey("error.position.codeExists")]
    PositionCodeExists = 42010,

    /// <summary>目标模块/应用不存在</summary>
    [MsgKey("error.module.notFound")]
    ModuleNotFound = 42011,

    /// <summary>模块编码已存在(编码唯一)</summary>
    [MsgKey("error.module.codeExists")]
    ModuleCodeExists = 42012,

    /// <summary>内置模块受保护:不可删除(如 system 模块)</summary>
    [MsgKey("error.module.protected")]
    ModuleProtected = 42013,

    /// <summary>无该模块访问权(未被授权该模块下任何菜单;设默认应用时校验)</summary>
    [MsgKey("error.module.accessDenied")]
    ModuleAccessDenied = 42014,

    /// <summary>目标菜单不存在</summary>
    [MsgKey("error.menu.notFound")]
    MenuNotFound = 42015,

    /// <summary>菜单下仍有子节点,不能删除(先移除或迁移子节点)</summary>
    [MsgKey("error.menu.hasChildren")]
    MenuHasChildren = 42016,

    /// <summary>非法父菜单:父节点不存在,或指向自身/自身子孙(会成环导致子树在树上消失)</summary>
    [MsgKey("error.menu.invalidParent")]
    MenuInvalidParent = 42017,

    /// <summary>角色编码已存在(编码唯一)</summary>
    [MsgKey("error.role.codeExists")]
    RoleCodeExists = 42018,

    /// <summary>新口令不满足密码复杂度策略;args 携带 minLength/requireUpper/requireLower/requireDigit/requireSpecial</summary>
    [MsgKey("error.user.passwordTooWeak")]
    PasswordTooWeak = 42019,

    /// <summary>回收站中未找到该已删记录</summary>
    [MsgKey("error.recycle.notFound")]
    RecycleNotFound = 42020,

    /// <summary>恢复时唯一字段冲突(编码/账号已被新记录占用)</summary>
    [MsgKey("error.recycle.uniqueConflict")]
    RecycleUniqueConflict = 42021,

    /// <summary>不支持的回收站实体类型</summary>
    [MsgKey("error.recycle.invalidType")]
    RecycleInvalidType = 42022,

    /// <summary>模块下仍有挂靠菜单,不能删除(先迁移或删除其顶级目录)</summary>
    [MsgKey("error.module.hasMenus")]
    ModuleHasMenus = 42023,

    /// <summary>会话不存在或已下线("我的会话"自助下线;含"不是你的会话"——不区分,防探测他人会话)</summary>
    [MsgKey("error.session.notFound")]
    SessionNotFound = 42024,

    /// <summary>新口令与当前或最近使用过的口令重复(密码历史防重用策略,开关 sys.security.password.historyCount)</summary>
    [MsgKey("error.user.passwordReused")]
    PasswordReused = 42025,

    /// <summary>头像 URL 不合法:仅允许空/空白 或 IFileUrlSigner 签出的本地直链,拒绝外部/未签名地址(QA25.3 防注入)</summary>
    [MsgKey("error.user.avatarUrlInvalid")]
    AvatarUrlInvalid = 42026,

    /// <summary>机构下仍有在职用户,不能删除(先移除或迁移用户,QA10)</summary>
    [MsgKey("error.org.hasUsers")]
    OrgHasUsers = 42027,

    /// <summary>职位下仍有在职用户,不能删除(先移除或迁移用户,QA10)</summary>
    [MsgKey("error.position.hasUsers")]
    PositionHasUsers = 42028,

    /// <summary>不能对自己执行此操作(QA10:防管理员自行停用/删除/修改自己)</summary>
    [MsgKey("error.user.cannotOperateSelf")]
    CannotOperateSelf = 42029,

    /// <summary>目标机构不在当前用户的数据范围内(QA08:机构/用户管理越权写入)</summary>
    [MsgKey("error.org.outOfScope")]
    OrgOutOfScope = 42030,

    // ── 43xxx 字典 / 配置 ────────────────────────────────────────────

    /// <summary>字典类型不存在</summary>
    [MsgKey("error.dict.typeNotFound")]
    DictTypeNotFound = 43001,

    /// <summary>字典类型编码已存在(编码唯一)</summary>
    [MsgKey("error.dict.typeCodeExists")]
    DictTypeCodeExists = 43002,

    /// <summary>字典项不存在</summary>
    [MsgKey("error.dict.itemNotFound")]
    DictItemNotFound = 43003,

    /// <summary>系统配置不存在</summary>
    [MsgKey("error.config.notFound")]
    ConfigNotFound = 43004,

    /// <summary>配置键已存在(键唯一)</summary>
    [MsgKey("error.config.keyExists")]
    ConfigKeyExists = 43005,

    /// <summary>种子数据受保护(Id &lt; 1000):不可删除,须通过数据库直接操作</summary>
    [MsgKey("error.dict.seedProtected")]
    SeedDataProtected = 43010,

    /// <summary>字典项 Value 在该类型下已存在(含软删行;唯一约束)</summary>
    [MsgKey("error.dict.itemValueExists")]
    DictItemValueExists = 43011,

    // ── 44xxx 文件上传 ───────────────────────────────────────────────

    /// <summary>空文件(未选择文件或文件长度为 0)</summary>
    [MsgKey("error.file.empty")]
    FileEmpty = 44001,

    /// <summary>文件超过大小上限;args 可携带 maxSizeMb</summary>
    [MsgKey("error.file.tooLarge")]
    FileTooLarge = 44002,

    /// <summary>文件后缀不在白名单;args 可携带 ext</summary>
    [MsgKey("error.file.extNotAllowed")]
    FileExtNotAllowed = 44003,

    /// <summary>文件记录不存在(或物理文件已丢失)</summary>
    [MsgKey("error.file.notFound")]
    FileNotFound = 44004,

    /// <summary>分片上传缺少分片(合并时发现空缺);args 可携带 index</summary>
    [MsgKey("error.file.chunkMissing")]
    ChunkMissing = 44005,

    /// <summary>分片合并后内容哈希与客户端声明不一致(传输损坏/漏片)</summary>
    [MsgKey("error.file.chunkHashMismatch")]
    ChunkHashMismatch = 44006,

    // ── 45xxx 消息通知 ───────────────────────────────────────────────

    /// <summary>通知不存在(或已被删除)</summary>
    [MsgKey("error.notice.notFound")]
    NoticeNotFound = 45001,

    /// <summary>定向发布(按角色/按用户)必须至少指定一个接收目标(QA25.2)</summary>
    [MsgKey("error.notice.receiverRequired")]
    NoticeReceiverRequired = 45002,

    /// <summary>定向发布的接收目标(角色/用户)不存在或已停用/已删除(QA25.2,发布前整体拒绝、不落库)</summary>
    [MsgKey("error.notice.receiverNotFound")]
    NoticeReceiverNotFound = 45003,

    // ── 46xxx 导入 / 导出 ────────────────────────────────────────────

    /// <summary>未安装 TenonAdmin.Excel(或未在 AddTenonAdmin() 之前调 AddTenonAdminExcel())</summary>
    [MsgKey("error.excel.providerMissing")]
    ExcelProviderMissing = 46001,
    /// <summary>导入文件为空</summary>
    [MsgKey("error.import.fileEmpty")]
    ImportFileEmpty = 46002,
    /// <summary>导入行数超过 TenonAdmin:Excel:MaxImportRows</summary>
    [MsgKey("error.import.rowLimitExceeded")]
    ImportRowLimitExceeded = 46003,
    /// <summary>必填列没有被任何表头映射上(列级,不属于任何一行)</summary>
    [MsgKey("error.import.columnMissing")]
    ImportColumnMissing = 46004,
    /// <summary>单元格必填但为空</summary>
    [MsgKey("error.import.cellRequired")]
    ImportCellRequired = 46005,
    /// <summary>字典列的值不在该字典的启用项里</summary>
    [MsgKey("error.import.cellDictInvalid")]
    ImportCellDictInvalid = 46006,
    /// <summary>按名查外键失败(机构名/岗位名/角色名/主管姓名在库里找不到)</summary>
    [MsgKey("error.import.cellRefNotFound")]
    ImportCellRefNotFound = 46007,
    /// <summary>单元格格式不合法(日期/数字/邮箱/手机号)</summary>
    [MsgKey("error.import.cellFormatInvalid")]
    ImportCellFormatInvalid = 46008,
    /// <summary>业务键在本文件内重复</summary>
    [MsgKey("error.import.duplicateInFile")]
    ImportDuplicateInFile = 46009,
    /// <summary>业务键在库中已存在(Error 策略下才算错误)</summary>
    [MsgKey("error.import.duplicateInDb")]
    ImportDuplicateInDb = 46010,
    /// <summary>导入行指定的机构不在当前用户的数据范围内(越权写入,§3.4)</summary>
    [MsgKey("error.import.orgOutOfScope")]
    ImportOrgOutOfScope = 46011,
    /// <summary>导出结果超过 TenonAdmin:Excel:MaxExportRows,请先收窄筛选条件</summary>
    [MsgKey("error.export.tooManyRows")]
    ExportRowLimitExceeded = 46012,
    /// <summary>请求导出的列不在该档案的可导列里</summary>
    [MsgKey("error.export.columnInvalid")]
    ExportColumnInvalid = 46013,

    // ── 47xxx 定时任务 ───────────────────────────────────────────────

    /// <summary>任务不存在</summary>
    [MsgKey("error.job.notFound")]
    JobNotFound = 47001,

    /// <summary>任务编码已存在(编码唯一)</summary>
    [MsgKey("error.job.codeExists")]
    JobCodeExists = 47002,

    /// <summary>cron 表达式不合法;args 携带 reason(段位与原因,来自 CronExpression 的 FormatException)</summary>
    [MsgKey("error.job.cronInvalid")]
    JobCronInvalid = 47003,

    /// <summary>触发配置不合法(间隔 &lt; 5 秒 / 一次性时刻已过 / 必填字段缺失)</summary>
    [MsgKey("error.job.triggerInvalid")]
    JobTriggerInvalid = 47004,

    /// <summary>编译处理器未注册(HandlerName 在 DI 集合里无匹配);args 携带 handlerName</summary>
    [MsgKey("error.job.handlerNotFound")]
    JobHandlerNotFound = 47005,

    /// <summary>串行任务上次执行未结束(SerialSkip 语义;「执行一次」时校验)</summary>
    [MsgKey("error.job.alreadyRunning")]
    JobAlreadyRunning = 47006,

    /// <summary>目标执行记录不在运行中(终止无从谈起)</summary>
    [MsgKey("error.job.runNotAlive")]
    JobRunNotAlive = 47007,

    /// <summary>SQL 任务未启用(TenonAdmin:Jobs:Sql:Enabled 总闸,默认关)</summary>
    [MsgKey("error.job.sqlDisabled")]
    JobSqlDisabled = 47008,

    /// <summary>HTTP 任务 URL 被围栏拒绝(非 http/https / 不在白名单 / 命中 CIDR 黑名单)</summary>
    [MsgKey("error.job.httpUrlBlocked")]
    JobHttpUrlBlocked = 47009,

    /// <summary>状态流转非法(如 enable 后重算仍无未来时刻)</summary>
    [MsgKey("error.job.statusConflict")]
    JobStatusConflict = 47010,

    /// <summary>属性包缺键或畸形;args 携带 key</summary>
    [MsgKey("error.job.propsInvalid")]
    JobPropsInvalid = 47011,

    /// <summary>执行记录不存在</summary>
    [MsgKey("error.job.logNotFound")]
    JobLogNotFound = 47012,

    /// <summary>在飞执行数已达 MaxConcurrentRuns 上限</summary>
    [MsgKey("error.job.runLimitReached")]
    JobRunLimitReached = 47013,

    /// <summary>内置任务(IsSystem)禁删</summary>
    [MsgKey("error.job.protected")]
    JobProtected = 47014,

    // ── 50xxx 系统内部 ───────────────────────────────────────────────

    /// <summary>
    /// 未知系统错误。用于操作日志里记录未捕获异常的结果码(详情只进日志不出接口)。
    /// 注(P2-2):框架级 400(模型校验/ProblemDetails)与未捕获 500 目前<b>不</b>套本信封——
    /// 程序缺陷该大声失败(见 AdminExceptionFilter)。要让 400/500 也走统一信封,需自写
    /// InvalidModelStateResponseFactory + UseExceptionHandler,留待需要面向非浏览器调用方统一形状时再做。
    /// </summary>
    [MsgKey("error.system.internal")]
    SystemError = 50000,
}

/// <summary>
/// 为 <see cref="ErrorCode"/> 成员标注语义 msgKey(前端 i18n 语言包的键)。
/// 显式标注而非命名约定推导,保证"码 → 键"的映射一目了然、重命名枚举不破坏契约。
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class MsgKeyAttribute(string key) : Attribute
{
    /// <summary>语义键,形如 <c>error.auth.passwordWrong</c></summary>
    public string Key { get; } = key;
}

/// <summary><see cref="ErrorCode"/> 的辅助扩展</summary>
public static class ErrorCodeExtensions
{
    // 启动后首次访问时反射一次,固化为 FrozenDictionary(只读、查询最快),此后零反射开销。
    private static readonly FrozenDictionary<ErrorCode, string> MSG_KEYS =
        typeof(ErrorCode).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (Code: (ErrorCode)f.GetValue(null)!, Key: f.GetCustomAttribute<MsgKeyAttribute>()?.Key))
            .Where(x => x.Key is not null)
            .ToFrozenDictionary(x => x.Code, x => x.Key!);

    /// <summary>
    /// 取错误码的语义 msgKey;未标注的成员回退为 <c>error.code.{数值}</c>(可用但不该出现,新增码务必标注)。
    /// </summary>
    public static string GetMsgKey(this ErrorCode code) =>
        MSG_KEYS.TryGetValue(code, out var key) ? key : $"error.code.{(int)code}";
}
