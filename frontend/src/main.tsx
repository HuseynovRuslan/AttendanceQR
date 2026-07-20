import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import { AuthProvider } from './auth/AuthContext'
import { BrandingProvider } from './branding/BrandingContext'
import { initDevice } from './lib/device'
import { registerServiceWorker } from './lib/registerSW'
import './index.css'
import './theme.css'

// Request persistent storage + self-heal the device id from its IndexedDB mirror BEFORE the app
// renders (so the first scan uses the recovered id, not a fresh one). Best-effort, never blocks.
void initDevice()

// Register the app-shell service worker so the PWA opens with no connection (prerequisite for offline
// check-in). Production only; never blocks render.
registerServiceWorker()

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <BrowserRouter>
      <BrandingProvider>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrandingProvider>
    </BrowserRouter>
  </React.StrictMode>,
)
