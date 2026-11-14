import { defineConfig } from 'vite';

// The client imports the engine (../shared, ../sim, ../bot, ...) by relative .ts
// paths, so the dev server must be allowed to read the parent directory.
export default defineConfig({
  base: './',
  server: { fs: { allow: ['..'] } },
  build: { target: 'es2022', outDir: 'dist', emptyOutDir: true },
});
