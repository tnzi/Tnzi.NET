import { defineConfig } from 'vite';
import UnoCSS from 'unocss/vite';
import vue from '@vitejs/plugin-vue';
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
        // Pre-auth surface. Its own entry so a consumer can load the sign-in
        // page without pulling `TChatApp` and the conversation tree with it.
        auth: resolve(__dirname, 'src/auth/index.ts'),
        // DTO -> view-model mapping. Its own entry so a consumer can map
        // without importing any component.
        adapters: resolve(__dirname, 'src/adapters/index.ts'),
        // Application assembly. Separate entry because it is the only one that
        // needs vue-router.
        plugin: resolve(__dirname, 'src/plugin/index.ts'),
        headless: resolve(__dirname, 'src/headless/index.ts'),
        // Drop-in chat product shell. Its own entry so a chat consumer does not
        // pay for the workflow / knowledge / skill domains in ./components.
        chat: resolve(__dirname, 'src/chat-app.ts'),
        embed: resolve(__dirname, 'src/embed/index.ts'),
        i18n: resolve(__dirname, 'src/i18n/index.ts'),
        locales: resolve(__dirname, 'src/locales/index.ts'),
        utils: resolve(__dirname, 'src/utils/index.ts'),
        // Everything that touches @vue-flow/core. A dedicated entry keeps the
        // heavy dep reachable only through `@tnzi/ui-ai/workflow`.
        workflow: resolve(__dirname, 'src/workflow/index.ts'),
        // Declaring `theme` as a top-level entry forces rollup to
        // preserve the named re-exports in `theme/index.ts`
        // (applyAiTheme / lightTokens / darkTokens / AiThemeTokens).
        // Otherwise tree-shake strips the `export { … } from
        // './tokens'` line and leaves only the locally-defined
        // applyTheme / resetTheme. Consumers can import the barrel
        // via the `./theme/*` subpath declared in package.json.
        theme: resolve(__dirname, 'src/theme/index.ts'),
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
