// @vitest-environment node
/**
 * Convention gate: `@tnzi/mobile` renders with Vant and never reaches for
 * naive-ui. A mobile app installs one component library; an import of the
 * other (`import type` included, since the emitted `.d.ts` keeps it) would make
 * it install two. Nor does this package style naive's classes or variables.
 */
import { describe, expect, it } from 'vitest'
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const pkgDir = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

function files(dir: string, out: string[] = []): string[] {
  if (!existsSync(dir)) return out
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) files(full, out)
    else if (/\.(ts|tsx|vue|mts|css)$/.test(name)) out.push(full)
  }
  return out
}

describe('@tnzi/mobile uses no naive-ui', () => {
  const sources = files(join(pkgDir, 'src'))

  it('reads the sources (guards the scan itself)', () => {
    expect(sources.length).toBeGreaterThan(10)
  })

  it('no file imports naive-ui, uses a naive tag or styles a naive class', () => {
    const found: string[] = []
    for (const file of sources) {
      const text = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/<!--[\s\S]*?-->/g, ' ')
      const at = relative(pkgDir, file).replace(/\\/g, '/')
      if (/['"]naive-ui(?:\/[^'"]*)?['"]/.test(text)) found.push(`${at}: imports naive-ui`)
      if (/<(?:n-[a-z-]+|N[A-Z][A-Za-z]+)[\s/>]/.test(text.match(/<template[\s\S]*<\/template>/)?.[0] ?? '')) {
        found.push(`${at}: naive tag in the template`)
      }
      if (/(?:^|[^\w-])\.n-[a-z]|--n-[a-z]/.test(file.endsWith('.ts') ? '' : text)) found.push(`${at}: naive style rule`)
    }
    expect(found).toEqual([])
  })

  it('package.json declares no naive-ui', () => {
    const manifest = JSON.parse(readFileSync(join(pkgDir, 'package.json'), 'utf8'))
    const fields = ['dependencies', 'peerDependencies', 'devDependencies'].filter((f) => manifest[f]?.['naive-ui'])
    expect(fields).toEqual([])
  })
})
