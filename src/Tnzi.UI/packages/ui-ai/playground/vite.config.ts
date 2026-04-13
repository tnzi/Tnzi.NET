import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'path';

const uiSrc = resolve(__dirname, '../../ui/src');
const coreSrc = resolve(__dirname, '../../core/src');

export default defineConfig({
  plugins: [vue()],
  resolve: {
    dedupe: ['vue'],
    alias: [
      // @tnzi/ui — resolve to source (no dist build needed)
      { find: /^@tnzi\/ui\/(.*)$/, replacement: `${uiSrc}/$1` },
      { find: '@tnzi/ui', replacement: resolve(uiSrc, 'index.ts') },
      // @tnzi/core — resolve to source
      { find: /^@tnzi\/core\/(.*)$/, replacement: `${coreSrc}/$1` },
      { find: '@tnzi/core', replacement: resolve(coreSrc, 'index.ts') },
      // @tnzi/ui-ai sub-paths
      { find: '@tnzi/ui-ai/locale', replacement: resolve(__dirname, '../src/locale') },
      { find: '@tnzi/ui-ai/themes', replacement: resolve(__dirname, '../src/themes') },
      { find: '@tnzi/ui-ai/components', replacement: resolve(__dirname, '../src/components') },
      { find: '@tnzi/ui-ai/composables', replacement: resolve(__dirname, '../src/composables') },
      { find: '@tnzi/ui-ai/chat', replacement: resolve(__dirname, '../src/chat') },
      { find: '@tnzi/ui-ai/admin', replacement: resolve(__dirname, '../src/admin') },
      { find: '@tnzi/ui-ai/embed', replacement: resolve(__dirname, '../src/embed') },
      { find: '@tnzi/ui-ai', replacement: resolve(__dirname, '../src') },
      { find: '@', replacement: resolve(__dirname, '../src') },
    ],
  },
  server: {
    port: 4200,
    strictPort: false,
  },
});
