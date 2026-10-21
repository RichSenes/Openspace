import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const api = 'http://localhost:5145'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': api,
      '/hubs': { target: api, ws: true },
    },
  },
  test: {
    include: ['src/**/*.test.ts'],
  },
})
