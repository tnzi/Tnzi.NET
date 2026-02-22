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
  build: {
    lib: {
      entry: {
        index: resolve(__dirname, 'src/index.ts'),
        components: resolve(__dirname, 'src/components/index.ts'),
        stores: resolve(__dirname, 'src/stores/index.ts'),
      },
      name: 'TnziNaiveUi',
      formats: ['es'],
    },
    rollupOptions: {
      external: (id) => id === 'vue' || id === 'naive-ui' || id === 'pinia' || id.startsWith('@tnzi/core'),
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
