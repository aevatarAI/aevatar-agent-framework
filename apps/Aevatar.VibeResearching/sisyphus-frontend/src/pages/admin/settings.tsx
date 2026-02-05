import { AccountLayout } from "@/components/account/account-layout"
import { SettingsPanel } from "@/components/sisyphus/settings"

// ============================================================
//  Platform Settings Page (Accessible to all logged-in users)
// ============================================================

export default function SettingsPage() {
  return (
    <AccountLayout
      title="Platform Settings"
      subtitle="Configure platform-wide settings for the Sisyphus system."
    >
      <SettingsPanel sessionId={null} connected={false} />
    </AccountLayout>
  )
}
