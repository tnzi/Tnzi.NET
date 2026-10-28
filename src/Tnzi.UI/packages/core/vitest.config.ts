import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: true,
    environmentOptions: {
      happyDOM: {
        settings: {
          // The app-update tests seed `document` with the `<script type="module">`
          // entries a built page carries, only for the code under test to read
          // their `src`. Script evaluation is off in happy-dom, and from 21 it
          // reports every such module as a console DOMException ("JavaScript
          // file loading is disabled") instead of skipping it. Treat the skipped
          // load as a success so the output carries only what the tests say.
          handleDisabledFileLoadingAsSuccess: true,
        },
      },
    },
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      include: ['src/**/*.ts'],
      exclude: [
        // Barrels and type-only modules: re-exports and declarations, no logic.
        'src/**/index.ts',
        'src/**/*.d.ts',
        'src/types/**',
        // Enum and constant modules are declarations too: object literals with
        // no branches or functions. They score 0 until some test happens to
        // import them, which measures test file layout, not coverage of logic.
        'src/enums/**',
        'src/constants/**',
        // Generated from the OpenAPI spec by `tnzi generate`; the generator is
        // what would need testing, not its output.
        '**/__tests__/**',
      ],
      // A RATCHET, not a target. Set just under measured (52.32 stmts / 52.31
      // branch / 31.84 funcs / 51.73 lines on 2026-08-02) so a real drop trips
      // it while ordinary churn does not.
      //
      // Measure with the exclude list above in place: without it the same suite
      // reads 57.49/58.60/33.99/56.91, because barrels and type modules score
      // high and were inflating the ratio. Excluding them lowers the number and
      // makes it mean something.
      //
      // Functions sits lowest because `src/services/**` is ~18 API factories of
      // thin one-line methods: each is a counted function, and the suite covers
      // the contracts that carry logic (streaming, capabilities, file-url
      // resolution) rather than every passthrough. Raising this number means
      // writing tests for passthroughs, which is not obviously worth it - the
      // floor exists to catch regressions, not to argue for that work.
      thresholds: {
        lines: 49,
        statements: 50,
        functions: 30,
        branches: 50,
      },
    },
  },
});
