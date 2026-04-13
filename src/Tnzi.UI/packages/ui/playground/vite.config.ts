import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import UnoCSS from 'unocss/vite';
import path from 'path';

const corePackagePath = path.resolve(__dirname, '../../core/src');
const uiPackagePath = path.resolve(__dirname, '../src');

export default defineConfig({
  plugins: [
    UnoCSS({
      configFile: path.resolve(__dirname, '../uno.config.ts'),
    }),
    vue(),
  ],
  resolve: {
    alias: [
      {
        find: /^@tnzi\/ui\/(.*)$/,
        replacement: `${uiPackagePath}/$1`,
      },
      {
        find: '@tnzi/ui',
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
