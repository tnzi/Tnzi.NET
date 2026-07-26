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
  },
})
