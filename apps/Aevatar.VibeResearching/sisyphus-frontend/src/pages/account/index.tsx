import { Routes, Route, Navigate } from "react-router-dom"
import {
  AccountLayout,
  ProfilePanel,
  PasswordPanel,
} from "@/components/account"
import { UserProvidersTab } from "@/components/user-providers"

// ============================================================
//  Account Page - Routes to Account Panels (Basic Version)
// ============================================================

export default function AccountPage() {
  return (
    <Routes>
      {/* Default redirect to profile */}
      <Route path="/" element={<Navigate to="/account/profile" replace />} />

      {/* Profile */}
      <Route
        path="/profile"
        element={
          <AccountLayout title="Profile" subtitle="Manage your personal information">
            <ProfilePanel />
          </AccountLayout>
        }
      />

      {/* Password */}
      <Route
        path="/password"
        element={
          <AccountLayout title="Change Password" subtitle="Update your account password">
            <PasswordPanel />
          </AccountLayout>
        }
      />

      {/* My LLM Providers */}
      <Route
        path="/providers"
        element={
          <AccountLayout title="My LLM Providers" subtitle="Configure your own AI model providers and API keys">
            <UserProvidersTab />
          </AccountLayout>
        }
      />
    </Routes>
  )
}
