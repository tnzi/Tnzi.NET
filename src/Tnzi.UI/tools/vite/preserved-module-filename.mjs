/**
 * `entryFileNames` for library builds that run with `output.preserveModules`.
 *
 * With `preserveModules`, every module gets its own output file named after
 * its id. `@vitejs/plugin-vue` splits an SFC into virtual modules whose ids
 * carry a query string - `Foo.vue?vue&type=script&setup=true&lang` - and
 * Rolldown (Vite 8's bundler) passes that query straight into the output path.
 * On Windows `?` is not a legal filename character, so the write fails with
 * `os error 123`; on POSIX it would silently produce junk filenames.
 *
 * Rollup (Vite <= 7) dropped the query before naming the chunk, which is how
 * the published packages ended up with `Foo.vue.js` / `Foo.vue2.js`. Stripping
 * the query here reproduces exactly that layout, so `dist` stays byte-for-byte
 * compatible with what consumers' `exports` maps and deep imports expect.
 *
 * @param {{ name: string }} chunk
 * @returns {string}
 */
export function preservedModuleFileName(chunk) {
  return `${chunk.name.replace(/\?.*$/, '')}.js`;
}
