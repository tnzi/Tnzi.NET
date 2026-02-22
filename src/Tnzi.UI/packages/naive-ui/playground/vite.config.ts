import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';

const corePackagePath = path.resolve(__dirname, '../../core/src');
const naiveUiPackagePath = path.resolve(__dirname, '../src');

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: [
      {
        find: /^@tnzi\/naive-ui\/(.*)$/,
        replacement: `${naiveUiPackagePath}/$1`,
      },
      {
        find: '@tnzi/naive-ui',
        replacement: path.resolve(__dirname, '../src/index.ts'),
      },
      {
        find: /^@tnzi\/core\/(.*)$/,
        replacement: `${corePackagePath}/$1`,
      },
      {
        find: '@tnzi/core',
        replacement: path.resolve(__dirname, '../../core/src/index.ts'),
      },
    ],
  },
  server: {
    port: 3004,
  },
});
