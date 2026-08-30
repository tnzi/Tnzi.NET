/**
 * Start-up diagnostics for consumer-supplied TOP-LEVEL routes (`rootRoutes`).
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
