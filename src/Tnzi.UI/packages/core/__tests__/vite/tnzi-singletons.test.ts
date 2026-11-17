// @vitest-environment node
/**
 * `tnziSingletons()` dedupes `vue` always and `vue-router` / `pinia` /
 * `naive-ui` only where the app owns them. Listing a name the app's root
 * cannot resolve makes Vite fail to resolve it; leaving out one it owns lets a
 * linked install load a second copy (an app on `@tnzi/ui-ai` whose
 * `TAuthRoute` saw the framework's router instead of the app's).
 */
import { afterEach, describe, expect, it } from 'vitest'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { appOwns, tnziSingletons } from '../../vite.mjs'

const made: string[] = []

/** An app directory whose node_modules (in it or a parent) holds `owned`. */
function app(owned: string[], { hoisted = false } = {}): string {
  const top = mkdtempSync(join(tmpdir(), 'tnzi-singletons-'))
  made.push(top)
  const root = hoisted ? join(top, 'apps', 'web') : top
  mkdirSync(root, { recursive: true })
  for (const name of owned) {
    const dir = join(top, 'node_modules', name)
    mkdirSync(dir, { recursive: true })
    writeFileSync(join(dir, 'package.json'), JSON.stringify({ name }))
  }
  return root
}

function dedupeFor(root: string, extra?: string[]): string[] {
  const plugin = tnziSingletons(extra ? { extra } : {}) as { config: (c: { root?: string }) => { resolve: { dedupe: string[] } } }
  return plugin.config({ root }).resolve.dedupe
}

afterEach(() => {
  for (const dir of made.splice(0)) rmSync(dir, { recursive: true, force: true })
})

describe('tnziSingletons', () => {
  it('an app on @tnzi/ui-ai that routes: vue and vue-router', () => {
    expect(dedupeFor(app(['vue', 'vue-router']))).toEqual(['vue', 'vue-router'])
  })

  it('an admin app: vue, vue-router, pinia and naive-ui with no options', () => {
    expect(dedupeFor(app(['vue', 'vue-router', 'pinia', 'naive-ui']))).toEqual(['vue', 'vue-router', 'pinia', 'naive-ui'])
  })

  it('an app without a router: vue alone', () => {
    expect(dedupeFor(app(['vue']))).toEqual(['vue'])
  })

  it('finds a copy hoisted above the app root, as Vite does', () => {
    expect(dedupeFor(app(['vue', 'vue-router'], { hoisted: true }))).toEqual(['vue', 'vue-router'])
  })

  it('appends extra unconditionally', () => {
    expect(dedupeFor(app(['vue']), ['@iconify/vue'])).toEqual(['vue', '@iconify/vue'])
  })

  it('appOwns looks for node_modules/<name>/package.json from the root up', () => {
    const root = app(['vue-router'], { hoisted: true })
    expect(appOwns(root, 'vue-router')).toBe(true)
    expect(appOwns(root, 'pinia')).toBe(false)
  })

  // `@tnzi/ui/vite` and `@tnzi/ui-ai/vite` hand out this same plugin, so an app
  // imports it from the package it already depends on.
  it('@tnzi/ui/vite and @tnzi/ui-ai/vite re-export it', async () => {
    const packages = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..')
    for (const pkg of ['ui', 'ui-ai']) {
      const mod = await import(pathToFileURL(join(packages, pkg, 'vite.mjs')).href)
      expect(mod.tnziSingletons).toBe(tnziSingletons)
      const manifest = JSON.parse(readFileSync(join(packages, pkg, 'package.json'), 'utf8'))
      expect(manifest.exports['./vite']).toEqual({ types: './vite.d.ts', import: './vite.mjs' })
      expect(manifest.files).toEqual(expect.arrayContaining(['vite.mjs', 'vite.d.ts']))
    }
  })

  // A declaration that imports `vite` binds to the framework's own copy in a
  // linked install, and Vite's Plugin types do not cross majors.
  it('the declaration names no Vite type', () => {
    const dts = readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'vite.d.ts'), 'utf8')
    const code = dts.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '')
    expect(code).not.toMatch(/from ['"]vite['"]|import\(['"]vite['"]\)/)
  })
})
