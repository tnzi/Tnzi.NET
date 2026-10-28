/**
 * Start-up diagnostics for consumer route options: TOP-LEVEL routes
 * (`rootRoutes`), and the names given to `hideModules` / `showOnlyModules`
 * (see `warnUnknownModuleNames`).
 *
 * A top-level route that never matches is valid TypeScript, so build, typecheck
 * and lint all pass on it. End-to-end smokes don't catch it either: they drive
 * the app through its own router and never open the kind of link these pages
 * exist for (a token URL mailed to someone who is not a user). The only witness
 * is that recipient clicking a dead link - in production.
 *
 * App start is the one moment the framework holds both halves of the equation:
 * the consumer's paths AND the resolved `basePath`. So the checks live here.
 *
 * ⚠️ Deliberately NOT gated on `import.meta.env.DEV`. This package ships a Vite
 * library build, which folds `import.meta.env.DEV` to `false` at ITS build time
 * and strips the guarded branch out of `dist` - and consumers read `dist`, so a
 * dev-gated warning here would be one that can never fire for the people it is
 * written for. Every check below stays silent on a correctly-wired app, so the
 * unconditional warning costs nothing in production.
 */
import type { RouteRecordRaw } from 'vue-router'
import { normalizeName } from './route-transforms'

const TAG = '[tnzi-admin]'

/**
 * Warn about `rootRoutes` entries whose path can't resolve where the consumer
 * expects once `basePath` has been applied.
 *
 * Call BEFORE prefixing, with the paths exactly as the consumer wrote them.
 *
 * @param routes   the consumer's top-level route records, unprefixed
 * @param basePath the normalized base path (`normalizeBasePath` output)
 */
export function warnMisplacedTopLevelRoutes(
  routes: RouteRecordRaw[],
  basePath: string,
): void {
  for (const route of routes) {
    const path = route.path
    if (typeof path !== 'string') continue

    if (path === '/') {
      console.warn(
        `${TAG} rootRoute "/" targets the app root, which the framework owns ` +
          `(the admin shell root resolves to "${basePath}"). The record will not ` +
          `be reachable. Give the page a path of its own instead.`,
      )
      continue
    }

    if (!path.startsWith('/')) {
      console.warn(
        `${TAG} rootRoute "${path}" is not absolute. Top-level routes are siblings ` +
          `of the admin shell root, not children of it, so their paths must start ` +
          `with "/" - vue-router cannot resolve a relative path at the top level.`,
      )
      continue
    }

    if (basePath !== '/' && (path === basePath || path.startsWith(`${basePath}/`))) {
      const stripped = path.slice(basePath.length) || '/'
      console.warn(
        `${TAG} rootRoute "${path}" already carries basePath "${basePath}". ` +
          `Consumer top-level routes are prefixed with basePath just like the ` +
          `framework's own (/login, /403, /share/:token), so this resolves to ` +
          `"${basePath}${path}" and nothing will reach it. Write the path ` +
          `prefix-free: "${stripped}".`,
      )
    }
  }
}

/**
 * Warn about `hideModules` / `showOnlyModules` entries that match no module
 * group.
 *
 * Both options compare against the route names of the groups directly under
 * the admin root (`identity`, `ai`, `payroll`, ...), not against backend module
 * names. An entry that matches none of them is valid TypeScript and changes
 * nothing: `hideModules` hides nothing, and `showOnlyModules` drops the group
 * the consumer meant to keep. Backend names are the likely miss: `AI.Skills`
 * is a page inside the `ai` group (hide it with `hideRoutes`), and
 * `Finance.Payroll` is the `payroll` group.
 *
 * @param routes the route table the option is applied to (before `addModules`)
 * @param option which option the names came from, for the message
 * @param names  the consumer's entries, as written
 */
export function warnUnknownModuleNames(
  routes: RouteRecordRaw[],
  option: 'hideModules' | 'showOnlyModules',
  names: readonly string[],
): void {
  const groups = routes
    .filter((route) => route.path === '/admin')
    .flatMap((route) => route.children ?? [])
    .map((child) => (typeof child.name === 'string' ? child.name : ''))
    .filter((name) => name !== '')
  const known = new Set(groups.map(normalizeName))
  for (const name of names) {
    if (known.has(normalizeName(name))) continue
    console.warn(
      `${TAG} ${option} entry "${name}" matches no module group, so it has no ` +
        `effect. The option compares against the route names of the groups under ` +
        `the admin root: ${groups.join(', ')}. A page inside a group is hidden ` +
        `with hideRoutes instead.`,
    )
  }
}
