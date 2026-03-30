import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './faro' // initialize Faro before anything else
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
