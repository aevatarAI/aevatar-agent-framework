// ============================================================
//  Sisyphus Frontend - Entry Point with Routing
// ============================================================

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import './index.css'
import LandingPage from './pages/landing'
import App from './App'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        {/* Landing Page - Default Route */}
        <Route path="/" element={<LandingPage />} />
        
        {/* Research App - Main Application */}
        <Route path="/app" element={<App />} />
      </Routes>
    </BrowserRouter>
  </StrictMode>,
)
