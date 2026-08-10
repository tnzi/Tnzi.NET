import type { Plugin } from 'vite'

/**
 * Bare specifiers that must resolve to exactly one copy per page.
 * See the JSDoc in `vite.mjs` for why each one is on the list.
 */
export declare const TNZI_SINGLETON_DEPS: readonly string[]

export interface TnziSingletonsOptions {
  /** Additional bare specifiers to dedupe alongside the framework's own. */
  extra?: string[]
}

/**
 * Vite plugin that pins the `@tnzi/*` single-instance dependencies (Vue, Vue
 * Router, Pinia, Naive UI) to the consuming app's own copy.
 *
 * Needed for `link:` installs, where bare imports inside the linked `dist/`
 * would otherwise resolve into the framework's own `node_modules`.
 */
export declare function tnziSingletons(options?: TnziSingletonsOptions): Plugin
