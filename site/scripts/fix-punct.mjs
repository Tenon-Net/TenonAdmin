#!/usr/bin/env node
// lint-prose.mjs 的搭档：它说哪里错，这个把机械能改的改掉。
// 规矩的真相源仍是 skills/write-docs.md，这里只实现【零判断】的那部分。
//
//   cd site
//   node scripts/fix-punct.mjs zh/backend/auth-security.md   # 只改这几页
//   node scripts/fix-punct.mjs                               # 全站（只碰 zh/）
//   node scripts/fix-punct.mjs --dry                         # 只报不改
//
// 手工修完 220 处之后才写的这个脚本。写早一点能省事，但也正因为手工过了一遍，
// 才知道哪些能机械改、哪些不能：顿号 vs 逗号、嵌套引号该不该用 『』，仍然留给人判。
//
// 安全闸：每个文件改完后把成对标点各自归一，必须与原文【逐字相同】。
// 任何一条不满足就整文件跳过、不写盘。这条闸门在开发中拦下过一次下标错位。

import { readFileSync, writeFileSync, globSync } from 'node:fs'

const CJK = '一-鿿'
const HAS_CJK = new RegExp(`[${CJK}]`)
const blank = (m) => m.replace(/[^\n]/g, ' ')

// 受保护片段一律换成【等长】空白：掩码下标 == 原文下标，于是可以按下标改原文。
function mask(src) {
  return src
    .replace(/^```[\s\S]*?^```/gm, blank)
    .replace(/`[^`\n]*`/g, blank)
    .replace(/\]\([^)\n]*\)/g, blank)
    .replace(/<!--[\s\S]*?-->/g, blank)
}

const PAIRS = { ',': '，', ';': '；', ':': '：', '?': '？', '!': '！' }

// 一趟不够：第 ④ 步把 `(` 换成 `（` 之后，会给第 ② 步造出新的「全角标点相邻」语境，
// 而 ② 已经跑过了。所以整体跑到不动点为止。闸门抓到过这个：括号转完剩下一批 `）:`。
function convert(src) {
  let cur = src
  let n = 0
  for (let pass = 0; pass < 5; pass += 1) {
    const [next, add] = onePass(cur)
    cur = next
    n += add
    if (!add) break
  }
  const norm = normalise
  if (norm(cur) !== norm(src)) throw new Error('安全闸：出现了标点之外的差异')
  return [cur, n]
}

function onePass(src) {
  let cur = src
  let n = 0

  const remap = (re, map, lineScoped) => {
    const m = mask(cur)
    if (m.length !== cur.length) throw new Error('掩码长度不等')
    const out = cur.split('')
    const hit = (at) => { if (map[out[at]]) { out[at] = map[out[at]]; n += 1 } }
    if (lineScoped) {
      let base = 0
      for (const line of m.split('\n')) {
        if (HAS_CJK.test(line)) {
          re.lastIndex = 0
          let x
          while ((x = re.exec(line)) !== null) hit(base + x.index + x[0].length - 1)
        }
        base += line.length + 1
      }
    } else {
      re.lastIndex = 0
      let x
      while ((x = re.exec(m)) !== null) {
        for (let i = x.index; i < x.index + x[0].length; i += 1) if (map[m[i]]) hit(i)
      }
    }
    cur = out.join('')
  }

  // ① 紧贴汉字的 , ; : ? !
  remap(new RegExp(`[${CJK}][,;:?!]|[,;:][${CJK}]`, 'g'), PAIRS, false)

  // ② 两侧是代码或全角标点、整行却是中文（同 lint-prose 的 zhLine 判据）
  remap(
    /(?:^|[，。：；（）？！、「」*_\s])[,;:](?=$|[，。：；（）？！、「」*_\s])/g,
    { ',': '，', ';': '；', ':': '：' },
    true
  )

  // ③ 半角双引号裹中文 → 「」。成对换，不做单边。
  {
    const m = mask(cur)
    const out = cur.split('')
    const re = new RegExp(`"[^"\\n]*[${CJK}][^"\\n]*"`, 'g')
    let x
    while ((x = re.exec(m)) !== null) {
      const l = x.index
      const r = x.index + x[0].length - 1
      if (out[l] === '"' && out[r] === '"') { out[l] = '「'; out[r] = '」'; n += 2 }
    }
    cur = out.join('')
  }

  // ④ 括号必须成对换。单边替换会留下孤儿——permission.md 那 4 处就是这么来的。
  {
    const m = mask(cur)
    const out = cur.split('')
    const re = new RegExp(
      `\\([^)\\n]*[${CJK}][^)\\n]*\\)`
      + `|[${CJK}][^\\S\\n]{0,2}\\([^)\\n]+\\)`
      + `|\\([^)\\n]+\\)[^\\S\\n]{0,2}[${CJK}]`,
      'g'
    )
    let x
    while ((x = re.exec(m)) !== null) {
      const l = x.index + x[0].indexOf('(')
      const r = x.index + x[0].lastIndexOf(')')
      if (out[l] === '(' && out[r] === ')') { out[l] = '（'; out[r] = '）'; n += 2 }
    }
    cur = out.join('')
  }

  // ⑤ 破折号两侧不留空格。这条会改变长度，所以放最后，且只在围栏之外动。
  {
    const fenced = cur.replace(/^```[\s\S]*?^```/gm, blank)
    const kill = new Set()
    for (const x of fenced.matchAll(/[ \t]+——|——[ \t]+/g)) {
      for (let i = x.index; i < x.index + x[0].length; i += 1) {
        if (/[ \t]/.test(cur[i])) kill.add(i)
      }
    }
    if (kill.size) {
      cur = cur.split('').filter((_, i) => !kill.has(i)).join('')
      n += kill.size
    }
  }

  return [cur, n]
}

// 成对标点各自归一：改前改后必须逐字相同，否则说明动了标点之外的东西。
function normalise(t) {
  return t
    .replace(/[,，]/g, '#').replace(/[;；]/g, '#').replace(/[:：]/g, '#')
    .replace(/[?？]/g, '#').replace(/[!！]/g, '#').replace(/["「」]/g, '#')
    .replace(/[(（]/g, '#').replace(/[)）]/g, '#')
    .replace(/[ \t]*——[ \t]*/g, '——')
}

const argv = process.argv.slice(2)
const dry = argv.includes('--dry')
const named = argv.filter((a) => !a.startsWith('--')).map((p) => p.replace(/\\/g, '/'))
const files = (named.length ? named : globSync('zh/**/*.md', { cwd: process.cwd() }).map((p) => p.replace(/\\/g, '/')))
  .filter((f) => f.startsWith('zh/'))   // 只碰中文侧：全角化不适用于英文

let touched = 0
let total = 0
for (const f of files.sort()) {
  const src = readFileSync(f, 'utf8')
  let out
  let cnt
  try { [out, cnt] = convert(src) } catch (e) { console.log(`✗ ${f}  ${e.message}`); continue }
  if (!cnt) continue
  if (!dry) writeFileSync(f, out)
  touched += 1
  total += cnt
  console.log(`${dry ? '将改' : '已改'} ${f}  ${cnt} 处`)
}
console.log(`\n${dry ? '待改' : '改毕'}：${total} 处，${touched} 个文件`)
