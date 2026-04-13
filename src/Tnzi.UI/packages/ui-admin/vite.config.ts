import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import dts from 'vite-plugin-dts';
import { resolve } from 'path';

export default defineConfig({
  plugins: [
    vue(),
    dts({
      include: ['src/**/*'],
      outDir: 'dist',
      entryRoot: resolve(__dirname, 'src'),
    }),
  ],
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
    },
  },
  build: {
    lib: {
      entry: {
        index: resolve(__dirname, 'src/index.ts'),
        components: resolve(__dirname, 'src/components/index.ts'),
        headless: resolve(__dirname, 'src/headless/index.ts'),
        pages: resolve(__dirname, 'src/pages/index.ts'),
        template: resolve(__dirname, 'src/template/index.ts'),
      },
      name: 'TnziUiAdmin',
      formats: ['es'],
    },
    rollupOptions: {
      external: (id) =>
        id === 'vue' ||
        id === 'pinia' ||
        id === 'vue-router' ||
        id.startsWith('@tnzi/core') ||
        id.startsWith('@tnzi/ui'),
      output: {
        preserveModules: true,
        preserveModulesRoot: resolve(__dirname, 'src'),
        exports: 'named',
        globals: {
          vue: 'Vue',
          pinia: 'Pinia',
          'vue-router': 'VueRouter',
          '@tnzi/core': 'TnziCore',
          '@tnzi/ui': 'TnziUi',
        },
      },
    },
    cssCodeSplit: false,
  },
});
