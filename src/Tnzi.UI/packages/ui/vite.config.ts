import { defineConfig } from 'vite';
import UnoCSS from 'unocss/vite';
import vue from '@vitejs/plugin-vue';
import dts from 'vite-plugin-dts';
import { resolve } from 'path';

export default defineConfig({
  plugins: [
    UnoCSS(),
    vue(),
    dts({
      include: ['src/**/*'],
      exclude: ['src/__tests__/**'],
      outDir: 'dist',
      entryRoot: resolve(__dirname, 'src'),
    }),
  ],
  build: {
    lib: {
      entry: {
        index: resolve(__dirname, 'src/index.ts'),
        components: resolve(__dirname, 'src/components/index.ts'),
        stores: resolve(__dirname, 'src/stores/index.ts'),
        composables: resolve(__dirname, 'src/composables/index.ts'),
        resolvers: resolve(__dirname, 'src/resolvers/index.ts'),
      },
      name: 'TnziUi',
      formats: ['es'],
    },
    rollupOptions: {
      external: (id) => id === 'vue' || id === 'naive-ui' || id === 'pinia' || id.startsWith('@tnzi/core') || id.startsWith('@iconify/vue'),
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
