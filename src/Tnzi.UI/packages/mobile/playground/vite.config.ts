import { defineConfig } from 'vite';
import UnoCSS from 'unocss/vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';

const corePackagePath = path.resolve(__dirname, '../../core/src');

export default defineConfig({
  plugins: [UnoCSS(), vue()],
  resolve: {
    dedupe: ['vue'],
    alias: [
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
    port: 3003,
  },
});
