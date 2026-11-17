/**
 * The controls stylesheet, `dist/controls.css` (`@tnzi/ui-ai/controls.css`):
 * the tokens, the global control classes, the tooltip and the scoped styles of
 * every component `@tnzi/ui-ai/controls` and `@tnzi/ui-ai/dialogs` reach, for a
 * page that uses only those entries (a desktop window, a widget built from
 * controls) and should not load the whole `style.css`.
 *
 * A second build rather than a split of the main one: the main build keeps
 * `cssCodeSplit` off so the package stylesheet stays one file, and a split
 * would give per-module sheets to stitch back together. This one builds the
 * controls entry on its own and keeps only its CSS. Same root, same Vue plugin
 * and same mode as the main build, so the scoped attribute of each component
 * (`data-v-<hash of path and source>`) is the one the main build's JavaScript
 * renders (gate: `__tests__/conventions/controls-stylesheet.test.ts`).
 */
import { defineConfig, type Plugin } from 'vite';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'path';
import { hoverMediaPlugin } from '../../tools/vite/hover-media.mjs';

/** Drops the JavaScript of this build: the main build already ships it. */
function cssOnly(): Plugin {
  return {
    name: 'tnzi-controls-css-only',
    enforce: 'post',
    generateBundle: {
      // After Vite's CSS plugin has collected the chunks' styles into the sheet.
      order: 'post',
      handler(_options, bundle) {
        for (const [name, file] of Object.entries(bundle)) {
          if (file.type === 'chunk') delete bundle[name];
        }
      },
    },
  };
}

export default defineConfig({
  plugins: [vue(), cssOnly()],
  css: {
    postcss: {
      plugins: [hoverMediaPlugin()],
    },
  },
  resolve: {
    alias: {
      '@': resolve(import.meta.dirname, 'src'),
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: false,
    lib: {
      entry: { 'controls-stylesheet': resolve(import.meta.dirname, 'src/controls-stylesheet.ts') },
      formats: ['es'],
      cssFileName: 'controls',
    },
    rolldownOptions: {
      external: (id) => ['vue', '@iconify/vue', 'uqr', '@tnzi/ui', '@tnzi/core'].some((name) => id === name || id.startsWith(`${name}/`)),
    },
    cssCodeSplit: false,
  },
});
