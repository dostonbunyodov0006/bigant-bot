import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  // Dashboard backend bilan BIR XIL manzildan tarqatiladi —
  // shuning uchun to'g'ridan-to'g'ri wwwroot ga quriladi.
  build: { outDir: '../src/Bigant.Api/wwwroot', emptyOutDir: true },
  plugins: [react()],
  server: {
    // host: true — IPv4 (127.0.0.1) va IPv6 (::1) ga ham bog'lanadi.
    // Faqat IPv6 da tursa Windows brauzeri localhost ni topa olmasligi mumkin.
    // Bonus: telefondan ham ochish mumkin (Network manzili ko'rsatiladi).
    host: true,
    // 5173-5176 — Sales Platform frontendi band qilgan.
    port: 5177,
    strictPort: true,
    proxy: {
      '/api': { target: 'http://127.0.0.1:5080', changeOrigin: true },
    },
  },
})
