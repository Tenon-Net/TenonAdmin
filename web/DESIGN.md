# TenonAdmin 设计系统规范(DESIGN.md)

> 设计单源:`../docs/rebuild-design.md` §7。**tokens 单源:[`src/styles/tokens.css`](src/styles/tokens.css)** —— 一切颜色/字号/间距/圆角/阴影都从那里的 CSS 变量取,本文件只做规范说明与「token → Naive UI」映射。
> 视觉来源(**已改版**):RBAC 后台原型 [`design-mockups/design_handoff_rbac_admin/`](design-mockups/design_handoff_rbac_admin/README.md)(权威说明 = 其 `README.md`);旧稿 `design-mockups/design-tokens.dc.html` 留档。本版细化**圆角(更圆)/ 阴影 / 顶栏毛玻璃 / 侧栏配色 / 主色派生规则 / 7 主色候选 / 密度档**。
> **色板已换(theme-refresh,2026-07)**:中性/语义色整层采 daisyUI(MIT)企业向主题——亮色 corporate、暗色 business(OKLCH 原值转 sRGB hex,warning/error/三级文字按可读性加深)。**默认 UI 主色为品牌深蓝 `#0052D4`**(与 React 模板一致,取自新版品牌图标的深蓝基调);圆角/字号/间距/阴影体系不变(daisyUI 的 radius 4px/depth 0 未采用)。手法补充:卡片 1px 描边(styles/index.css 质感层)、表头次级色+600 字重(naive-theme.ts)。

---

## 1. 设计基调

企业级、精简、留白充分、克制。品牌深蓝主色(`#0052D4`)+ corporate 中性灰阶(深端带藏蓝墨色),暗色为 business 灰。信息密度按后台管理(正文 14/22,表格可紧凑),明暗双主题对等。参考观感:daisyUI corporate/business 色系 + Soybean / Naive UI Admin 的质感手法。

## 2. Design Tokens(概览,权威值见 `tokens.css`)

分四层,业务**只消费角色令牌层**,不直接引原语:

| 层 | 令牌 | 说明 |
|---|---|---|
| 原语·中性 | `--color-gray-50 … --color-gray-900` | Arco 系 10 级灰阶,固定值 |
| 原语·主色 | `--color-primary` / `-hover` / `-pressed` / `-light` | 四档态 |
| 原语·语义 | `--color-{success,warning,danger,info}` + `-bg` | base + 浅底 |
| **角色·背景** | `--color-bg-{body,container,elevated}` | 页面/容器/弹层 |
| **角色·文字** | `--color-text-{primary,secondary,tertiary,disabled}` | 四级 |
| **角色·线/填充** | `--color-border` / `-border-strong` / `--color-fill` / `-fill-hover` / `--color-mask` | |
| 字号 | `--font-size-{xs,sm,base,md,lg,xl}` = 12/13/14/16/20/24,配 `--line-height-*` | 正文 base 14/22 |
| 间距 | `--space-{4,8,12,16,24,32}` | 4px 基准网格 |
| 圆角 | `--radius-{sm,md,lg,xl}` = 6/10/12/16 | 标签 sm、输入/按钮 md、卡片/面板 lg、登录卡 xl |
| 阴影 | `--shadow-{1,2,3}` | 卡片/弹层·hover 上浮/悬浮·抽屉,暗色更重 |
| 顶栏底 | `--color-header-bg` | 半透明,配 `backdrop-filter: blur(12px)` |

**明暗切换**:`<html data-theme="dark">` 即暗色,不打即亮色。角色令牌/主色/语义/阴影在 `[data-theme="dark"]` 下整体翻转,原语灰阶与度量层不变。

## 3. 布局

竖向侧边栏骨架(§7.4):左侧可折叠侧边栏(展开 **236** / 收起 **76**,收起仅留图标)+ 顶栏(高 **62**,`--color-header-bg` + `backdrop-filter: blur(12px)`;折叠按钮 · 面包屑 · 全局搜索 · 主题切换 · 主色选择 · 密度 · 语言 · 用户)+ 内容区(可选多页签 Tabs)。侧栏 `--color-bg-container`、内容区 `--color-bg-body`;当前菜单项底 `--color-primary-light` + 左侧 3px 主色指示条(带 `glow`)。暗色侧栏选中态可用主色 90° 线性渐变(`rgba(accent,.24)→.06`)。

## 4. 核心页面形态

- **列表页**:筛选区(SearchForm)+ 工具栏(新增/批量/列设置)+ 数据表(状态用语义标签)+ 分页。表头 `--color-fill`,行 hover `--color-fill-hover`。
- **树 + 表联动**:左树(机构 / 菜单)+ 右表,树选中过滤右表。
- **表单弹窗 / 抽屉**:轻量单步用弹窗(`--color-bg-elevated` + `--shadow-2`),多字段/分步用抽屉。
- **角色授权面板**:上半可勾选菜单权限树(目录/页面/按钮三级,父子联动)+ 下半数据范围单选(全部/本机构/本机构及以下/仅本人/自定义,选自定义展开机构多选树)。
- **工作台**:统计卡片行(`--shadow-1`)+ 最近登录日志 / 快捷入口,克制不堆图表。

## 5. 组件规范

- **按钮层级**:主 `--color-primary`(hover→`-hover`,pressed→`-pressed`)/ 次要(描边 `--color-border` + 文字 `--color-text-secondary`)/ 文本 / 危险(`--color-danger`)/ 禁用(`--color-fill` + `--color-text-disabled`)。高 34,圆角 `--radius-md`。
- **表格密度**:舒适(行高 58)/ 紧凑(行高 48)两档(运行时可切,存 `app.density`;联动页内边距 24/18、卡片间距 16/12、卡片内边距 20×22/16×18);数值列 `tabular-nums`。
- **状态反馈**:标签 = 语义 base 文字 + `-bg` 底 + 圆点;空/加载/错误态统一走 Naive 内建占位。
- **主色可换(运行时)**:7 候选 `#0052D4`(品牌默认)/ `#646CFF` / `#7C5CFF` / `#0EA5E9` / `#EC4899` / `#F97316` / `#10B981`;切主色 = 按 §7 派生规则从 accent 重算 `--color-primary*`(写到 `document.documentElement`)+ 重建 Naive `themeOverrides`,存 `app.accent`(旧候选仍有效,非法值由 afterHydrate 回落默认)。
- **英雄元素**:登录页主按钮 = accent 实心 + hover 上浮 `translateY(-2px)`,层级靠 48 高/600 字重/整行宽,**不加渐变与光晕**(与 React 模板的平面主色按钮一致);渐变 `btnGrad` 只留给欢迎横幅的占位头像。**应用内常规按钮走 Naive 平面主色**。环境动画(柔光/扫光)默认关(沉稳档)。

## 6. 可访问性

实测对比度(WCAG,theme-refresh 中性/语义 + 品牌深蓝主色,两主题):**主文字** on 页底/容器 亮 15.1–17.2:1 / 暗 10.3–10.7:1、**次文字** 亮 6.3:1 / 暗 5.7:1 —— 均过 AA(≥4.5)。**占位/辅助文字**(`--color-text-tertiary` #85868E / 暗 #6E6E6E)亮 3.6:1 / 暗 3.2:1、**白字 on 主色按钮** 亮(`#0052D4`)约 6.7:1 / 暗(`#2E71DC`)约 4.7:1、**语义色文字** on 容器 亮 3.3–4.2:1(warning 已从 daisyUI 原值加深至 #A88400 保 3.5:1)/ 暗 4.4–7.9:1 —— 主色按钮过 AA;语义色仍只用于非正文的提示/大字/按钮/标签场景,若某处放 14px 正文级用途需改用 `--color-text-secondary`。焦点态保留可见描边;交互控件键盘可达;语义色不作唯一区分手段(配文字/图标)。

---

## 7. token → Naive UI `GlobalThemeOverrides` 映射

落地时(下一刀脚手架)把 tokens 喂给 `n-config-provider :theme-overrides`。JS 侧读取 CSS 变量:`getComputedStyle(document.documentElement).getPropertyValue('--color-primary')`,或直接引常量。切换 `data-theme` 后重算即得暗色主题对象。核心 `common` 段:

| Naive `common.*` | token |
|---|---|
| `primaryColor` | `--color-primary` |
| `primaryColorHover` | `--color-primary-hover` |
| `primaryColorPressed` | `--color-primary-pressed` |
| `primaryColorSuppl` | `--color-primary-hover` |
| `successColor` / `warningColor` / `errorColor` / `infoColor` | `--color-success` / `--color-warning` / `--color-danger` / `--color-info` |
| `bodyColor` | `--color-bg-body` |
| `cardColor` / `tableColor` | `--color-bg-container` |
| `modalColor` / `popoverColor` | `--color-bg-elevated` |
| `textColorBase` / `textColor1` | `--color-text-primary` |
| `textColor2` | `--color-text-secondary` |
| `textColor3` / `placeholderColor` | `--color-text-tertiary` |
| `textColorDisabled` | `--color-text-disabled` |
| `borderColor` / `dividerColor` | `--color-border` |
| `inputColor` 描边强调 | `--color-border-strong` |
| `actionColor` / `tableHeaderColor` | `--color-fill` |
| `hoverColor` | `--color-fill-hover` |
| `modalMaskColor` / `popoverMaskColor` | `--color-mask` |
| `borderRadius` | `--radius-md` |
| `borderRadiusSmall` | `--radius-sm` |
| `fontSize` / `fontSizeMedium` | `--font-size-base` |
| `fontSizeMini/Tiny/Small` | `--font-size-xs` / `xs` / `sm` |
| `fontSizeLarge/Huge` | `--font-size-md` / `lg` |
| `fontFamily` | `--font-family-base` |
| `fontFamilyMono` | `--font-family-mono` |
| `boxShadow1` / `boxShadow2` / `boxShadow3` | `--shadow-1` / `-2` / `-3` |

### 7.1 派生规则(权威;`web/src/theme/mix.ts` 逐字实现)

`mix(a, b, t)` = 两色按 `t∈[0,1]` 线性插值(sRGB 分量)。以 accent(6 候选之一)为输入:

| 目标 | 亮色 | 暗色 |
|---|---|---|
| `primary`(基准) | `accent` | `mix(accent, #FFF, .18)` |
| `primary-hover` | `mix(primary, #FFF, .16)` | 同左 |
| `primary-pressed` | `mix(primary, #000, .18)` | 同左 |
| `primary-light` | `mix(primary, #FFF, .90)` | `mix(primary, bg-container, .82)` |
| 语义 hover/pressed/suppl | base:hover=`mix(base,#FFF,.16)`、pressed=`mix(base,#000,.18)`、suppl=hover | 同规则(暗色 base 取暗色令牌值) |
| 语义徽章底 | `mix(base, #FFF, .88)` | `mix(base, bg-container, .80)` |
| 语义徽章字 | `mix(base, #000, .06)` | `mix(base, #FFF, .18)` |

头像专用(仅欢迎横幅的占位头像,自定义 CSS,不入 Naive 覆写):
- `btnGrad(accent)` = `linear-gradient(135deg, accent 0%, mix(accent,#8B5CF6,.55) 55%, mix(accent,#EC4899,.62) 100%)`

登录页主按钮**不用**上式,取 `accent` 实心、无光晕(§5)。

> Naive `common.borderRadius`←`--radius-md`(10)覆盖多数控件;卡片圆角(lg=12)、`DataTable` 紧凑档行高等按组件细粒度覆写(`Card`/`DataTable`),脚手架刀在 `naive-theme.ts` 补。

## 8. 登录页品牌双栏

默认登录皮肤为 `split`（2026-10）。保留 `aurora` / `spotlight`、`?skin=` 一次性预览和本地皮肤记忆，不覆盖用户已保存的选择。皮肤入口收为右上角菜单，明暗与语言切换保持独立。

- 视觉参考：用户提供的 `01_light_brand.png` 的双栏、留白与建筑场景；深色取 `02_dark_material.png` 的石墨曲面材质。背景不承载文字、Logo 或表单；真实品牌继续来自 `useSite` / `TenonLogo`。
- 桌面左右等宽，左侧一处品牌、一组两行标题与三个现有能力标签，右侧表单卡片最大宽度 560px。明暗主题共用结构，不重挂载表单、不丢失输入；关闭环境动画。
- 900px 及以下隐藏场景与宣传内容，保留品牌和工具栏。480px 及以下去掉表单外卡片，保证输入宽度；页面可纵向滚动，短屏及二次验证不裁切。
- 登录、验证码、短信、TOTP、SSO、强制绑定与待绑定确认继续由 `LoginForm` 及已有 composable 负责；SSO 只显示后台启用项，不添加注册、组织、忘记密码等示意功能。
- 双栏表单使用 `presentation="brand"`：正常登录显示欢迎标题与说明，挑战态显示自己的标题；输入框 46px、主按钮 48px，实心品牌色，无光晕。所有文案有中英文版本。
- 验证覆盖 1448×1086 参考尺寸、1440×900、1366×768 和 320/375/414/768px 宽度，检查内容可达、无横向溢出、明暗及语言切换保留输入。

登录场景颜色集中在 `src/styles/tokens.css` 的 `--color-login-*` / `--shadow-login`，由全局明暗主题切换；表单只局部覆盖 Naive 输入框表面和对比度，不新建全局主题。

## 9. 登录页「概览」皮肤

`product` 以用户提供的 `04_light_product.png` 为初始参考，经页面实测调整为完整产品截图与登录表单的双栏布局；`split` 继续作为默认皮肤。通过右上角皮肤菜单选择「概览」，或用 `/login?skin=product` 一次性预览。

- 居中圆角面板最大1200px，左右约55%/45%。左侧以业务主张、完整的真实Vue用户管理截图、紧凑的三项能力说明构成产品展示；右侧为现有登录表单和轻量文档链接。品牌统一放在页头，避免重复 Logo 和站名。
- 截图处于正常文档流，不做大角度透视、绝对定位或侧栏裁切；面板高度随内容增长，短屏允许滚动。标题使用均衡换行，手机保留带边界的表单卡片。
- 截图为静态本地WebP，来自仓库文档资产；匿名页不加载用户、统计或权限业务数据。原图中文保留，说明文本支持中英文。
- 深色沿用相同结构和登录场景令牌，截图保留真实产品配色。900px以下隐藏产品宣传区，只保留品牌和表单；短屏可纵向滚动。
- 复用 `LoginForm` 的认证状态和 `loginTheme.ts` 的输入尺度、对比度配置。主题或语言切换不重挂载表单，保留输入。
- 文档入口使用项目真实站点，随语言指向中文或英文首页；没有添加参考图中的组织字段、注册或虚构演示入口。


## 10. 登录页「简约」皮肤

`light-brand` 为 Vue 与 React 各自独立实现的皮肤，通过菜单选择「简约」或 `/login?skin=light-brand` 预览。Vue 保留当前 `split` 默认值，React 保留 `aurora` 默认值。

- 依据 `01_light_brand.png` 的等宽双栏、白色卡片和浅色几何场景实现；深色使用同一素材与布局，不混用其他候选图。
- 正式 Logo 与站点标题来自站点配置，介绍文案按 README 的定位描述 NuGet 接入、业务独立开发、权限及双前端，提供中英文版本。
- 图片仅承载装饰，文字、表单和工具栏均为真实组件。输入框约 58px、主按钮约 60px；验证码、短信、TOTP 与已配置的第三方认证继续由共享表单处理。
- 900px 及以下隐藏装饰区，保留品牌、卡片和完整表单；内容较多时允许纵向滚动，支持键盘焦点和减少动态效果偏好。
- 开发环境可添加 `&previewSso=1` 查看 GitHub、Gitee、微信按钮外观。按钮禁用并标明仅预览，不注册提供方、不发起认证；生产构建不启用此入口，正常页面只展示后端启用的提供方。
- 登录提交入口增加忙碌状态防护，防止连续 Enter 重复提交。接口、Token、会话与后端安全配置保持原有行为。
