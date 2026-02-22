// vite.config.ts
import { defineConfig } from "file:///C:/src/Tnzi.NET/src/Tnzi.UI/node_modules/.pnpm/vite@5.4.21_@types+node@22.19.11_terser@5.46.0/node_modules/vite/dist/node/index.js";
import vue from "file:///C:/src/Tnzi.NET/src/Tnzi.UI/node_modules/.pnpm/@vitejs+plugin-vue@5.2.4_vi_25fc415354d6fd9d09d813f7e47a1274/node_modules/@vitejs/plugin-vue/dist/index.mjs";
import dts from "file:///C:/src/Tnzi.NET/src/Tnzi.UI/node_modules/.pnpm/vite-plugin-dts@3.9.1_@type_9ab5e86227574ece845ec3eb1e54c440/node_modules/vite-plugin-dts/dist/index.mjs";
var vite_config_default = defineConfig({
  plugins: [
    vue(),
    dts({
      include: ["src/**/*"],
      outDir: "dist"
    })
  ],
  build: {
    lib: {
      entry: "src/index.ts",
      name: "TnziUi",
      formats: ["es", "umd"],
      fileName: (format) => format === "es" ? "index.js" : "index.umd.cjs"
    },
    rollupOptions: {
      external: ["vue"],
      output: {
        exports: "named",
        globals: {
          vue: "Vue"
        }
      }
    }
  }
});
export {
  vite_config_default as default
};
//# sourceMappingURL=data:application/json;base64,ewogICJ2ZXJzaW9uIjogMywKICAic291cmNlcyI6IFsidml0ZS5jb25maWcudHMiXSwKICAic291cmNlc0NvbnRlbnQiOiBbImNvbnN0IF9fdml0ZV9pbmplY3RlZF9vcmlnaW5hbF9kaXJuYW1lID0gXCJFOlxcXFxNeVxcXFxUdWFuWmkuTkVUXFxcXHNyY1xcXFxUdWFuWmkuVUlcXFxccGFja2FnZXNcXFxcd2ViXCI7Y29uc3QgX192aXRlX2luamVjdGVkX29yaWdpbmFsX2ZpbGVuYW1lID0gXCJFOlxcXFxNeVxcXFxUdWFuWmkuTkVUXFxcXHNyY1xcXFxUdWFuWmkuVUlcXFxccGFja2FnZXNcXFxcd2ViXFxcXHZpdGUuY29uZmlnLnRzXCI7Y29uc3QgX192aXRlX2luamVjdGVkX29yaWdpbmFsX2ltcG9ydF9tZXRhX3VybCA9IFwiZmlsZTovLy9FOi9NeS9UdWFuWmkuTkVUL3NyYy9UdWFuWmkuVUkvcGFja2FnZXMvd2ViL3ZpdGUuY29uZmlnLnRzXCI7aW1wb3J0IHsgZGVmaW5lQ29uZmlnIH0gZnJvbSAndml0ZSc7XG5pbXBvcnQgdnVlIGZyb20gJ0B2aXRlanMvcGx1Z2luLXZ1ZSc7XG5pbXBvcnQgZHRzIGZyb20gJ3ZpdGUtcGx1Z2luLWR0cyc7XG5cbmV4cG9ydCBkZWZhdWx0IGRlZmluZUNvbmZpZyh7XG4gIHBsdWdpbnM6IFtcbiAgICB2dWUoKSxcbiAgICBkdHMoe1xuICAgICAgaW5jbHVkZTogWydzcmMvKiovKiddLFxuICAgICAgb3V0RGlyOiAnZGlzdCcsXG4gICAgfSksXG4gIF0sXG4gIGJ1aWxkOiB7XG4gICAgbGliOiB7XG4gICAgICBlbnRyeTogJ3NyYy9pbmRleC50cycsXG4gICAgICBuYW1lOiAnVHVhblppVWknLFxuICAgICAgZm9ybWF0czogWydlcycsICd1bWQnXSxcbiAgICAgIGZpbGVOYW1lOiAoZm9ybWF0KSA9PiAoZm9ybWF0ID09PSAnZXMnID8gJ2luZGV4LmpzJyA6ICdpbmRleC51bWQuY2pzJyksXG4gICAgfSxcbiAgICByb2xsdXBPcHRpb25zOiB7XG4gICAgICBleHRlcm5hbDogWyd2dWUnXSxcbiAgICAgIG91dHB1dDoge1xuICAgICAgICBleHBvcnRzOiAnbmFtZWQnLFxuICAgICAgICBnbG9iYWxzOiB7XG4gICAgICAgICAgdnVlOiAnVnVlJyxcbiAgICAgICAgfSxcbiAgICAgIH0sXG4gICAgfSxcbiAgfSxcbn0pO1xuIl0sCiAgIm1hcHBpbmdzIjogIjtBQUFpVSxTQUFTLG9CQUFvQjtBQUM5VixPQUFPLFNBQVM7QUFDaEIsT0FBTyxTQUFTO0FBRWhCLElBQU8sc0JBQVEsYUFBYTtBQUFBLEVBQzFCLFNBQVM7QUFBQSxJQUNQLElBQUk7QUFBQSxJQUNKLElBQUk7QUFBQSxNQUNGLFNBQVMsQ0FBQyxVQUFVO0FBQUEsTUFDcEIsUUFBUTtBQUFBLElBQ1YsQ0FBQztBQUFBLEVBQ0g7QUFBQSxFQUNBLE9BQU87QUFBQSxJQUNMLEtBQUs7QUFBQSxNQUNILE9BQU87QUFBQSxNQUNQLE1BQU07QUFBQSxNQUNOLFNBQVMsQ0FBQyxNQUFNLEtBQUs7QUFBQSxNQUNyQixVQUFVLENBQUMsV0FBWSxXQUFXLE9BQU8sYUFBYTtBQUFBLElBQ3hEO0FBQUEsSUFDQSxlQUFlO0FBQUEsTUFDYixVQUFVLENBQUMsS0FBSztBQUFBLE1BQ2hCLFFBQVE7QUFBQSxRQUNOLFNBQVM7QUFBQSxRQUNULFNBQVM7QUFBQSxVQUNQLEtBQUs7QUFBQSxRQUNQO0FBQUEsTUFDRjtBQUFBLElBQ0Y7QUFBQSxFQUNGO0FBQ0YsQ0FBQzsiLAogICJuYW1lcyI6IFtdCn0K
