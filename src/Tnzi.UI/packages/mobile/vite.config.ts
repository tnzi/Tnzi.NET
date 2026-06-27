import { defineConfig } from 'vite';
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

export default defineConfig({
  plugins: [
    vue(),
    // .d.ts emitted by `vue-tsc -p tsconfig.build.json` in the build script.
  ],
  build: {
    lib: {
      entry: {
        index: resolve(__dirname, 'src/index.ts'),
        'components/index': resolve(__dirname, 'src/components/index.ts'),
        'stores/index': resolve(__dirname, 'src/stores/index.ts'),
        'headless/index': resolve(__dirname, 'src/headless/index.ts'),
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
        preserveModulesRoot: resolve(__dirname, 'src'),
        exports: 'named',
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
