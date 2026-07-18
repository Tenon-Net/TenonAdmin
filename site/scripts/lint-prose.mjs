#!/usr/bin/env node
// 文档散文闸门。规矩的真相源是 skills/write-docs.md，这里只实现其中【机器判得准】的部分。
//
// 铁律（违反 → 非零退出）：
//   1. 中文正文里半角 , : 紧贴中文字符  → 应为全角 ，：
//   2. 中文正文里半角 ( ) 包裹中文内容  → 应为全角 （）
//   3. 正文第一句以「这页/本页/这一页/这篇」开头（朗读目录的入口）
//   4. 英文正文第一句以 This page / This guide / This section / This document 开头
// 手感项（只打印，不拦）：
//   5. 每页破折号 —— 超过 2 个
//
// 判据是「紧贴中文」而不是「出现半角」：Result<T>、AddTenonAdmin()、TenonAdmin:Database
// 里的半角是合法的。代码块、行内代码、链接目标一律剥掉后再判，否则误报能把这个闸门废掉。

import { readFileSync } from 'node:fs'
import { globSync } from 'node:fs'

const CJK = '\\u4e00-\\u9fff'
const EMDASH_BUDGET = 2

const blank = (m) => m.replace(/[^\n]/g, ' ')

// 只剥围栏代码块。行内代码留着——破折号的标签位判定要靠 `术语`——释义 里的反引号认形状，
// 剥成空白就再也认不出来了。
function stripFences(src) {
  return src.replace(/^```[\s\S]*?^```/gm, blank)
}

// 强调标记把中文和它后面的标点隔开，三条「紧贴中文」的规则就全看不见了：
// `**验证参数**(TenonAdminSetup.cs)`、`**两端都要配**:` 里的半角，读者看得见，规则看不见。
// 抹成等长空白不行——那要给逗号、冒号、括号三条规则各加空白容差，
// 而容差会顺带把 `` `Result<T>`: 说明 `` 这类几百处一起卷进来，那是另一个决定，不该由这个补丁夹带。
// 所以把标记【挪到前面】而不是原地抹掉：长度不变、行号不变，被强调的文字与后一个字符恢复相邻。
function shiftEmphasis(src) {
  return src.replace(/(\*\*|__)([^*_\n]+)\1/g, (_, mark, inner) => '    ' + inner)
}

// 剥掉不该被判标点的部分，用等长空白占位以保住行号与列号。
function stripCode(src) {
  return shiftEmphasis(stripFences(src))
    .replace(/`[^`\n]*`/g, blank) // 行内代码
    .replace(/\]\([^)\n]*\)/g, blank) // markdown 链接目标（含其中的半角括号）
    .replace(/<!--[\s\S]*?-->/g, blank) // HTML 注释
}

// 正文第一句。
// 引用块和列表【不豁免】：icon-picker.md、components/index.md 的开篇正文就写在 `>` 里，
// 跳过它们等于给「不许朗读目录」开一个后门——把开头写成引用块就能绕过铁律。
function firstProseLine(src) {
  const body = src.replace(/^---\n[\s\S]*?\n---\n/, '')
  for (const [i, raw] of body.split('\n').entries()) {
    const line = raw.trim()
    if (!line) continue
    if (line.startsWith('#')) continue // 标题
    if (line.startsWith(':::')) continue // VitePress 容器标记
    if (line.startsWith('```')) continue
    if (line.startsWith('|')) continue // 表格
    if (line.startsWith('<')) continue // HTML 块
    // 引用块与列表：剥掉标记后当正文判
    const text = line.replace(/^>\s?/, '').replace(/^[-*]\s+/, '').trim()
    if (!text) continue
    return { line: text, lineNo: i + 1 }
  }
  return null
}

// 破折号有三种位置，只有散文位该占额度：
//   占位   `| —— |`          表格空单元格，不是标点，不计数
//   标签位 `**术语**——释义`   干的是冒号的活，机械换 ：，不占额度
//   散文位 句子中间           要重排句子，占额度
// 把标签位算进额度会逼人去改根本没毛病的定义列表——配额达标，好东西改坏。
// 按【形状】判，不按粗体标记判：**术语**—— / `代码`—— / 短词—— 一视同仁。
// 必须【按单元格】判而不是按行：表格里既有 `vite`——dev server 这种标签，
// 也有整段解释性长句，后者住在表格里也依然是散文。
// 术语必须带 **粗体** 或 `代码` 标记——实测全站列表项的标签位 100% 如此，零个裸术语。
// 裸术语只在表格单元格里出现（全站 2 例：「自定义指令——」「小型初始化工具——」），
// 所以裸术语分支只对表格开放。放开给散文，「把它排除掉——没有它」这种就会被误判成标签。
const LABEL_MARKED = /^(?:\*\*[^*\n]{1,24}\*\*|`[^`\n]{1,24}`)\s*——/
const LABEL_BARE = /^[^\s——。，？！,]{1,12}\s*——/

function classifyChunk(text, acc, allowBare = false) {
  const t = text.trim()
  const hits = (t.match(/——/g) || []).length
  if (!hits) return
  if (t === '——') return // 表格空单元格占位符
  if (LABEL_MARKED.test(t) || (allowBare && LABEL_BARE.test(t))) {
    acc.label += 1
    acc.prose += hits - 1 // 标签位之后若还有破折号，那些是散文
  } else {
    acc.prose += hits
  }
}

function countEmdash(src) {
  const acc = { prose: 0, label: 0 }
  for (const line of stripFences(src).split('\n')) {
    if (!line.includes('——')) continue
    if (/^\s*\|/.test(line)) {
      // 表格行：拆成单元格逐格判，裸术语在这里算标签
      for (const cell of line.split('|').slice(1, -1)) classifyChunk(cell, acc, true)
    } else {
      // 列表项与普通段落：必须带标记才算标签
      classifyChunk(line.replace(/^\s*[-*]\s+/, ''), acc, false)
    }
  }
  return acc
}

const RULES = [
  {
    id: 'halfwidth-comma',
    zhOnly: true,
    re: new RegExp(`[${CJK}],|,[${CJK}]`, 'g'),
    msg: '半角逗号紧贴中文，应为全角 ，',
  },
  {
    id: 'halfwidth-colon',
    zhOnly: true,
    re: new RegExp(`[${CJK}]:|:[${CJK}]`, 'g'),
    msg: '半角冒号紧贴中文，应为全角 ：',
  },
  {
    id: 'halfwidth-paren',
    zhOnly: true,
    // 判据是【括号所处的语境是不是中文】，不是【括号里装的是不是中文】。
    // 「超管(`sadm`)放行」「# 前端规范(Vue 3 + Naive UI)」都是中文句子里的括号，
    // 里面装代码也该全角；只按内容判会把这 134 处放过，留下一句里两套宽度的混血观感。
    // 纯西文语境的 (v1.2)、以及真·空括号（裸写的 foo()）放过。
    re: new RegExp(
      `\\([^)\\n]*[${CJK}][^)\\n]*\\)` // 括号内含中文
      + `|[${CJK}][^\\S\\n]{0,2}\\([^)\\n]+\\)` // 括号前是中文
      + `|\\([^)\\n]+\\)[^\\S\\n]{0,2}[${CJK}]`, // 括号后是中文
      'g'
    ),
    msg: '中文语境里的半角括号，应为全角 （）',
  },
  {
    id: 'halfwidth-sentence-end',
    zhOnly: true,
    // 只判「汉字后面紧跟」这一个方向。逗号冒号要判两侧，是因为它们也做分隔符；
    // 问号叹号只收句，句子收在汉字上就该用全角。剥掉代码与链接目标后，`站点?q=1` 这类不会来。
    // 不含分号：半角 ; 全站 137 处、27 页，而规范「分号焊接」那条要求的是【拆句】而不是换标点，
    // 一条把它们就地全角化的硬拦，会把本该拆开的句子焊得更死。那批单独决定。
    re: /[一-鿿][?!]/g,
    msg: '半角问号/叹号收在中文句尾，应为全角 ？！',
  },
  {
    id: 'mixed-paren',
    zhOnly: true,
    // 全角开、半角闭（或反之）。全站 5 处，4 处在 permission.md——批量改动只改了一半的残骸。
    re: /（[^）\n]*\)|\([^)\n]*）/g,
    msg: '括号全角半角混配',
  },
  {
    id: 'spaced-emdash',
    zhOnly: true,
    // 只剥围栏，保留行内代码。剥了会误报：`就两个文件——\`locales/zh-CN.ts\`` 原文没空格，
    // 行内代码被抹成等长空白后，规则看到的却是「—— 」。
    keepInlineCode: true,
    // 保留下来的破折号统一写成无空格全角。混用是分批生成、没有统一底本的直接证据。
    re: /[ \t]——|——[ \t]/g,
    msg: '破折号两侧不留空格',
  },
]

// 自检：node scripts/lint-prose.mjs --selftest
// 这里的分类逻辑（三类破折号、首句认定）踩过坑，改动前先让它跑绿。
if (process.argv[2] === '--selftest') {
  const { strictEqual: eq, deepStrictEqual: deq } = await import('node:assert')

  // 破折号三分类
  deq(countEmdash('| `vite`——dev server |'), { prose: 0, label: 1 }, '表格单元格标签位')
  deq(countEmdash('| —— |'), { prose: 0, label: 0 }, '表格空单元格占位符不计数')
  deq(countEmdash('- **原语**——灰阶、品牌主色。'), { prose: 0, label: 1 }, '列表标签位')
  deq(countEmdash('把它排除掉——没有它，一次 COPY 就能把密钥烤进镜像。'), { prose: 1, label: 0 }, '散文位')
  deq(countEmdash('| 安全项 | 开发机的 data 里躺着真实密钥，把它排除掉——没有它一次 COPY 就烤进镜像 |'), { prose: 1, label: 0 }, '表格里的长句仍是散文')
  deq(countEmdash('| 自定义指令——auth.ts 定义 v-auth |'), { prose: 0, label: 1 }, '表格允许裸术语标签')
  deq(countEmdash('- 把它排除掉——没有它就会出事'), { prose: 1, label: 0 }, '列表里的裸短句是散文，不是标签')
  deq(countEmdash('```\n代码——里的不算\n```'), { prose: 0, label: 0 }, '围栏代码块剥掉')
  deq(countEmdash('- `BaseEntity` —— 主键 `Id`'), { prose: 0, label: 1 }, '行内代码当术语，不能被剥成空白')

  // 首句认定：引用块与列表不豁免
  eq(firstProseLine('# 标题\n\n> 这页讲 A、B、C。').line, '这页讲 A、B、C。', '引用块里的首句要认出来')
  eq(firstProseLine('# 标题\n\n- 这页讲 A。').line, '这页讲 A。', '列表里的首句要认出来')
  eq(firstProseLine('# 标题\n\n::: tip\n提示\n:::\n\n正文第一句。').line, '提示', '容器标记跳过，内容仍是正文')

  // 半角判据：紧贴中文才算，代码里的半角放过
  // 必须重置 lastIndex：这些正则带 /g，test() 会推进游标，
  // 上一个断言匹配成功后，下一个断言就从中间开始扫，白白判假。
  // 按 id 取规则，不按下标：插一条新规则就会让所有下标位移，
  // 断言却照样跑绿——那正是规范说的「跑一遍绿灯、带着违规发车」。
  const R = (id) => RULES.find((r) => r.id === id).re
  const has = (re, s) => { re.lastIndex = 0; return re.test(stripCode(s)) }
  eq(has(R('halfwidth-comma'), '内核,不是应用'), true, '半角逗号紧贴中文 → 拦')
  eq(has(R('halfwidth-comma'), '`foo, bar` 是合法的'), false, '行内代码里的半角逗号 → 放过')
  eq(has(R('halfwidth-colon'), '::: tip 提示'), false, 'VitePress 容器标记 → 放过')
  eq(has(R('halfwidth-paren'), '内核(元包)'), true, '半角括号裹中文 → 拦')
  eq(has(R('halfwidth-paren'), '超管(`sadm`)放行'), true, '中文语境、括号裹代码 → 拦')
  eq(has(R('halfwidth-paren'), '前端规范(Vue 3 + Naive UI)'), true, '中文语境、括号裹西文 → 拦')
  eq(has(R('halfwidth-paren'), 'see release notes (v1.2) for details'), false, '纯西文语境 → 放过')
  eq(has(R('halfwidth-paren'), '调用 AddTenonAdmin() 之后'), false, '真·空括号（裸函数调用）→ 放过')
  eq(has(R('mixed-paren'), '判定在服务端（后端的过滤器才是权威)'), true, '括号混配 → 拦')
  eq(has(R('mixed-paren'), '判定在服务端（后端的过滤器才是权威）'), false, '括号成对全角 → 放过')
  eq(has(R('halfwidth-sentence-end'), '不同用户看到的行为什么不同?'), true, '半角问号收在中文句尾 → 拦')
  eq(has(R('halfwidth-sentence-end'), '不同用户看到的行为什么不同？'), false, '全角问号 → 放过')
  eq(has(R('halfwidth-sentence-end'), '构建期给出 `VITE_API_BASE=x?y` 即可'), false, '行内代码里的问号 → 放过')
  eq(has(R('halfwidth-sentence-end'), '详见 [说明](https://x.dev/a?b=1) 一节'), false, '链接目标里的问号 → 放过')
  eq(has(R('halfwidth-sentence-end'), 'Which route? See below.'), false, '西文句尾 → 放过')

  // 强调标记不得成为半角标点的藏身处（shiftEmphasis）
  eq(has(R('halfwidth-paren'), '**验证参数**(`TenonAdminSetup.cs`)'), true, '粗体中文 + 半角括号 → 拦')
  eq(has(R('halfwidth-comma'), '**错误是数字，从不下发文案**,后端只给码'), true, '粗体中文 + 半角逗号 → 拦')
  eq(has(R('halfwidth-colon'), '**两端都要配**:'), true, '粗体中文 + 半角冒号 → 拦')
  eq(has(R('halfwidth-paren'), '**验证参数**（`TenonAdminSetup.cs`）'), false, '粗体中文 + 全角括号 → 放过')
  eq(has(R('halfwidth-paren'), '`lucide`(Lucide) 是图标集'), true, '括号两端都是代码/西文，但后面跟着中文 → 拦')
  eq(has(R('halfwidth-paren'), '`lucide`(Lucide)、`ep`(Element Plus)。'), false,
    '同上但邻居全是全角标点 → 放过。判据只认汉字，不认全角标点：'
    + '把全角标点也算中文语境，会连带拦下三行纯代码的表格单元格，'
    + '4 真 5 误的准确率不配当硬拦。那几处手工修，见 appearance.md:45。')
  eq(has(R('halfwidth-paren'), 'see the **release notes** (v1.2)'), false, '纯西文语境的粗体 → 放过')
  eq(stripCode('**验证参数**(x)').length, '**验证参数**(x)'.length, '挪动标记不改变长度，行列号不漂')
  const hasKeep = (re, s) => { re.lastIndex = 0; return re.test(stripFences(s)) }
  eq(hasKeep(R('spaced-emdash'), '一个进程 —— 内部系统首选'), true, '破折号带空格 → 拦')
  eq(hasKeep(R('spaced-emdash'), '一个进程——内部系统首选'), false, '破折号无空格 → 放过')
  eq(hasKeep(R('spaced-emdash'), '就两个文件——`locales/zh-CN.ts` 与它'), false, '破折号紧跟行内代码 → 放过（剥了行内代码会误报成「—— 」）')

  // 自称词全列
  for (const w of ['这页', '本页', '这一页', '这篇', '本篇', '本文', '本节', '本指南']) {
    eq(/^(这页|本页|这一页|这篇|本篇|本文|本节|本指南)/.test(`${w}讲 A、B、C。`), true, `自称词 ${w} → 拦`)
  }
  eq(/^(这页|本页|这一页|这篇|本篇|本文|本节|本指南)/.test('这套机制分两层。'), false, '「这套」不是自称词 → 放过')

  console.log('自检通过。')
  process.exit(0)
}

// 不传参 = 全站扫（CI 用）；传参 = 只扫这几页。
// 存量归零之前，全站扫必红，改稿人得能只跑自己那一页，否则第一天就学会无视这个闸门。
const argv = process.argv.slice(2)
const files = argv.length
  ? argv.map((p) => p.replace(/\\/g, '/'))
  : globSync('**/*.md', { cwd: process.cwd(), exclude: (p) => p.includes('node_modules') })
const errors = []
const warnings = []

for (const file of files.sort()) {
  const raw = readFileSync(file, 'utf8')
  const isZh = file.replace(/\\/g, '/').startsWith('zh/')
  const clean = stripCode(raw)

  const fenced = stripFences(raw)

  for (const rule of RULES) {
    if (rule.zhOnly && !isZh) continue
    const target = rule.keepInlineCode ? fenced : clean
    for (const line of target.split('\n').entries()) {
      const [i, text] = line
      rule.re.lastIndex = 0
      let m
      while ((m = rule.re.exec(text)) !== null) {
        errors.push(`${file}:${i + 1}  [${rule.id}] ${rule.msg}  →  ${JSON.stringify(m[0])}`)
      }
    }
  }

  // 开头不许朗读目录
  const first = firstProseLine(clean)
  if (first) {
    // 自称词全列封死。「本篇/本文」现在全站 0 次，正是 c7885b0 迁走的上一代用法——
    // 不封死就是一条免费返回原句的通道：换个自称词，槽位原样留下。
    if (isZh && /^(这页|本页|这一页|这篇|本篇|本文|本节|本指南)/.test(first.line)) {
      errors.push(`${file}:${first.lineNo}  [toc-readout] 正文第一句不得以「这页/本页」开头，见 skills/write-docs.md  →  ${JSON.stringify(first.line.slice(0, 30))}`)
    }
    if (!isZh && /^This (page|guide|section|document)\b/.test(first.line)) {
      errors.push(`${file}:${first.lineNo}  [toc-readout] 正文第一句不得以 "This page" 开头，见 skills/write-docs.md  →  ${JSON.stringify(first.line.slice(0, 40))}`)
    }
  }

  // 手感项：破折号预算。只算散文位；标签位干的是冒号的活，不占额度。
  // 中文侧才算——英文的 em-dash 是母语节奏，不设限。
  if (isZh) {
    const { prose, label } = countEmdash(raw)
    if (prose > EMDASH_BUDGET) {
      const tail = label ? `，另有标签位 ${label} 个（不占额度，机械换 ：）` : ''
      warnings.push(`${file}  散文位破折号 ${prose} 个（预算 ${EMDASH_BUDGET}）${tail}`)
    }
  }
}

if (warnings.length) {
  console.log(`\n手感项（不拦，供参考）——破折号超预算 ${warnings.length} 页：`)
  for (const w of warnings) console.log(`  ${w}`)
}

if (errors.length) {
  console.error(`\n铁律违规 ${errors.length} 处：`)
  for (const e of errors) console.error(`  ${e}`)
  console.error(`\n规矩见 skills/write-docs.md。`)
  process.exit(1)
}

console.log(`\n铁律通过：扫了 ${files.length} 个 md 文件，无违规。`)
