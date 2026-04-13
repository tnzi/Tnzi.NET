import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';

const corePackagePath = path.resolve(__dirname, '../../packages/core/src');

export default defineConfig({
  plugins: [vue()],
  resolve: {
    dedupe: ['vue'],
    alias: [
      {
        find: /^@tnzi\/core\/(.*)$/,
        replacement: `${corePackagePath}/$1`,
      },
      {
        find: '@tnzi/core',
        replacement: path.resolve(__dirname, '../../packages/core/src/index.ts'),
      },
      {
        find: '@tnzi/ui',
        replacement: path.resolve(__dirname, 'src/index.ts'),
      },
      {
        find: '@',
        replacement: path.resolve(__dirname, 'src'),
      },
    ],
  },
  server: {
    port: 3010,
  },
});
