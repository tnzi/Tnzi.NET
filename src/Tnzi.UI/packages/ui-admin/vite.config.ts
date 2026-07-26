import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import UnoCSS from 'unocss/vite';
import { resolve } from 'path';

export default defineConfig({
  plugins: [
    // UnoCSS compiles the atoms referenced by .vue / .ts under src/ into
    // dist/style.css. Consumers `import '@tnzi/ui-admin/style.css'` and pick
    // up the precomputed atoms. They don't need to install unocss to use
    // the library - the atoms are already baked in.
    UnoCSS(),
    vue(),
    // .d.ts emitted by `vue-tsc -p tsconfig.build.json` in the build script.
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
        presets: resolve(__dirname, 'src/presets/index.ts'),
        // locales barrel needs its own entry - consumers import the
        // aggregate `@tnzi/ui-admin/locales`; without this entry,
        // preserveModules tree-shakes the barrel and the subpath 404s.
        'locales/index': resolve(__dirname, 'src/locales/index.ts'),
      },
      name: 'TnziUiAdmin',
      formats: ['es'],
      // vite 6+ defaults the lib CSS file to the package name; pin to `style.css`
      // so the `./style.css` export keeps resolving.
      cssFileName: 'style',
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
        id === 'vue-draggable-plus' ||
        id.startsWith('echarts') ||
        id.startsWith('@vueuse/') ||
        id.startsWith('@css-render/') ||
        id.startsWith('@juggle/') ||
        id.startsWith('@tnzi/core') ||
        id.startsWith('@tnzi/ui') ||
        id.startsWith('@tnzi/ui-ai'),
      output: {
        preserveModules: true,
        preserveModulesRoot: resolve(__dirname, 'src'),
        exports: 'named',
        globals: {
          vue: 'Vue',
          pinia: 'Pinia',
          'vue-router': 'VueRouter',
          'naive-ui': 'NaiveUI',
          'vue-draggable-plus': 'VueDraggablePlus',
          echarts: 'echarts',
          '@tnzi/core': 'TnziCore',
          '@tnzi/ui': 'TnziUi',
          '@tnzi/ui-ai': 'TnziUiAi',
        },
      },
    },
    cssCodeSplit: false,
  },
});
