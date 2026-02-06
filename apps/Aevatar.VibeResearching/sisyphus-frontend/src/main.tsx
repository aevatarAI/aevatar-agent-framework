// ============================================================
//  Sisyphus Frontend - Entry Point with Routing
// ============================================================

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ToastProvider } from '@/components/ui/toast'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 30_000, retry: 1 },
  },
})
import './index.css'

// Pages
import LandingPage from './pages/landing'
import App from './App'

// Auth Pages
import {
  LoginPage,
  RegisterPage,
  ForgotPasswordPage,
  ResetPasswordPage,
  EmailConfirmationPage,
  AccountLockedPage,
  OAuthCallbackPage,
} from './pages/auth'

// Admin Pages
import { UsersPage, RolesPage, PermissionsPage, SettingsPage } from './pages/admin'

// Account Page
import AccountPage from './pages/account'

// Error Pages
import AccessDeniedPage from './pages/errors/access-denied'

// Route Guards
import { ProtectedRoute, AdminRoute } from './components/guards'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
    <ToastProvider>
    <BrowserRouter>
      <Routes>
        {/* Public Routes */}
        <Route path="/" element={<LandingPage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/email-confirmation" element={<EmailConfirmationPage />} />
        <Route path="/account-locked" element={<AccountLockedPage />} />
        
        {/* OAuth Callback Routes */}
        <Route path="/auth/callback/github" element={<OAuthCallbackPage />} />
        <Route path="/auth/callback/google" element={<OAuthCallbackPage />} />
        
        {/* App Route - Public for anonymous browsing */}
        <Route path="/app" element={<App />} />
        
        {/* Admin Routes */}
        <Route
          path="/admin/users"
          element={
            <AdminRoute>
              <UsersPage />
            </AdminRoute>
          }
        />
        <Route
          path="/admin/roles"
          element={
            <AdminRoute>
              <RolesPage />
            </AdminRoute>
          }
        />
        <Route
          path="/admin/permissions"
          element={
            <AdminRoute>
              <PermissionsPage />
            </AdminRoute>
          }
        />
        <Route
          path="/admin/settings"
          element={
            <ProtectedRoute>
              <SettingsPage />
            </ProtectedRoute>
          }
        />
        
        {/* Account Routes */}
        <Route
          path="/account/*"
          element={
            <ProtectedRoute>
              <AccountPage />
            </ProtectedRoute>
          }
        />
        
        {/* Error Routes */}
        <Route path="/403" element={<AccessDeniedPage />} />
      </Routes>
    </BrowserRouter>
    </ToastProvider>
    </QueryClientProvider>
  </StrictMode>,
)
