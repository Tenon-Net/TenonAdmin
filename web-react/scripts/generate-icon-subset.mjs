import { readdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, extname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const sourceRoot = join(root, 'src')
const outputPath = join(sourceRoot, 'assets', 'icons.generated.json')
const prefixes = ['ph', 'lucide', 'ep', 'ant-design']
const iconPattern = new RegExp(`\\b(${prefixes.join('|')}):([a-z0-9][a-z0-9-]*)\\b`, 'g')

async function sourceFiles(dir) {
  const out = []
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) out.push(...await sourceFiles(path))
    else if (['.ts', '.tsx'].includes(extname(entry.name)) && !entry.name.includes('.spec.')) out.push(path)
  }
  return out
}

const names = new Set(JSON.parse(await readFile(join(root, 'scripts', 'icon-manifest.json'), 'utf8')))
for (const file of await sourceFiles(sourceRoot)) {
  const text = await readFile(file, 'utf8')
  for (const match of text.matchAll(iconPattern)) names.add(`${match[1]}:${match[2]}`)
}

const byPrefix = new Map(prefixes.map((prefix) => [prefix, []]))
for (const fullName of names) {
  const [prefix, name] = fullName.split(':')
  if (byPrefix.has(prefix) && name) byPrefix.get(prefix).push(name)
}

const collections = []
for (const prefix of prefixes) {
  const source = JSON.parse(await readFile(join(root, 'node_modules', '@iconify-json', prefix, 'icons.json'), 'utf8'))
  const icons = {}
  const aliases = {}

  function include(name) {
    if (source.icons?.[name]) {
      icons[name] = source.icons[name]
      return
    }
    const alias = source.aliases?.[name]
    if (!alias) throw new Error(`Icon ${prefix}:${name} is not present in @iconify-json/${prefix}`)
    aliases[name] = alias
    include(alias.parent)
  }

  for (const name of [...new Set(byPrefix.get(prefix))].sort()) include(name)
  if (Object.keys(icons).length === 0 && Object.keys(aliases).length === 0) continue
  collections.push({
    prefix,
    ...(source.width ? { width: source.width } : {}),
    ...(source.height ? { height: source.height } : {}),
    icons,
    ...(Object.keys(aliases).length ? { aliases } : {}),
  })
}

await writeFile(outputPath, `${JSON.stringify(collections, null, 2)}\n`, 'utf8')
console.log(`Generated ${relative(root, outputPath)} with ${names.size} referenced icons.`)
