import { AdminLayout } from "@/components/admin"
import { SettingsPanel } from "@/components/sisyphus/settings"

// ============================================================
//  Platform Settings Admin Page
// ============================================================

export default function SettingsPage() {
  return (
    <AdminLayout
      title="Platform Settings"
      subtitle="Configure platform-wide settings for the Sisyphus system."
    >
      <SettingsPanel sessionId={null} connected={false} />
    </AdminLayout>
  )
}
