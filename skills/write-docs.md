# 文档写作规范（Write Docs）

编写、修改或翻译 `site/` 页面及根目录 `README*.md` 前，先读这一份。中文是母版，其他语言按母语表达并保持事实对齐。脚本判得准的进闸门，判不准的靠人工校对。

总纲：机器感来自重复槽位和固定句式，不来自某几个禁词。先看真实文本，再决定删改；范例用于示范判断，不提供可批量套用的句型。

## 写作定位与阅读顺序

文档站和 README 都面向正在选用或接入框架的开发者。语气直接、克制、具体：说清现有能力、操作结果和使用条件，让读者能够判断是否适合自己的项目。

- 开头和过渡的语气按[三、声音](references/write-docs/site-prose.md#三声音)，首句按[四、开头](references/write-docs/site-prose.md#四开头)。优先给事实或判断，解释只保留会影响操作的部分。
- 使用具体对象和动作。介绍数据权限时，说明部门经理能查询哪些客户记录，比列出一串权限术语更有用。
- 功能描述须有源码、配置或已验证示例支持。默认可用、需要配置、可选包、需要业务项目自行实现，分别在对应能力旁交代。
- 表格用于并列比较，列表用于步骤或独立规则，连续解释写成段落。具体的句式与强调判断见[八、手感项](references/write-docs/site-prose.md#八手感项)。

按下面的交付物分支读取对应参考。共同流程维护在本文件，具体规则只在对应参考中维护；技能入口与 AGENTS.md 仍只指向本文件。

## README 分支

修改根目录 `README*.md` 时，读取 [README 写作分支](references/write-docs/readme.md)。它包含 README 与文档站的分工、内容结构、截图、多语言同步和人工验收；再按改动内容加载站点规范中适用于 README 的声音、开头、标点或手感章节。

## 站点页面分支

修改或翻译 `site/` 页面时，按改动查阅 [站点正文写作规范](references/write-docs/site-prose.md) 的相关章节：正文措辞看声音与手感，首段看开头，翻译看中英对齐，验证看 prose 闸门与逐页自检。局部拼写修正可复用未变化的已读规则。

执行顺序：

1. 同时检查目标中文页、对应英文页及相关源码；先确认事实归属和页面边界。
2. 以中文为母版修改，再按英语母语表达同步事实与 H2/H3 结构。代码块中的真实字面量按参考规范判断是否可译。
3. 在 `site/` 下对每个改动页运行 `node scripts/lint-prose.mjs <相对路径>`；修改闸门脚本时先跑 `--selftest`，再跑全站扫描。
4. 按参考规范的自检清单人工校对；一批页面完成后横向比较开头，清除重复句式。

只改一页就只验证这一页；改了共享导航、配置或闸门，再扩大验证范围。

## 沉淀位

| | |
|---|---|
| 单一真相源 | `skills/write-docs.md` 维护共同流程；`skills/references/write-docs/` 分别维护 README 与站点正文分支 |
| Claude 入口 | `.claude/skills/write-docs/SKILL.md`（`/write-docs`） |
| Codex / agents 入口 | `.agents/skills/write-docs/SKILL.md` |
| 闸门实现 | `site/scripts/lint-prose.mjs`，头注指回本文 |
| 闸门执行 | `.github/workflows/docs.yml` 的 `lint` job：selftest → 全站扫，push 与 PR 都跑 |
| 指路 | `AGENTS.md` 的任务参考；`CLAUDE.md` 导入该文件 |
