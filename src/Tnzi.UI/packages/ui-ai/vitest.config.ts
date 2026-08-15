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
        'src/locales/**',
        'src/embed/**',
        'src/theme/**',
        'src/styles/**',
        // Re-export-only entry for the `@tnzi/ui-ai/chat` subpath.
        'src/chat-app.ts',
        // The SFCs under components/** need a real DOM plus user interaction to
        // test meaningfully; unit tests target the headless layer and utils instead,
        // and the SFCs are exercised in a consumer chat app.
        //
        // Scoped to `.vue` on purpose: pure logic co-located with a component
        // (parsers, grouping, formatting) is exactly what this comment says unit
        // tests should cover, so excluding the whole directory would hide it.
        'src/components/**/*.vue',
        '**/__tests__/**',
      ],
      thresholds: {
        lines: 80,
        statements: 80,
        // Same rationale as ui-admin: mount-based unit tests can't reach 80%
        // function coverage for arrow-function handlers inside script setup.
        // Real flows are only exercised by hand in a consumer chat app.
        functions: 60,
        branches: 70,
      },
    },
  },
});
