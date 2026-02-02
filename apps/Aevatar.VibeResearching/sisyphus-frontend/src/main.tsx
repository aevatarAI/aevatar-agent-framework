// ============================================================
//  Sisyphus Frontend - Entry Point with Routing
// ============================================================

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import './index.css'
import LandingPage from './pages/landing'
import App from './App'

const rootElement = document.getElementById('root')
// #region agent log
fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'',runId:'',hypothesisId:'H90',location:'main.tsx:init',message:'root_check',data:{rootExists:!!rootElement,path:window.location.pathname,hash:window.location.hash,search:window.location.search},timestamp:Date.now()})}).catch(()=>{});
// #endregion

window.addEventListener('error', (event) => {
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'',runId:'',hypothesisId:'H91',location:'main.tsx:window_error',message:'window_error',data:{message:event.message || '',filename:event.filename || '',lineno:event.lineno || 0,colno:event.colno || 0},timestamp:Date.now()})}).catch(()=>{});
  // #endregion
})

window.addEventListener('unhandledrejection', (event) => {
  const reason = event.reason instanceof Error ? event.reason.message : String(event.reason ?? '')
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'',runId:'',hypothesisId:'H91',location:'main.tsx:unhandledrejection',message:'unhandled_rejection',data:{reason},timestamp:Date.now()})}).catch(()=>{});
  // #endregion
})

createRoot(rootElement!).render(
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

const logPostRender = () => {
  const root = document.getElementById('root')
  const bodyStyle = window.getComputedStyle(document.body)
  const rootStyle = root ? window.getComputedStyle(root) : null
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'',runId:'',hypothesisId:'H92',location:'main.tsx:post_render',message:'root_rendered',data:{rootExists:!!root,rootChildren:root?.childElementCount || 0,bodyBg:bodyStyle.backgroundColor,bodyColor:bodyStyle.color,rootDisplay:rootStyle?.display || '',rootVisibility:rootStyle?.visibility || '',rootOpacity:rootStyle?.opacity || ''},timestamp:Date.now()})}).catch(()=>{});
  // #endregion
}

if (typeof queueMicrotask === 'function') {
  queueMicrotask(logPostRender)
} else {
  Promise.resolve().then(logPostRender)
}
