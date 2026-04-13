import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import dts from 'vite-plugin-dts';
import { readFileSync } from 'fs';
import { resolve } from 'path';

const packageJson = JSON.parse(readFileSync(resolve(__dirname, 'package.json'), 'utf8')) as {
  dependencies?: Record<string, string>;
  peerDependencies?: Record<string, string>;
};

const externalPackages = new Set([
  ...Object.keys(packageJson.dependencies ?? {}),
  ...Object.keys(packageJson.peerDependencies ?? {}),
]);

const uiDistPath = resolve(__dirname, '../ui/dist').replace(/\\/g, '/');
const coreDistPath = resolve(__dirname, '../core/dist').replace(/\\/g, '/');

export default defineConfig({
  plugins: [
    vue(),
    dts({
      include: ['src/**/*'],
      exclude: ['src/**/*.test.ts'],
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
      external: (id) => {
        const normalizedId = id.replace(/\\/g, '/');

        return Array.from(externalPackages).some(packageName =>
          normalizedId === packageName || normalizedId.startsWith(`${packageName}/`)
        ) ||
          normalizedId.startsWith(coreDistPath) ||
          normalizedId.startsWith(uiDistPath) ||
          normalizedId.startsWith('@vue-flow/');
      },
      output: {
        preserveModules: true,
        preserveModulesRoot: resolve(__dirname, 'src'),
        exports: 'named',
        globals: {
          vue: 'Vue',
          pinia: 'Pinia',
          '@tnzi/core': 'TnziCore',
          '@tnzi/ui': 'TnziUi',
        },
      },
    },
    cssCodeSplit: false,
  },
});
