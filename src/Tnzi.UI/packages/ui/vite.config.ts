import { defineConfig } from 'vite';
import UnoCSS from 'unocss/vite';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'path';

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
        index: resolve(__dirname, 'src/index.ts'),
        components: resolve(__dirname, 'src/components/index.ts'),
        stores: resolve(__dirname, 'src/stores/index.ts'),
        headless: resolve(__dirname, 'src/headless/index.ts'),
        resolvers: resolve(__dirname, 'src/resolvers/index.ts'),
        utils: resolve(__dirname, 'src/utils/index.ts'),
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
        preserveModulesRoot: resolve(__dirname, 'src'),
        exports: 'named',
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
