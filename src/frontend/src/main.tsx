import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ToastProvider } from './ui/Toasts'
import './index.css'
import App from './App.tsx'
import { SpriteLab } from './lab/SpriteLab'

console.info('Hello, friend.')

const queryClient = new QueryClient({
  // Freshness comes from SignalR, not from refetching on focus.
  defaultOptions: { queries: { refetchOnWindowFocus: false, retry: 1 } },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        {new URLSearchParams(location.search).has('lab') ? <SpriteLab /> : <App />}
      </ToastProvider>
    </QueryClientProvider>
  </StrictMode>,
)
