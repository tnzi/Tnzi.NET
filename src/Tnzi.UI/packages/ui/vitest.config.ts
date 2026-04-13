import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'path';

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: [
      // Subpath aliases MUST come before the general '@tnzi/core' alias
      // to prevent Vite prefix matching from swallowing subpath imports
      { find: '@tnzi/core/headless', replacement: resolve(__dirname, '../core/src/headless/index.ts') },
      { find: '@tnzi/core/types/shared-ui', replacement: resolve(__dirname, '../core/src/types/shared-ui.ts') },
      { find: '@tnzi/core/utils', replacement: resolve(__dirname, '../core/src/utils/index.ts') },
      { find: '@tnzi/core/adapters/storage', replacement: resolve(__dirname, '../core/src/adapters/storage.ts') },
      { find: '@tnzi/core/adapters/i18n', replacement: resolve(__dirname, '../core/src/adapters/i18n/index.ts') },
      { find: '@tnzi/core/adapters/theme', replacement: resolve(__dirname, '../core/src/adapters/theme/index.ts') },
      { find: '@tnzi/core/adapters', replacement: resolve(__dirname, '../core/src/adapters/index.ts') },
      { find: '@tnzi/core/state', replacement: resolve(__dirname, '../core/src/state/index.ts') },
      { find: '@tnzi/core/types', replacement: resolve(__dirname, '../core/src/types/index.ts') },
      { find: '@tnzi/core/http/http', replacement: resolve(__dirname, '../core/src/http/http.ts') },
      { find: '@tnzi/core/state', replacement: resolve(__dirname, '../core/src/state/index.ts') },
      { find: '@tnzi/core/services/identity', replacement: resolve(__dirname, '../core/src/services/identity/index.ts') },
      // General alias last — only matches exact '@tnzi/core' import
      { find: '@tnzi/core', replacement: resolve(__dirname, '../core/src/index.ts') },
    ],
  },
  test: {
    globals: false,
    environment: 'happy-dom',
  },
});
