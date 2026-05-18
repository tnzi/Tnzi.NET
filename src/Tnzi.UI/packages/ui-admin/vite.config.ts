import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import UnoCSS from 'unocss/vite';
import dts from 'vite-plugin-dts';
import { resolve } from 'path';

export default defineConfig({
  plugins: [
    // UnoCSS compiles the atoms referenced by .vue / .ts under src/ into
    // dist/style.css. Consumers `import '@tnzi/ui-admin/style.css'` and pick
    // up the precomputed atoms. They don't need to install unocss to use
    // the library — the atoms are already baked in.
    UnoCSS(),
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
        router: resolve(__dirname, 'src/router/index.ts'),
        stores: resolve(__dirname, 'src/stores/index.ts'),
        template: resolve(__dirname, 'src/template/index.ts'),
        presets: resolve(__dirname, 'src/presets/index.ts'),
      },
      name: 'TnziUiAdmin',
      formats: ['es'],
    },
    rollupOptions: {
      external: (id) =>
        id === 'vue' ||
        id === 'pinia' ||
        id === 'pinia-plugin-persistedstate' ||
        id === 'vue-router' ||
        id === 'naive-ui' ||
        id === 'vueuc' ||
        id === 'css-render' ||
        id === '@iconify/vue' ||
        id.startsWith('@vueuse/') ||
        id.startsWith('@css-render/') ||
        id.startsWith('@juggle/') ||
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
          'naive-ui': 'NaiveUI',
          '@tnzi/core': 'TnziCore',
          '@tnzi/ui': 'TnziUi',
        },
      },
    },
    cssCodeSplit: false,
  },
});
