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
        composables: resolve(__dirname, 'src/composables/index.ts'),
        chat: resolve(__dirname, 'src/chat/index.ts'),
        admin: resolve(__dirname, 'src/admin/index.ts'),
        embed: resolve(__dirname, 'src/embed/index.ts'),
      },
      name: 'TnziAi',
      formats: ['es'],
    },
    rollupOptions: {
      external: (id) =>
        id === 'vue' ||
        id.startsWith('@tnzi/core') ||
        id.startsWith('@vue-flow/'),
      output: {
        preserveModules: true,
        preserveModulesRoot: resolve(__dirname, 'src'),
        exports: 'named',
        globals: {
          vue: 'Vue',
          '@tnzi/core': 'TnziCore',
        },
      },
    },
    cssCodeSplit: false,
  },
});
