/**
 * Vite helpers for applications that consume `@tnzi/*`.
 *
 * Node-side config code - never bundled into an app, so it lives outside
 * `src/` and ships as-is rather than through the library build.
 *
 * It lives in `@tnzi/core` because that is the one package every app on
 * `@tnzi/*` has, and it carries no components. `@tnzi/ui/vite` and
 * `@tnzi/ui-ai/vite` re-export it, so an app imports it from whichever
 * package it already names - a page built from the `@tnzi/ui-ai` controls
 * alone does not have to add another dependency for it. The returned
 * plugin's type names no Vite type (see `TnziSingletonsPlugin` in
 * `vite.d.ts`), so it typechecks under any Vite major the app is on.
 *
 * ## Why this exists
 *
 * Vue, Vue Router, Pinia and Naive UI are single-instance libraries: two copies
 * in one page means two `provide`/`inject` registries, two reactivity effect
 * scopes and two Pinia roots. The symptoms are not "module not found" but
 * silent nonsense - a store written by the app that the framework's components
 * never see, `inject()` returning undefined inside a working component tree, a
 * theme provider that only half the tree reads.
 *
 * The `@tnzi/*` packages declare all four as **peerDependencies only** (where
 * they use them at all), so an ordinary `npm i @tnzi/ui-admin` cannot produce
 * a second copy. But under
 * pnpm's `link:` protocol the consumer points at the framework's own working
 * tree, which necessarily carries its own `node_modules` (the packages need Vue
 * to build and test themselves). Bare imports inside the linked `dist/` then
 * resolve upward into *that* tree, and the app gets a second Vue.
 *
 * `resolve.dedupe` is the fix: it forces the listed bare specifiers to resolve
 * from the app's own root no matter which file imported them. This plugin just
 * keeps the list in one place - the framework's - instead of every consumer
 * maintaining a copy that silently goes stale when a fifth singleton appears.
 *
 * ```ts
 * // vite.config.ts of an app on any @tnzi/* package
 * import { tnziSingletons } from '@tnzi/core/vite'
 *
 * export default defineConfig({
 *   plugins: [vue(), tnziSingletons()],
 * })
 * ```
 *
 * `vue` is always deduped; `vue-router`, `pinia` and `naive-ui` are deduped
 * when the app has a copy of its own - see {@link TNZI_SINGLETON_DEPS} and
 * {@link TNZI_SINGLETON_DEPS_WHEN_INSTALLED}.
 *
 * Additive and idempotent: an app that already hand-rolls the same `dedupe` /
 * `alias` block keeps working unchanged - Vite merges the arrays and duplicate
 * entries are harmless.
 *
 * ⚠️ This covers the bundler only. `vue-tsc` resolves types through the
 * consumer's `tsconfig.json`, which this plugin cannot reach; a linked install
 * still needs a matching `paths` block there (see the README section linked
 * from `docs/coding-standards/ui-frontend.md`). TypeScript resolves `paths`
 * relative to the config that declares `baseUrl`, so a base config shipped from
 * here would point back at the framework's copies - exactly the wrong way.
 */

import { existsSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'

/**
 * Bare specifiers that must resolve to exactly one copy per page.
 *
 * ## Entry criterion, and why it is narrow
 *
 * A name belongs here only if **the consuming app is guaranteed to own a copy**
 * - i.e. every `@tnzi/*` package that touches it declares it as a
 * `peerDependency`, never as a `dependency`. `resolve.dedupe` works by
 * re-resolving the specifier from the app's own root; if the app has nothing to
 * resolve, Vite's `tryNodeResolve` hands back *nothing* (it sets
 * `basedir = root` and returns early when the package is missing) rather than
 * retrying from the importer. Listing a name the app does not own therefore
 * ranges from a silent no-op to a hard "failed to resolve import".
 *
 * That is exactly how `@vue/reactivity` used to break list pages. It sat on
 * this list while `@tnzi/core` declared it as a regular `dependency`, and no
 * application depends on it directly - Vue pulls it in transitively, so it is
 * not resolvable from an app root under pnpm. The dedupe entry could never
 * fire, the linked `dist/` resolved the framework's own copy, and the app ended
 * up with two reactivity runtimes. Dependency tracking is module-level state,
 * so a `computed()` built by one instance never subscribes to a `reactive()`
 * proxy built by the other: controllers kept updating, views never heard about
 * it, and nothing threw. `@tnzi/core` now imports `reactive` from `vue`
 * instead - the copy the host already owns - so the second runtime is gone
 * rather than deduped, and the entry is gone with it.
 *
 * The bar for the unconditional list is therefore "**every app inevitably has
 * its own copy**", whichever `@tnzi/*` package it is built on, and only `vue`
 * clears it.
 *
 * `vue-router`, `pinia` and `naive-ui` are singletons the framework imports
 * that only some apps own: an admin app owns all three, an app on
 * `@tnzi/ui-ai` owns `vue-router` when it routes (and `TAuthRoute`,
 * `TGuestWelcome` and `defineChatApp` import it) but neither of the others.
 * They are in {@link TNZI_SINGLETON_DEPS_WHEN_INSTALLED}: the plugin dedupes
 * each one only when the app's root resolves it, by the same lookup Vite's
 * dedupe then does (`node_modules/<name>/package.json` from the root
 * upwards). Deduped where the app owns it, left alone where it does not, so
 * neither half of the `@vue/reactivity` failure can happen and no app has to
 * remember which names its package needs.
 *
 * `echarts` and `@iconify/vue` are deliberately absent even though a second
 * copy of either really does split a registry. Apps routinely have neither:
 * `@tnzi/ui-admin` ships both as regular dependencies and lazy-loads echarts
 * behind a capability check, so an app can consume every chart and icon the
 * framework renders without ever importing them itself, and then the
 * framework's copy is the only one. Apps that DO use them directly should pass
 * them via `extra`.
 */
export const TNZI_SINGLETON_DEPS = Object.freeze([
  'vue',
])

/**
 * Singletons deduped only when the app owns a copy (see {@link TNZI_SINGLETON_DEPS}).
 */
export const TNZI_SINGLETON_DEPS_WHEN_INSTALLED = Object.freeze([
  'vue-router',
  'pinia',
  'naive-ui',
])

/**
 * Whether `name` resolves from `root` the way Vite resolves a deduped
 * specifier: a `node_modules/<name>/package.json` in `root` or above it.
 *
 * @param {string} root
 * @param {string} name
 * @returns {boolean}
 */
export function appOwns(root, name) {
  let dir = resolve(root)
  for (;;) {
    if (existsSync(join(dir, 'node_modules', name, 'package.json'))) return true
    const parent = dirname(dir)
    if (parent === dir) return false
    dir = parent
  }
}

/**
 * Vite plugin that pins the `@tnzi/*` single-instance dependencies to the
 * consuming app's own copy: `vue`, and each of `vue-router`, `pinia` and
 * `naive-ui` the app's root resolves.
 *
 * @param {{ extra?: string[] }} [options]
 *   `extra` appends further specifiers, deduped unconditionally - for
 *   singletons the app itself shares with the framework and therefore has in
 *   its own `node_modules` (`@iconify/vue` when the app registers its own
 *   icons, `echarts` when it renders its own charts). Only pass names the app
 *   actually depends on; see {@link TNZI_SINGLETON_DEPS} for why that matters.
 * @returns {import('./vite.d.ts').TnziSingletonsPlugin}
 */
export function tnziSingletons(options = {}) {
  return {
    name: 'tnzi:singletons',
    // `config` (not `configResolved`): the list has to be merged before Vite
    // resolves anything, and returning a partial config is how Vite merges
    // arrays additively with whatever the app already declared. The root is
    // the one Vite will use: the config's `root`, else the working directory.
    config(config) {
      const root = resolve(config.root ?? process.cwd())
      const owned = TNZI_SINGLETON_DEPS_WHEN_INSTALLED.filter((name) => appOwns(root, name))
      return { resolve: { dedupe: [...TNZI_SINGLETON_DEPS, ...owned, ...(options.extra ?? [])] } }
    },
  }
}
