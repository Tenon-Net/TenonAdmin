import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { spawnSync } from 'node:child_process'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const script = join(repository, 'scripts/check-agent-guidance.mjs')

function run(...args) {
  return spawnSync(process.execPath, [script, ...args], {
    cwd: repository,
    encoding: 'utf8',
  })
}

function createSkill(root, directory, name, description = '测试技能') {
  const skill = join(root, directory)
  mkdirSync(skill, { recursive: true })
  writeFileSync(join(skill, 'SKILL.md'), `---\nname: ${name}\ndescription: ${description}\n---\n`)
  return skill
}

test('重复选择同一真实技能不会误报，并支持符号链接入口', (context) => {
  const fixture = mkdtempSync(join(tmpdir(), 'tenon-agent-guidance-'))
  context.after(() => rmSync(fixture, { recursive: true, force: true }))

  const source = join(fixture, 'source')
  const skill = createSkill(source, 'alpha', 'external-alpha')
  writeFileSync(join(source, 'notes.md'), '[无效链接](missing.md)')
  const rootLink = join(fixture, 'root-link')
  symlinkSync(source, rootLink, 'dir')
  const entryRoot = join(fixture, 'entry-root')
  mkdirSync(entryRoot)
  symlinkSync(skill, join(entryRoot, 'linked-skill'), 'dir')

  const result = run('--skill-root', rootLink, '--skill-root', entryRoot)

  assert.equal(result.status, 0, result.stderr)
  assert.match(result.stdout, /Agent guidance: \d+ skill entries/)
})

test('跨根目录报告技能名冲突和元数据错误', (context) => {
  const fixture = mkdtempSync(join(tmpdir(), 'tenon-agent-guidance-'))
  context.after(() => rmSync(fixture, { recursive: true, force: true }))

  const first = join(fixture, 'first')
  const second = join(fixture, 'second')
  createSkill(first, 'one', 'external-collision')
  createSkill(second, 'two', 'external-collision')
  createSkill(second, 'repo-collision', 'analyze')
  const malformed = createSkill(second, 'malformed', 'external-malformed')
  writeFileSync(join(malformed, 'SKILL.md'), '---\nname: external-malformed\n---\n')

  const result = run('--skill-root', first, '--skill-root', second)

  assert.equal(result.status, 1)
  assert.match(result.stderr, /duplicate Codex skill external-collision/)
  assert.match(result.stderr, /duplicate Codex skill analyze/)
  assert.match(result.stderr, /missing description/)
})

test('无效参数和根目录返回可操作的错误', (context) => {
  const fixture = mkdtempSync(join(tmpdir(), 'tenon-agent-guidance-'))
  context.after(() => rmSync(fixture, { recursive: true, force: true }))
  const file = join(fixture, 'file')
  writeFileSync(file, '')

  const missingArgument = run('--skill-root')
  assert.equal(missingArgument.status, 1)
  assert.match(missingArgument.stderr, /--skill-root requires a path/)

  const missingRoot = run('--skill-root', join(fixture, 'missing'))
  assert.equal(missingRoot.status, 1)
  assert.match(missingRoot.stderr, /path does not exist/)

  const fileRoot = run('--skill-root', file, '--unknown')
  assert.equal(fileRoot.status, 1)
  assert.match(fileRoot.stderr, /path is not a directory/)
  assert.match(fileRoot.stderr, /unknown argument: --unknown/)
})
