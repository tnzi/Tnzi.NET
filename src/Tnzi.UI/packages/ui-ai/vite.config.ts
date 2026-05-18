import { defineConfig } from 'vite';
import UnoCSS from 'unocss/vite';
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
    UnoCSS(),
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
        embed: resolve(__dirname, 'src/embed/index.ts'),
        locale: resolve(__dirname, 'src/locale/index.ts'),
        shell: resolve(__dirname, 'src/shell/index.ts'),
        utils: resolve(__dirname, 'src/utils/index.ts'),
        // Declaring `themes` as a top-level entry forces rollup to
        // preserve the named re-exports in `themes/index.ts`
        // (applyAiTheme / lightTokens / darkTokens / AiThemeTokens).
        // Otherwise tree-shake strips the `export { … } from
        // './tokens'` line and leaves only the locally-defined
        // applyTheme / resetTheme. Consumers can import the barrel
        // via the `./themes/*` subpath declared in package.json.
        themes: resolve(__dirname, 'src/themes/index.ts'),
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
