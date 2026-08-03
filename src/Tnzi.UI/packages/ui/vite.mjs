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
 * `@vue/reactivity` is on the list because `@tnzi/core`'s state managers are
 * built on it directly: a second copy means the app's Vue never tracks the
 * refs those managers hand out, and the UI simply stops updating. If the app
 * cannot resolve it from its own root (pnpm does not hoist it), Vite falls back
 * to normal resolution and nothing breaks.
 */
export const TNZI_SINGLETON_DEPS = Object.freeze([
  'vue',
  'vue-router',
  'pinia',
  'naive-ui',
  '@vue/reactivity',
])

/**
 * Vite plugin that pins the `@tnzi/*` single-instance dependencies to the
 * consuming app's own copy.
 *
 * @param {{ extra?: string[] }} [options]
 *   `extra` appends further specifiers - use it for singletons the app itself
 *   shares with the framework (e.g. `@iconify/vue` when the app registers its
 *   own icons, or `echarts`).
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
