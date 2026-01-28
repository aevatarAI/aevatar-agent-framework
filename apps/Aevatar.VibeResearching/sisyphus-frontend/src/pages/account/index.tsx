import { Routes, Route, Navigate } from "react-router-dom"
import {
  AccountLayout,
  ProfilePanel,
  PasswordPanel,
} from "@/components/account"

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
    </Routes>
  )
}
