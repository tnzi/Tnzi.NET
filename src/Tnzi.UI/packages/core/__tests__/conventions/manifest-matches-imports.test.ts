// @vitest-environment node
/**
 * Convention gate: `package.json` declares what `src/` imports, and only that.
 *
 * Imported packages (type-only and `import()` included) must be dependencies
 * or peers; every peer must be imported somewhere; an optional peer may not be
 * imported statically from the root entry. The rules and their reasons are in
 * `tools/conventions/manifest-imports.mjs`, shared by all five packages.
 */
import { describe, expect, it } from 'vitest'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { compareManifestWithImports } from '../../../../tools/conventions/manifest-imports.mjs'

const pkgDir = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const result = compareManifestWithImports(pkgDir, {
    // Its types need a minimum compiler; nothing imports the compiler itself.
    peersWithoutImports: { typescript: 'the published types need TypeScript 5 or later' },
  })

describe('manifest matches imports', () => {
  it('reads the sources (guards the scan itself)', () => {
    expect(result.imported).toContain('vue')
  })

  it('every imported package is a dependency or a peer', () => {
    expect(result.undeclared).toEqual([])
  })

  it('every peer is imported', () => {
    expect(result.unusedPeers).toEqual([])
  })

  it('no optional peer is a static import of the root entry', () => {
    expect(result.falselyOptional).toEqual([])
  })
})
