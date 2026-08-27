import { spawnSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import { resolve } from 'node:path'

const apiTarget = (process.env.TENON_API_TARGET ?? 'http://localhost:5100').replace(/\/+$/, '')
const input = `${apiTarget}/openapi/v1.json`
const cli = resolve(process.cwd(), 'node_modules/openapi-typescript/bin/cli.js')

if (!existsSync(cli)) {
  console.error('openapi-typescript is not installed; run npm ci in this frontend first')
  process.exit(1)
}

const result = spawnSync(
  process.execPath,
  [cli, input, '-o', 'src/api/schema.d.ts'],
  {
    cwd: process.cwd(),
    stdio: 'inherit',
  },
)

if (result.error) {
  console.error(`Failed to run openapi-typescript: ${result.error.message}`)
  process.exit(1)
}

process.exit(result.status ?? 1)
