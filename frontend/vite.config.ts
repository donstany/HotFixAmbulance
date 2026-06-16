import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // HotFixAmbulance.Api default HTTP port (see backend/src/HotFixAmbulance.Api/Properties/launchSettings.json)
      '/api': 'http://localhost:5283',
    },
  },
});
