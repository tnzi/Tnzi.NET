/**
 * Vite helpers for applications that consume `@tnzi/*`.
 *
 * Node-side config code - never bundled into an app, so it lives outside
 * `src/` and ships as-is rather than through the library build.
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
 * The `@tnzi/*` packages already declare all four as **peerDependencies only**,
 * so an ordinary `npm i @tnzi/ui-admin` cannot produce a second copy. But under
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
 * // vite.config.ts
 * import { tnziSingletons } from '@tnzi/ui/vite'
 *
 * export default defineConfig({
 *   plugins: [vue(), tnziSingletons()],
 * })
 * ```
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
 * The bar is therefore higher than "two copies would be bad". It is "**the app
 * inevitably has its own copy**", and only the four below clear it: an app
 * cannot be a Vue app without `vue`, and it cannot drive the framework's
 * router / stores / component theming without owning `vue-router`, `pinia` and
 * `naive-ui` too.
 *
 * `echarts` and `@iconify/vue` are deliberately absent even though a second
 * copy of either really does split a registry. Apps routinely have neither:
 * `@tnzi/ui-admin` ships both as regular dependencies and lazy-loads echarts
 * behind a capability check, so an app can consume every chart and icon the
 * framework renders without ever importing them itself. Listing them would
 * then be the `@vue/reactivity` mistake again - at best inert, at worst a
 * build-breaking "failed to resolve import". Apps that DO use them directly
 * should pass them via `extra`.
 */
export const TNZI_SINGLETON_DEPS = Object.freeze([
  'vue',
  'vue-router',
  'pinia',
  'naive-ui',
])

/**
 * Vite plugin that pins the `@tnzi/*` single-instance dependencies to the
 * consuming app's own copy.
 *
 * @param {{ extra?: string[] }} [options]
 *   `extra` appends further specifiers - use it for singletons the app itself
 *   shares with the framework and therefore has in its own `node_modules`
 *   (e.g. `@iconify/vue` when the app registers its own icons, or `echarts`
 *   when it renders its own charts). Only pass names the app actually depends
 *   on; see {@link TNZI_SINGLETON_DEPS} for why that matters.
 * @returns {import('vite').Plugin}
 */
export function tnziSingletons(options = {}) {
  const dedupe = [...TNZI_SINGLETON_DEPS, ...(options.extra ?? [])]
  return {
    name: 'tnzi:singletons',
    // `config` (not `configResolved`): the list has to be merged before Vite
    // resolves anything, and returning a partial config is how Vite merges
    // arrays additively with whatever the app already declared.
    config() {
      return { resolve: { dedupe } }
    },
  }
}
