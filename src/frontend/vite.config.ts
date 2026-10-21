import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Where the Openspace API listens; override to run a second instance side by side.
const api = process.env.OPENSPACE_API ?? 'http://localhost:5145'

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
