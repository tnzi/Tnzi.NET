/**
 * What a package's source imports, against what its `package.json` declares.
 *
 * Shared by every package's `__tests__/conventions/manifest-matches-imports.test.ts`.
 * Two directions:
 *
 * - **Imported, so declared.** Every bare package a file under `src/` imports
 *   (`import type` and `import()` included: an emitted `.d.ts` keeps a type
 *   import, and a consumer's type check then needs the package) must be a
 *   `dependency` or a `peerDependency`. A `devDependency` is not enough: it is
 *   not installed for a consumer.
 * - **Declared, so imported.** Every `peerDependency` must be imported
 *   somewhere under `src/`. A peer nobody imports is an install requirement
 *   with nothing behind it: the manifest of `@tnzi/ui` once required naive-ui
 *   for applications that never rendered a naive component.
 *
 * And one rule for optional peers: an optional peer may not be reached by a
 * static, value-level import from the package root entry. If it is, every
 * application that imports the root needs it, and "optional" is a false
 * promise (it was, for `@tnzi/ui` in `@tnzi/ui-ai`). Reaching it through
 * `import()`, through a type-only import, or only from a subpath entry is fine.
 *
 * Node-only module (no Vite, no Vue): plain regex over the sources, the way
 * the other convention gates in this workspace read them.
 */
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'

const SOURCE = /\.(ts|tsx|vue|mts)$/

/** Every source file under `dir`. */
export function sourceFiles(dir, out = []) {
  if (!existsSync(dir)) return out
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) sourceFiles(full, out)
    else if (SOURCE.test(name)) out.push(full)
  }
  return out
}

/** Comments out, so prose that mentions an import is not one. */
function stripComments(source) {
  return source.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/(^|[^:'"`\\])\/\/[^\n]*/g, '$1')
}

/**
 * Every import specifier in a file, with whether it is type-only and whether
 * it is dynamic. `export ... from` counts as an import.
 */
export function importsOf(source) {
  const code = stripComments(source)
  const found = []
  const statement = /(?:^|[\n;])\s*(import|export)\s+(type\s+)?([^'";]*?)\s*from\s*['"]([^'"]+)['"]/g
  for (const m of code.matchAll(statement)) {
    const clause = m[3] ?? ''
    // `import { type A, type B }` is type-only too.
    const braces = clause.match(/^\{([^}]*)\}$/)
    const allTypeNames = braces
      ? braces[1].split(',').map((n) => n.trim()).filter(Boolean).every((n) => n.startsWith('type '))
      : false
    found.push({ specifier: m[4], typeOnly: Boolean(m[2]) || allTypeNames, dynamic: false })
  }
  for (const m of code.matchAll(/(?:^|[\n;])\s*import\s*['"]([^'"]+)['"]/g)) {
    found.push({ specifier: m[1], typeOnly: false, dynamic: false })
  }
  for (const m of code.matchAll(/\bimport\(\s*['"]([^'"]+)['"]\s*\)/g)) {
    found.push({ specifier: m[1], typeOnly: false, dynamic: true })
  }
  return found
}

/** `@scope/name/sub` -> `@scope/name`, `name/sub` -> `name`; null for non-packages. */
export function packageOf(specifier) {
  if (specifier.startsWith('.') || specifier.startsWith('/')) return null
  if (specifier.startsWith('node:') || specifier.startsWith('virtual:')) return null
  if (specifier.startsWith('@/')) return null
  const parts = specifier.split('/')
  return specifier.startsWith('@') ? parts.slice(0, 2).join('/') : parts[0]
}

function resolveRelative(fromFile, specifier) {
  const base = resolve(dirname(fromFile), specifier)
  for (const candidate of [base, `${base}.ts`, `${base}.vue`, join(base, 'index.ts')]) {
    if (existsSync(candidate) && statSync(candidate).isFile()) return candidate
  }
  return null
}

/**
 * Packages reached by a static, value-level import from `entry`, following
 * relative imports, with the path that reaches each.
 */
export function staticValueImportsFrom(entry) {
  const reached = new Map()
  const seen = new Set()
  const queue = [{ file: entry, trail: [entry] }]
  while (queue.length) {
    const { file, trail } = queue.shift()
    if (seen.has(file)) continue
    seen.add(file)
    if (!SOURCE.test(file)) continue
    for (const imp of importsOf(readFileSync(file, 'utf8'))) {
      if (imp.typeOnly || imp.dynamic) continue
      const pkg = packageOf(imp.specifier)
      if (pkg) {
        if (!reached.has(pkg)) reached.set(pkg, [...trail, imp.specifier])
      } else if (imp.specifier.startsWith('.')) {
        const next = resolveRelative(file, imp.specifier)
        if (next) queue.push({ file: next, trail: [...trail, next] })
      }
    }
  }
  return reached
}

/**
 * Compare a package's manifest with its sources. Returns lists of problems,
 * each a readable sentence; all empty means the two agree.
 *
 * @param {string} pkgDir           package directory (holds package.json and src/)
 * @param {{ rootEntry?: string, peersWithoutImports?: Record<string, string> }} [options]
 *   `rootEntry` is the file behind the `.` export (default `src/index.ts`);
 *   `peersWithoutImports` names peers that are required for a reason other
 *   than an import (each with that reason), such as `typescript` for a
 *   package whose types need a minimum compiler.
 */
export function compareManifestWithImports(pkgDir, options = {}) {
  const manifest = JSON.parse(readFileSync(join(pkgDir, 'package.json'), 'utf8'))
  const self = manifest.name
  const deps = manifest.dependencies ?? {}
  const peers = manifest.peerDependencies ?? {}
  const optional = new Set(
    Object.entries(manifest.peerDependenciesMeta ?? {}).filter(([, m]) => m?.optional).map(([n]) => n),
  )
  const exempt = options.peersWithoutImports ?? {}

  const importedBy = new Map()
  for (const file of sourceFiles(join(pkgDir, 'src'))) {
    for (const imp of importsOf(readFileSync(file, 'utf8'))) {
      const pkg = packageOf(imp.specifier)
      if (!pkg || pkg === self) continue
      if (!importedBy.has(pkg)) importedBy.set(pkg, relative(pkgDir, file).replace(/\\/g, '/'))
    }
  }

  const undeclared = [...importedBy]
    .filter(([pkg]) => !(pkg in deps) && !(pkg in peers))
    .map(([pkg, file]) => `${pkg} is imported (${file}) but is neither a dependency nor a peerDependency`)

  const unusedPeers = Object.keys(peers)
    .filter((pkg) => !importedBy.has(pkg) && !(pkg in exempt))
    .map((pkg) => `${pkg} is a peerDependency but nothing under src/ imports it`)

  const rootEntry = join(pkgDir, options.rootEntry ?? 'src/index.ts')
  const fromRoot = existsSync(rootEntry) ? staticValueImportsFrom(rootEntry) : new Map()
  const falselyOptional = [...optional]
    .filter((pkg) => fromRoot.has(pkg))
    .map((pkg) => {
      const trail = fromRoot.get(pkg).map((f) => (f.startsWith(pkgDir) ? relative(pkgDir, f).replace(/\\/g, '/') : f))
      return `${pkg} is an optional peer, but the root entry imports it statically: ${trail.join(' -> ')}`
    })

  return { undeclared, unusedPeers, falselyOptional, imported: [...importedBy.keys()].sort() }
}
