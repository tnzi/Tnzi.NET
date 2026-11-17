/**
 * Bare specifiers that must resolve to exactly one copy per page.
 * See the JSDoc in `vite.mjs` for why each one is on the list.
 */
export declare const TNZI_SINGLETON_DEPS: readonly string[]

/**
 * Singletons deduped only when the app's root resolves them
 * (`vue-router`, `pinia`, `naive-ui`).
 */
export declare const TNZI_SINGLETON_DEPS_WHEN_INSTALLED: readonly string[]

/**
 * Whether `name` resolves from `root` as Vite resolves a deduped specifier:
 * a `node_modules/<name>/package.json` in `root` or a directory above it.
 */
export declare function appOwns(root: string, name: string): boolean

export interface TnziSingletonsOptions {
  /** Additional bare specifiers to dedupe, unconditionally, alongside the framework's own. */
  extra?: string[]
}

/**
 * The plugin `tnziSingletons()` returns, spelled out rather than typed as
 * `import('vite').Plugin`.
 *
 * A declaration that imports `vite` binds to whichever `vite` resolves from
 * this file. In a linked install that is the framework's own copy, whatever
 * major the app is on, and Vite's `Plugin` types are not assignable across
 * majors: a Vite 7 app's `plugins: [tnziSingletons()]` then fails to
 * typecheck against a Vite 8 `Plugin`. This shape mentions no Vite type, so
 * it is assignable to `Plugin` of any Vite with a `config` hook (every
 * version the framework supports), and the package needs no `vite` peer.
 */
export interface TnziSingletonsPlugin {
  name: 'tnzi:singletons'
  /** Merges `resolve.dedupe` into the app's config, before anything resolves. */
  config(config: { root?: string }): { resolve: { dedupe: string[] } }
}

/**
 * Vite plugin that pins the `@tnzi/*` single-instance dependencies to the
 * consuming app's own copy: `vue` always, each of `vue-router`, `pinia` and
 * `naive-ui` that the app's root resolves, and what `extra` names.
 *
 * Needed for `link:` installs, where bare imports inside the linked `dist/`
 * would otherwise resolve into the framework's own `node_modules`.
 */
export declare function tnziSingletons(options?: TnziSingletonsOptions): TnziSingletonsPlugin
