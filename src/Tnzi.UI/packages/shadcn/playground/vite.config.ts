import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';

const webPackagePath = path.resolve(__dirname, '../');
const corePackagePath = path.resolve(__dirname, '../../core/src');

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: [
      {
        find: /^@tnzi\/shadcn\/(.*)$/,
        replacement: `${webPackagePath}/src/$1`,
      },
      {
        find: '@tnzi/shadcn',
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
      {
        find: /^@\//,
        replacement: `${webPackagePath}/src/`,
      },
    ],
  },
  server: {
    port: 3001,
  },
});
