import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'path';

const uiSrc = resolve(__dirname, '../../ui/src');

export default defineConfig({
  plugins: [vue()],
  resolve: {
    dedupe: ['vue'],
    alias: [
      // @tnzi/ui — resolve to source (no dist build needed)
      { find: /^@tnzi\/ui\/(.*)$/, replacement: `${uiSrc}/$1` },
      { find: '@tnzi/ui', replacement: resolve(uiSrc, 'index.ts') },
      // @tnzi/ui-ai sub-paths — all source-resolved for live HMR while
      // iterating on the package itself.
      { find: '@tnzi/ui-ai/locale', replacement: resolve(__dirname, '../src/locale') },
      { find: '@tnzi/ui-ai/themes', replacement: resolve(__dirname, '../src/themes') },
      { find: '@tnzi/ui-ai/components', replacement: resolve(__dirname, '../src/components') },
      { find: '@tnzi/ui-ai/composables', replacement: resolve(__dirname, '../src/composables') },
      { find: '@tnzi/ui-ai/chat', replacement: resolve(__dirname, '../src/chat') },
      { find: '@tnzi/ui-ai/shell', replacement: resolve(__dirname, '../src/shell') },
      { find: '@tnzi/ui-ai/embed', replacement: resolve(__dirname, '../src/embed') },
      { find: '@tnzi/ui-ai/utils', replacement: resolve(__dirname, '../src/utils') },
      // `style.css` is the published bundle (it transitively imports
      // `@tnzi/ui/style.css`, which only exists at `dist/style.css`),
      // so the playground consumes the built artefact rather than
      // source. Requires `pnpm --filter @tnzi/ui-ai build` to have run
      // at least once. Must precede the catch-all `@tnzi/ui-ai` alias
      // below — Vite alias matching is prefix-based and first-wins.
      { find: '@tnzi/ui-ai/style.css', replacement: resolve(__dirname, '../dist/style.css') },
      { find: '@tnzi/ui-ai', replacement: resolve(__dirname, '../src') },
      { find: '@', replacement: resolve(__dirname, '../src') },
    ],
  },
  server: {
    port: 4200,
    strictPort: false,
  },
});
