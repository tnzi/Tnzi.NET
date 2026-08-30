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

export default defineConfig({
  plugins: [
    UnoCSS(),
    vue(),
    // .d.ts emitted by `vue-tsc -p tsconfig.build.json` in the build script.
  ],
  build: {
    lib: {
      entry: {
        index: resolve(import.meta.dirname, 'src/index.ts'),
        'components/index': resolve(import.meta.dirname, 'src/components/index.ts'),
        'stores/index': resolve(import.meta.dirname, 'src/stores/index.ts'),
        // Declared as an entry so the barrel survives: a pure re-export module
        // that is not an entry gets folded into the importer and the
        // `./headless` subpath export would resolve to a missing file.
        'headless/index': resolve(import.meta.dirname, 'src/headless/index.ts'),
        'adapters/index': resolve(import.meta.dirname, 'src/adapters/index.ts'),
      },
      name: 'TnziMobile',
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
        );
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
        },
      },
    },
    cssCodeSplit: false,
  },
});
