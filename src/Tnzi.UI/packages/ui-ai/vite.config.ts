import { defineConfig } from 'vite';
import UnoCSS from 'unocss/vite';
import vue from '@vitejs/plugin-vue';
import { readFileSync } from 'fs';
import { resolve } from 'path';
import { preservedModuleFileName } from '../../tools/vite/preserved-module-filename.mjs';

const packageJson = JSON.parse(readFileSync(resolve(import.meta.dirname, 'package.json'), 'utf8')) as {
  dependencies?: Record<string, string>;
  peerDependencies?: Record<string, string>;
};

const externalPackages = new Set([
  ...Object.keys(packageJson.dependencies ?? {}),
  ...Object.keys(packageJson.peerDependencies ?? {}),
]);

const uiDistPath = resolve(import.meta.dirname, '../ui/dist').replace(/\\/g, '/');
const coreDistPath = resolve(import.meta.dirname, '../core/dist').replace(/\\/g, '/');

export default defineConfig({
  plugins: [
    UnoCSS(),
    vue(),
    // .d.ts emitted by `vue-tsc -p tsconfig.build.json` in the build script.
  ],
  resolve: {
    alias: {
      '@': resolve(import.meta.dirname, 'src'),
    },
  },
  build: {
    lib: {
      entry: {
        index: resolve(import.meta.dirname, 'src/index.ts'),
        components: resolve(import.meta.dirname, 'src/components/index.ts'),
        // Pre-auth surface. Its own entry so a consumer can load the sign-in
        // page without pulling `TChatApp` and the conversation tree with it.
        auth: resolve(import.meta.dirname, 'src/auth/index.ts'),
        // DTO -> view-model mapping. Its own entry so a consumer can map
        // without importing any component.
        adapters: resolve(import.meta.dirname, 'src/adapters/index.ts'),
        // Application assembly. Separate entry because it is the only one that
        // needs vue-router.
        plugin: resolve(import.meta.dirname, 'src/plugin/index.ts'),
        headless: resolve(import.meta.dirname, 'src/headless/index.ts'),
        // Drop-in chat product shell. Its own entry so a chat consumer does not
        // pay for the workflow / knowledge / skill domains in ./components.
        chat: resolve(import.meta.dirname, 'src/chat-app.ts'),
        embed: resolve(import.meta.dirname, 'src/embed/index.ts'),
        i18n: resolve(import.meta.dirname, 'src/i18n/index.ts'),
        locales: resolve(import.meta.dirname, 'src/locales/index.ts'),
        utils: resolve(import.meta.dirname, 'src/utils/index.ts'),
        // Everything that touches @vue-flow/core. A dedicated entry keeps the
        // heavy dep reachable only through `@tnzi/ui-ai/workflow`.
        workflow: resolve(import.meta.dirname, 'src/workflow/index.ts'),
        // Declaring `theme` as a top-level entry forces rollup to
        // preserve the named re-exports in `theme/index.ts`
        // (applyAiTheme / lightTokens / darkTokens / AiThemeTokens).
        // Otherwise tree-shake strips the `export { … } from
        // './tokens'` line and leaves only the locally-defined
        // applyTheme / resetTheme. Consumers can import the barrel
        // via the `./theme/*` subpath declared in package.json.
        theme: resolve(import.meta.dirname, 'src/theme/index.ts'),
      },
      name: 'TnziAi',
      formats: ['es'],
      // vite 6+ defaults the lib CSS file to the package name; pin to `style.css`
      // so the `./style.css` export keeps resolving.
      cssFileName: 'style',
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
        preserveModulesRoot: resolve(import.meta.dirname, 'src'),
        exports: 'named',
        entryFileNames: preservedModuleFileName,
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
