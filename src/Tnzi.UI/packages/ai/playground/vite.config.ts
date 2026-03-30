import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'path';

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@tnzi/ai/locale': resolve(__dirname, '../src/locale'),
      '@tnzi/ai/themes': resolve(__dirname, '../src/themes'),
      '@tnzi/ai/components': resolve(__dirname, '../src/components'),
      '@tnzi/ai/composables': resolve(__dirname, '../src/composables'),
      '@tnzi/ai/chat': resolve(__dirname, '../src/chat'),
      '@tnzi/ai/admin': resolve(__dirname, '../src/admin'),
      '@tnzi/ai/embed': resolve(__dirname, '../src/embed'),
      '@tnzi/ai': resolve(__dirname, '../src'),
      '@': resolve(__dirname, '../src'),
    },
  },
  server: {
    port: 4200,
    strictPort: false,
  },
});
