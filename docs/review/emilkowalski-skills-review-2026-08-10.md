# emilkowalski/skills 调研

检查日期：2026-08-10（首次调研）；补充复核：2026-08-10。

## 结论

[`emilkowalski/skills`](https://github.com/emilkowalski/skills) 是面向设计师和工程师的 UI 设计与动效技能集合，不是可运行的组件库或框架。它适合作为双前端模板的动效质量参考，尤其能补足进入/退出、交互频率、性能和无障碍检查；不适合整包、无版本锁地接入 TenonAdmin。

**补充复核的关键发现**：10 个技能里只有 3 个（`review-animations`、`prototype`、`pick-ui-library`）在 frontmatter 显式声明 `disable-model-invocation: true`，即仅在被明确调用时才运行；其余 7 个（`animate`、`animation-vocabulary`、`apple-design`、`ask-sonner`、`emil-design-eng`、`find-animation-opportunities`、`improve-animations`）没有这个字段，会按 `description` 语义匹配自动触发。[review-animations frontmatter](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/skills/review-animations/SKILL.md#L1-L5) [prototype frontmatter](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/skills/prototype/SKILL.md#L1-L5) [animate frontmatter（无该字段）](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/skills/animate/SKILL.md#L1-L5) 若原样vendor这 7 个，等于让"要不要加动效/该用哪个曲线"这类判断在本仓任何前端任务里被动触发，这与本仓技能一律经 slash command 显式调用的既有约定（见 `CLAUDE.md` "Agent skills" 一节）以及刚安装的 `ponytail`（极简/YAGNI 倾向）在行为哲学上是冲突的——即便动效规则本身没问题。因此**vendor 时应对全部技能副本强制补写 `disable-model-invocation: true`**，不保留上游的自动触发行为。

另外核实：`web/DESIGN.md` 目前完全没有动效相关章节（未搜到 `animat`/`transition`/`motion`/`ease`/`duration` 关键字），`web-react` 同理。也就是说这不是"参考一份已有规范之外的资料"，而是**在真空里为两个独立模板确立第一份动效规范**——选择就此成为需要认真决策的产品选型，不只是"顺手引进"。

## 内容与机制

- README 以 `npx skills@latest add emilkowalski/skills` 安装，并列出 10 个 Markdown 技能：动画构建和审查、全仓动效审计、动效机会发现、术语、Apple 设计、UI 库选择、原型和 Sonner 指南。[README](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/README.md#L23-L27) [技能清单](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/README.md#L41-L52)
- `animate` 以顺序决策约束实现：先判断该不该动，再确定目的、选择最低成本的技术、属性、曲线、时长、中断与降级策略。它要求高频操作不加动画、优先 `transform`/`opacity`，并随动效交付 `prefers-reduced-motion` 和 hover 指针条件。[animate](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/skills/animate/SKILL.md#L30-L79)
- `review-animations` 将这些规则变为严格评审门槛（十条硬性标准 + 升级触发词 + 修复优先级 + 表格化输出格式，仅要求 Block/Approve 二选一的明确结论）；`prototype` 则要求在隔离的原型路由中提供 3 个真正不同的可交互方案，用户选定后才进入生产代码，且承诺"选定前绝不碰生产代码"。两者都是仅显式调用（`disable-model-invocation: true`），可独立引入而不影响日常自动行为。[review-animations](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/skills/review-animations/SKILL.md#L23-L96) [prototype](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/skills/prototype/SKILL.md#L17-L30)
- `pick-ui-library` 是一份"任务 → 指定第三方库"速查表（toast 用 Sonner、下拉/弹层用 base-ui、状态用 zustand、虚拟列表用 Virtuoso 等），会直接与本仓既有的 Naive UI（`web/`）和 antd 6（`web-react/`）组件体系及 `web-react/COMPONENTS.md`/`web/COMPONENTS.md` 中已封装的组件冲突或重复。[pick-ui-library](https://github.com/emilkowalski/skills/blob/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278/skills/pick-ui-library/SKILL.md#L15-L58)

## 对 TenonAdmin 的建议（可执行方案）

**引入范围**：只 vendor `review-animations`；`animate` 可选，且必须改写为仅显式调用后再引入。不引入 `prototype`（原型选型流程与本仓 issue/PRD 工作流不同，价值有限）、`pick-ui-library`（依赖建议直接对撞既有 UI 栈）、`ask-sonner`（本仓无 Sonner 依赖）、`improve-animations`（默认产出 `plans/` 拆分任务，需先改造成本仓的 issue 流程）、`animation-vocabulary`/`apple-design`/`emil-design-eng`/`find-animation-opportunities`（价值主要是灵感读物，不必占用技能槽位，需要时直接读源文件即可）。

**vendor 方式**：复用本仓已有的 `daisyui` 模式——技能正文放在 `.claude/skills/<name>/SKILL.md`，来源元数据记进仓库根目录的 `skills-lock.json`。参考现有条目：

```json
{
  "version": 1,
  "skills": {
    "daisyui": {
      "source": "saadeghi/daisyui",
      "sourceType": "github",
      "skillPath": "skills/daisyui/SKILL.md",
      "computedHash": "8ae127fa514329a854b89aa6620554e4ae5fbab78a099f4c46730946f0406bbc"
    },
    "review-animations": {
      "source": "emilkowalski/skills",
      "sourceType": "github",
      "sourceRef": "e48aeea9b5e4fe682f8e83b82440e3d43a9a6278",
      "skillPath": "skills/review-animations/SKILL.md",
      "computedHash": "<vendor 时计算>"
    }
  }
}
```

固定到当前 `main` 最新提交 `e48aeea9b5e4fe682f8e83b82440e3d43a9a6278`（复核时仍是最新），而不是 README 里依赖 `@latest` 的浮动安装命令；同时拷贝 `STANDARDS.md`（`review-animations` 的规则详表，被正文引用）、保留 MIT `LICENSE` 归属。

**改写要点**（区别于原样复制）：
1. 把 `transform-origin`、Base UI 变量名等框架专属示例，换成本仓 Naive UI / antd 6 的等价写法。
2. 给 `web/DESIGN.md` 和 `web-react/COMPONENTS.md` 各补一小节"动效基线"，把 `review-animations` 里的十条标准落成本仓可引用的固定规范，而不是让规则只活在技能文件里。
3. 无论上游是否已声明，vendor 后的每个技能都显式写 `disable-model-invocation: true`，保证只能通过 slash command 或显式请求触发，与本仓其余技能的调用方式一致。

## 成熟度与风险

截至复核时，GitHub API 显示 27,911 stars、1,545 forks、MIT 许可、**0 个 open issues**、最新提交仍是 `e48aeea9`（2026-08-10），与首次调研时数据一致，未发现新变化。项目创建于 2026-03、没有 tags/releases，贡献集中于作者，因此社区关注度不等于稳定的发布治理；0 个 open issues 更可能反映使用者少或反馈渠道未激活，而非零缺陷。将其视为可审阅的灵感和规则来源，而非带兼容性承诺的依赖。[仓库元数据](https://api.github.com/repos/emilkowalski/skills) [最新提交](https://github.com/emilkowalski/skills/commit/e48aeea9b5e4fe682f8e83b82440e3d43a9a6278) [tags](https://api.github.com/repos/emilkowalski/skills/tags) [贡献者](https://api.github.com/repos/emilkowalski/skills/contributors?per_page=100)
