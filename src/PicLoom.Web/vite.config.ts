import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5278',
        changeOrigin: false,
      },
    },
  },
  build: {
    outDir: '../PicLoom.Api/wwwroot',
    emptyOutDir: true,
  },
  test: {
    // The stubbed component suite was removed together with the offline provider. Real behaviour is
    // now verified by tests/PicLoom.LiveTests against a live relay; this runner stays configured so
    // a future test file is picked up automatically.
    environment: 'jsdom',
    globals: true,
    // `npm test` relies on `--passWithNoTests`, so an empty tests/ directory is not an error.
    include: ['src/**/*.test.{ts,tsx}', 'tests/**/*.test.{ts,tsx}'],
    css: false,
  },
});
