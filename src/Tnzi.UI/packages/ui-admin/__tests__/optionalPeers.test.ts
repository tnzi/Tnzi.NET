// @vitest-environment node
/**
 * Convention gate: **this package's optional peers are only ever loaded on demand.**
 *
 * `@tnzi/ui-ai` and `vue-draggable-plus` are declared `optional: true` because
 * only some features use them: the workflow editor and drag-to-reorder. That
 * promise holds only if nothing imports them statically. A consumer's bundler
 * resolves every static import of every module it includes, and the admin
 * shell includes the tab bar and the default routes, so one value import is
 * enough to make the peer required for every app: Vite then substitutes an
 * empty placeholder for the missing package and the build fails with
 * `MISSING_EXPORT`. That had happened in three places (the tab bar, the column
 * settings popover and the workflow editor), and no test here could see it,
 * because the workspace always has both packages installed.
 *
 * Allowed: `import type ...` (erased at build) and `import('...')` (resolved
 * when the feature runs, with a fallback in place). Anything else is a value
 * import and fails this test.
 *
 * Not applied to `@tnzi/ui` or `@tnzi/ui-ai`: their optional peers are needed
 * only by the entry points that import them (`useEcharts`, the chat
 * components, the routed auth page), so a static import there is the intended
 * contract, not a leak.
 */
import { describe, it, expect } from 'vitest'
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, relative, resolve } from 'node:path'

const packageDir = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const srcDir = resolve(packageDir, 'src')

interface Manifest {
  peerDependenciesMeta?: Record<string, { optional?: boolean }>
}

const manifest = JSON.parse(readFileSync(resolve(packageDir, 'package.json'), 'utf8')) as Manifest
const optionalPeers = Object.entries(manifest.peerDependenciesMeta ?? {})
  .filter(([, meta]) => meta.optional)
  .map(([name]) => name)

function walk(dir: string, out: string[] = []): string[] {
  if (!existsSync(dir)) return out
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name)
    if (entry.isDirectory()) walk(full, out)
    else if (/\.(ts|vue)$/.test(entry.name)) out.push(full)
  }
  return out
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * Static `import` / `export ... from` statements that reach `peer` (or one of
 * its subpaths) and are not `import type` / `export type`. Anchored at the
 * start of a line, so a JSDoc example (` * import ...`) is not a statement.
 */
function valueImportsOf(source: string, peer: string): string[] {
  const specifier = `['"]${escapeRegExp(peer)}(?:/[^'"]*)?['"]`
  const statement = new RegExp(
    `^[ \\t]*(?:import|export)\\s+(?!type\\s)(?:[^;'"]*?\\sfrom\\s+)?${specifier}`,
    'gm',
  )
  return [...source.matchAll(statement)].map((m) => m[0].trim())
}

describe('optional peers are loaded on demand', () => {
  // Guards the guard: an empty peer list or an empty file list would make the
  // scan below pass over nothing.
  it('sees the optional peers and the source tree', () => {
    expect(optionalPeers).toEqual(expect.arrayContaining(['@tnzi/ui-ai', 'vue-draggable-plus']))
    expect(walk(srcDir).length).toBeGreaterThan(100)
  })

  it('recognises the forms it is meant to catch, and only those', () => {
    const peer = 'vue-draggable-plus'
    expect(valueImportsOf(`import { VueDraggable } from 'vue-draggable-plus'`, peer)).toHaveLength(1)
    expect(valueImportsOf(`import {\n  Handle,\n  type NodeProps,\n} from "vue-draggable-plus/sub"`, peer)).toHaveLength(1)
    expect(valueImportsOf(`import 'vue-draggable-plus'`, peer)).toHaveLength(1)
    expect(valueImportsOf(`export { VueDraggable } from 'vue-draggable-plus'`, peer)).toHaveLength(1)

    expect(valueImportsOf(`import type { VueDraggable } from 'vue-draggable-plus'`, peer)).toHaveLength(0)
    expect(valueImportsOf(`const m = await import('vue-draggable-plus')`, peer)).toHaveLength(0)
    expect(valueImportsOf(` * import { VueDraggable } from 'vue-draggable-plus'`, peer)).toHaveLength(0)
    expect(valueImportsOf(`import { x } from 'vue-draggable-plus-extra'`, peer)).toHaveLength(0)
  })

  it.each(optionalPeers)('%s is never imported statically', (peer) => {
    const offenders = walk(srcDir).flatMap((file) =>
      valueImportsOf(readFileSync(file, 'utf8'), peer).map(
        (statement) => `${relative(packageDir, file).split(/[\\/]/).join('/')}: ${statement}`,
      ),
    )

    expect(
      offenders,
      `${peer} is an optional peer: load it with import() and keep a fallback, or use import type`,
    ).toEqual([])
  })
})
