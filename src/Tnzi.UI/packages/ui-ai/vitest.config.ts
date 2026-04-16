import { defineConfig } from 'vitest/config';
import { resolve } from 'path';

export default defineConfig({
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
    },
  },
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
        'src/locale/**',
        'src/chat/**',
        'src/embed/**',
        'src/themes/**',
        'src/styles/**',
        // All 63 Vue business components under components/** are Naive UI /
        // Vue SFCs that require real DOM + user interaction to meaningfully
        // test. Covered by Playwright chat-themes spec in Task 6.6 — unit
        // tests focus on composables + lib.
        'src/components/**',
        'src/primitives/**',
        'playground/**',
        '**/__tests__/**',
      ],
      thresholds: {
        lines: 80,
        statements: 80,
        // Same rationale as ui-admin: mount-based unit tests can't reach 80%
        // function coverage for arrow-function handlers inside script setup.
        // Real flows are covered by Playwright E2E (Task 6.6 chat-themes).
        functions: 60,
        branches: 70,
      },
    },
  },
});
