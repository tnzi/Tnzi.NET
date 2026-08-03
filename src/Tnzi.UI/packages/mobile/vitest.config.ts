import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  // SFCs are mounted in the component tests, so the vue plugin is required.
  // `@tnzi/core` deliberately resolves through the workspace link (its built
  // dist) rather than an alias to source, so these tests exercise the same
  // entry points consumers get.
  plugins: [vue()],
  test: {
    globals: true,
    environment: 'happy-dom',
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      include: ['src/**/*.{ts,vue}'],
      exclude: [
        'src/**/index.ts',
        'src/**/*.d.ts',
        'src/plugin.ts',
        // `@tnzi/core` resolves to its built dist here (see the note above), so
        // without this its compiled output lands in this package's denominator.
        'dist/**',
        '**/__tests__/**',
      ],
      // A RATCHET, not a target. Set just under measured (64.68 stmts / 54.00
      // branch / 52.02 funcs / 65.78 lines on 2026-08-02, with the exclude list
      // above in place - barrels score high, so excluding them lowers the figure
      // from 67.69/57.77/49.28/70.52 and makes it mean something).
      //
      // Functions trails the others for the reason it does in ui-admin and
      // ui-ai: mount-based tests under happy-dom do not execute handlers
      // declared inside `<script setup>` without full user-flow simulation, and
      // this package has no browser-level suite.
      thresholds: {
        lines: 63,
        statements: 62,
        functions: 50,
        branches: 52,
      },
    },
  },
})
