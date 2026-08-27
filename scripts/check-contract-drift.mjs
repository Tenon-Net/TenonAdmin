import { spawn, spawnSync } from 'node:child_process'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const apiUrl = (process.env.TENON_CONTRACT_URL ?? `http://127.0.0.1:${process.env.TENON_CONTRACT_PORT ?? '5101'}`).replace(/\/+$/, '')
const generator = fileURLToPath(new URL('./gen-api.mjs', import.meta.url))
let host
let hostLog = ''

function appendHostLog(chunk) {
  hostLog = `${hostLog}${chunk}`.slice(-20000)
}

function stopHost() {
  if (host && host.exitCode === null) {
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/PID', String(host.pid), '/T', '/F'], { stdio: 'ignore' })
    } else {
      host.kill('SIGTERM')
    }
  }
}

function fail(message) {
  stopHost()
  console.error(`[contract] ${message}`)
  if (hostLog) console.error(hostLog)
  process.exit(1)
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function run(command, args, cwd, env) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd,
      env,
      stdio: 'inherit',
    })
    child.on('error', reject)
    child.on('exit', (code, signal) => resolve(code ?? (signal ? 1 : 0)))
  })
}

async function waitForReady() {
  for (let attempt = 0; attempt < 90; attempt += 1) {
    try {
      const response = await fetch(`${apiUrl}/health/ready`)
      if (response.ok) return
    } catch {
      // The host may still be compiling or binding its listener.
    }
    if (host.exitCode !== null) fail('MinimalHost exited before becoming ready')
    await sleep(2000)
  }
  fail('MinimalHost did not become ready')
}

async function main() {
  process.chdir(root)
  process.on('SIGINT', () => {
    stopHost()
    process.exit(130)
  })
  process.on('SIGTERM', () => {
    stopHost()
    process.exit(143)
  })

  console.log(`[contract] starting MinimalHost on ${apiUrl}`)
  host = spawn('dotnet', ['run', '--no-launch-profile', '-c', 'Release', '--project', 'backend/samples/MinimalHost'], {
    cwd: root,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: apiUrl,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  host.stdout.on('data', appendHostLog)
  host.stderr.on('data', appendHostLog)

  await waitForReady()

  const generationEnv = { ...process.env, TENON_API_TARGET: apiUrl }
  console.log('[contract] regenerating web/src/api/schema.d.ts')
  if ((await run(process.execPath, [generator], `${root}/web`, generationEnv)) !== 0) {
    fail('web schema generation failed')
  }

  console.log('[contract] regenerating web-react/src/api/schema.d.ts')
  if ((await run(process.execPath, [generator], `${root}/web-react`, generationEnv)) !== 0) {
    fail('web-react schema generation failed')
  }

  stopHost()
  const diff = spawnSync('git', ['diff', '--quiet', 'HEAD', '--', 'web/src/api/schema.d.ts', 'web-react/src/api/schema.d.ts'], {
    cwd: root,
    stdio: 'ignore',
  })
  if (diff.status !== 0) {
    console.error('[contract] schema.d.ts is stale')
    spawnSync('git', ['diff', '--', 'web/src/api/schema.d.ts', 'web-react/src/api/schema.d.ts'], {
      cwd: root,
      stdio: 'inherit',
    })
    fail('stage and commit both generated files before pushing')
  }

  console.log('[contract] contract in sync')
}

main().catch((error) => fail(error instanceof Error ? error.message : String(error)))
