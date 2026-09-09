#!/usr/bin/env node
// Structural checks only; YAML/TOML parsing and workflow decisions need separate review.
import assert from 'node:assert/strict'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const read = (path) => readFileSync(join(root, path), 'utf8')
const errors = []
const check = (condition, message) => { if (!condition) errors.push(message) }

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
let skills = 0

for (const path of files) {
  const source = read(path)
  if (path.endsWith('/SKILL.md')) {
    skills++
    const metadata = source.match(/^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/)?.[1]
    check(metadata, `${path}: missing frontmatter`)
    for (const field of ['name', 'description']) {
      check(new RegExp(`^${field}:\\s*\\S`, 'm').test(metadata ?? ''), `${path}: missing ${field}`)
    }
    const name = metadata?.match(/^name:\s*["']?([a-z0-9-]+)["']?\s*$/m)?.[1]
    check(name, `${path}: invalid skill name`)
    if (name && !path.startsWith('.claude/')) {
      check(!codexNames.has(name), `${path}: duplicate Codex skill ${name} (${codexNames.get(name)})`)
      codexNames.set(name, path)
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

const agents = read('AGENTS.md')
for (const marker of ['CODEGRAPH', 'OMX:AGENTS', 'OMX:GUIDANCE:OPERATING',
  'OMX:GUIDANCE:SPECIALIST-ROUTING', 'OMX:MODELS', 'OMX:GUIDANCE:VERIFYSEQ',
  'OMX:RUNTIME', 'OMX:TEAM:WORKER']) {
  const delimiter = marker === 'CODEGRAPH' ? '_' : ':'
  const start = `<!-- ${marker}${delimiter}START -->`
  const end = `<!-- ${marker}${delimiter}END -->`
  check(agents.includes(start) && agents.indexOf(end) > agents.indexOf(start), `AGENTS.md: unpaired ${marker}`)
}

assert.equal(errors.length, 0, errors.join('\n'))
console.log(`Agent guidance: ${skills} skill entries, ${files.length} Markdown files, links and markers passed.`)
