import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ToastProvider } from './ui/Toasts'
import './index.css'
import App from './App.tsx'
import { SpriteLab } from './lab/SpriteLab'
import { ShioriLab } from './lab/shiori/ShioriLab'
import { Presence } from './lab/presence/Presence'
import { PortraitLab } from './lab/presence/portrait/PortraitLab'
import { IsoLab } from './lab/iso/IsoLab'

console.info('Hello, friend.')

const lab = new URLSearchParams(location.search).get('lab')

const queryClient = new QueryClient({
  // Freshness comes from SignalR, not from refetching on focus.
  defaultOptions: { queries: { refetchOnWindowFocus: false, retry: 1 } },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        {lab === 'shiori' ? <ShioriLab /> : lab === 'presence' ? <Presence /> : lab === 'portrait' ? <PortraitLab /> : lab === 'iso' ? <IsoLab /> : lab !== null ? <SpriteLab /> : <App />}
      </ToastProvider>
    </QueryClientProvider>
  </StrictMode>,
)
