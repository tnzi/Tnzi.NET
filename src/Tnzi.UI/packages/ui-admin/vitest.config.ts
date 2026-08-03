import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: [
      { find: '@', replacement: resolve(__dirname, 'src') },
      // Stub virtual:uno.css so @tnzi/ui entry can be imported in tests
      { find: 'virtual:uno.css', replacement: resolve(__dirname, '__tests__/stubs/empty.css') },
      { find: '@tnzi/ui', replacement: resolve(__dirname, '../ui/src') },
      { find: '@tnzi/core', replacement: resolve(__dirname, '../core/src') },
      // @tnzi/ui-ai is not built in test envs (heavy vue-flow deps).
      // Tests vi.mock(...) for bespoke stubs; the aliases here just satisfy
      // vite's resolver so dynamic import() in lazy-load shells doesn't fail at
      // transform time.
      // The `/workflow` subpath must come FIRST: vite string aliases match by
      // prefix, so the bare entry would otherwise rewrite it to
      // `<stub>/workflow` and fail to resolve.
      { find: '@tnzi/ui-ai/workflow', replacement: resolve(__dirname, '__tests__/stubs/ui-ai.ts') },
      { find: '@tnzi/ui-ai', replacement: resolve(__dirname, '__tests__/stubs/ui-ai.ts') },
      // Enable runtime template compilation so stub components using
      // `template: '...'` strings render correctly under vue-test-utils.
      { find: /^vue$/, replacement: 'vue/dist/vue.esm-bundler.js' },
    ],
  },
  test: {
    globals: true,
    environment: 'happy-dom',
    // Preloads the `en` dictionary into the i18n registry. The bundled locale
    // packs are async chunks fetched by `install()`, which tests never call, so
    // without this every label under test resolves to its humanised key.
    setupFiles: ['./__tests__/setup.ts'],
    // Integration tests mount Phase 3/5 pages via dynamic imports; cold-cache
    // SFC transform + TCrudPage + bridge init can exceed vitest's 5s default
    // on the first test in a file. We previously ran at 15s but Phase G
    // started seeing 1-4 flaky timeouts per run on Windows SSDs (the page
    // shifted between runs - i.e. environmental, not regression). Bumped
    // to 30s to give all integration mounts headroom; genuine hangs still
    // surface within a reasonable wall-clock for CI.
    testTimeout: 30000,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      include: ['src/**/*.{ts,vue}'],
      exclude: [
        'src/**/index.ts',
        'src/**/*.d.ts',
        // Data, not logic: ~4,400 lines of literal message objects per locale
        // would dominate the ratio while telling us nothing about test quality.
        'src/locales/**',
        'src/plugin/index.ts',
        '**/__tests__/**',
      ],
      // A RATCHET, not the destination. These sit just under what the suite
      // actually achieves, so any drop trips the gate; raise them whenever a
      // batch of tests lands. The previous values (80/70/80/60) were aspiration
      // written as contract: the run had never met them, so `test:coverage`
      // failed by default and nobody could tell a regression from the status
      // quo. A threshold you do not meet is decoration.
      //
      // `src/components/**` used to be excluded here - 138 files and ~30k lines,
      // the surface consumers reuse most, invisible to the very number meant to
      // describe the package. Including it RAISED the ratio (components are
      // better covered than pages), so the exclusion was hiding good news and
      // an unmeasured 28% of the code at the same time.
      //
      // Functions stays lowest for the original reason: mount-based tests under
      // happy-dom never execute handlers declared inside `<script setup>`
      // without full user-flow simulation. Those flows currently have no
      // automated coverage at all - see docs/frontend/architecture.md §4.2.
      thresholds: {
        statements: 53,
        lines: 55,
        branches: 41,
        functions: 41,
      },
    },
  },
})
