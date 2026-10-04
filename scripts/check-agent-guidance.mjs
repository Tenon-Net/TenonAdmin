#!/usr/bin/env node
// 仅做结构检查；元数据使用正则近似检查，YAML/TOML 语义和工作流决策仍需单独审查。
import { existsSync, readFileSync, readdirSync, realpathSync, statSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const read = (path) => readFileSync(join(root, path), 'utf8')
const errors = []
const check = (condition, message) => { if (!condition) errors.push(message) }

function parseArguments(args) {
  const roots = []
  for (let index = 0; index < args.length; index++) {
    if (args[index] !== '--skill-root') {
      errors.push(`unknown argument: ${args[index]}`)
      continue
    }
    const path = args[++index]
    if (!path || path.startsWith('--')) {
      errors.push('--skill-root requires a path')
      if (path) index--
    } else {
      roots.push(path)
    }
  }
  return roots
}

function markdownFiles(directory) {
  return readdirSync(join(root, directory), { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name)
    return entry.isDirectory() ? markdownFiles(path) : entry.name.endsWith('.md') ? [path] : []
  })
}

const skillRoots = ['.agents/skills', '.codex/skills', '.claude/skills']
const files = ['AGENTS.md', 'CLAUDE.md', 'docs/agents/repository-reference.md',
  ...markdownFiles('skills'), ...skillRoots.flatMap(markdownFiles)]
const codexNames = new Map()
const seenSkillFiles = new Set()
let skills = 0

function checkSkill(path, source, checkDuplicate = true) {
  skills++
  const metadata = source.match(/^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/)?.[1]
  check(metadata, `${path}: missing frontmatter`)
  for (const field of ['name', 'description']) {
    check(new RegExp(`^${field}:\\s*\\S`, 'm').test(metadata ?? ''), `${path}: missing ${field}`)
  }
  const name = metadata?.match(/^name:\s*["']?([a-z0-9-]+)["']?\s*$/m)?.[1]
  check(name, `${path}: invalid skill name`)
  if (name && checkDuplicate) {
    check(!codexNames.has(name), `${path}: duplicate Codex skill ${name} (${codexNames.get(name)})`)
    if (!codexNames.has(name)) codexNames.set(name, path)
  }
}

for (const path of files) {
  const source = read(path)
  if (path.endsWith('/SKILL.md')) {
    const realFile = realpathSync(join(root, path))
    if (!seenSkillFiles.has(realFile)) {
      seenSkillFiles.add(realFile)
      checkSkill(path, source, !path.startsWith('.claude/'))
    }
  }

  // Examples contain hypothetical files; validate actual prose links only.
  const prose = source.replace(/^(`{3,}|~{3,})[^\n]*\n[\s\S]*?^\1\s*$/gm, '')
  for (const match of prose.matchAll(/\[[^\]\n]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g)) {
    const target = match[1].replace(/^<|>$/g, '').split('#')[0]
    if (!target || /^(?:[a-z][a-z0-9+.-]*:|\/)/i.test(target) || /[{}<>]/.test(target)) continue
    check(existsSync(resolve(root, dirname(path), decodeURIComponent(target))), `${path}: broken link ${target}`)
  }
}

function checkExternalSkillRoot(argument) {
  const selectedRoot = resolve(argument)
  let rootStats
  try {
    rootStats = statSync(selectedRoot)
  } catch (error) {
    errors.push(`--skill-root ${argument}: ${error.code === 'ENOENT' ? 'path does not exist' : error.message}`)
    return
  }
  if (!rootStats.isDirectory()) {
    errors.push(`--skill-root ${argument}: path is not a directory`)
    return
  }

  const seenDirectories = new Set()
  function visit(directory) {
    const realDirectory = realpathSync(directory)
    if (seenDirectories.has(realDirectory)) return
    seenDirectories.add(realDirectory)

    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name)
      let stats
      try {
        stats = entry.isSymbolicLink() ? statSync(path) : entry
      } catch (error) {
        errors.push(`${path}: ${error.message}`)
        continue
      }
      if (stats.isDirectory()) {
        visit(path)
      } else if (stats.isFile() && entry.name === 'SKILL.md') {
        const realFile = realpathSync(path)
        if (seenSkillFiles.has(realFile)) continue
        seenSkillFiles.add(realFile)
        checkSkill(path, readFileSync(path, 'utf8'))
      }
    }
  }

  visit(selectedRoot)
}

for (const path of parseArguments(process.argv.slice(2))) checkExternalSkillRoot(path)

const agents = read('AGENTS.md')
for (const marker of ['CODEGRAPH', 'OMX:AGENTS', 'OMX:GUIDANCE:OPERATING',
  'OMX:GUIDANCE:SPECIALIST-ROUTING', 'OMX:MODELS', 'OMX:GUIDANCE:VERIFYSEQ',
  'OMX:RUNTIME', 'OMX:TEAM:WORKER']) {
  const delimiter = marker === 'CODEGRAPH' ? '_' : ':'
  const start = `<!-- ${marker}${delimiter}START -->`
  const end = `<!-- ${marker}${delimiter}END -->`
  check(agents.includes(start) && agents.indexOf(end) > agents.indexOf(start), `AGENTS.md: unpaired ${marker}`)
}

if (errors.length) {
  console.error(errors.join('\n'))
  process.exitCode = 1
} else {
  console.log(`Agent guidance: ${skills} skill entries, ${files.length} Markdown files, links and markers passed.`)
}
