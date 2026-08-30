import { defineConfig } from 'vite';
import UnoCSS from 'unocss/vite';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'path';
import { preservedModuleFileName } from '../../tools/vite/preserved-module-filename.mjs';

export default defineConfig({
  // NOTE: .d.ts are emitted by `vue-tsc -p tsconfig.build.json` in the `build`
  // script (vite-plugin-dts 4.x/5.x don't emit per-SFC `.vue.d.ts` with vue-tsc 3).
  plugins: [
    UnoCSS(),
    vue(),
  ],
  build: {
    lib: {
      entry: {
        index: resolve(import.meta.dirname, 'src/index.ts'),
        components: resolve(import.meta.dirname, 'src/components/index.ts'),
        stores: resolve(import.meta.dirname, 'src/stores/index.ts'),
        headless: resolve(import.meta.dirname, 'src/headless/index.ts'),
        resolvers: resolve(import.meta.dirname, 'src/resolvers/index.ts'),
        utils: resolve(import.meta.dirname, 'src/utils/index.ts'),
      },
      name: 'TnziUi',
      formats: ['es'],
      // vite 6+ defaults the lib CSS file to the package name (→ `ui.css`);
      // pin it back to `style.css` so the `./style.css` export + cross-package
      // `@import '@tnzi/ui/style.css'` keep resolving.
      cssFileName: 'style',
    },
    rollupOptions: {
      external: (id) =>
        id === 'vue'
        || id === 'naive-ui'
        || id === 'pinia'
        || id === 'echarts'
        || id === 'vue-draggable-plus'
        || id === '@vueuse/core'
        || id.startsWith('echarts/')
        || id.startsWith('@vueuse/')
        || id.startsWith('@tnzi/core')
        || id.startsWith('@iconify/vue'),
      output: {
        preserveModules: true,
        preserveModulesRoot: resolve(import.meta.dirname, 'src'),
        exports: 'named',
        entryFileNames: preservedModuleFileName,
        globals: {
          vue: 'Vue',
          'naive-ui': 'NaiveUi',
          pinia: 'Pinia',
          '@tnzi/core': 'TnziCore',
        },
      },
    },
    cssCodeSplit: false,
  },
});
