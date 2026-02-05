// ============================================================================
//  Settings Panel - Main Component
//  Full-page settings in main content area
//  Integrates: Tools & MCP, LLM Providers, Agent Config
// ============================================================================

import React, { useState } from 'react'
import { cn } from '@/lib/utils'
import { useSisyphusStore } from '@/store/sisyphus-store'
import { ToolsMcpTab } from './tools-mcp-tab'
import { ProvidersTab } from './providers-tab'
import { AgentsTab } from './agents-tab'
import { AdvancedTab } from './advanced-tab'
import { UserProvidersTab } from '@/components/user-providers'
import { AgentProviderPanel } from '@/components/user-providers'
import type { SettingsPanelProps, TabKey } from './types'

const SettingsPanel: React.FC<SettingsPanelProps> = ({ sessionId, connected }) => {
  const { tools, setTools, apiInfo, agentRoster } = useSisyphusStore()

  const [activeTab, setActiveTab] = useState<TabKey>('tools')

  return (
    <div className="h-full flex flex-col overflow-hidden">
      {/* Header */}
      <header className="flex-shrink-0 px-6 py-4 border-b border-border-subtle bg-surface/50">
        <h1 className="text-xl font-display font-bold text-neon-cyan tracking-wider text-balance">
          Settings
        </h1>
        <p className="text-sm text-text-muted mt-1 text-pretty">
          Manage tools, MCP connections, LLM providers, and API keys
        </p>
      </header>

      {/* Tabs */}
      <nav className="flex-shrink-0 px-6 pt-4 flex gap-1 border-b border-border-subtle">
        {[
          { key: 'tools' as TabKey, label: 'Tools & MCP', icon: '⚙️' },
          { key: 'providers' as TabKey, label: 'Platform Providers', icon: '🔑' },
          { key: 'my-providers' as TabKey, label: 'My Providers', icon: '🔐' },
          { key: 'agents' as TabKey, label: 'Agents', icon: '🤖' },
          { key: 'advanced' as TabKey, label: 'Advanced', icon: '🛠️' },
        ].map((tab) => (
          <button
            key={tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={cn(
              'px-4 py-2.5 text-sm font-mono rounded-t-lg transition-all duration-200',
              activeTab === tab.key
                ? 'bg-surface border border-b-0 border-border-subtle text-neon-cyan'
                : 'text-text-muted hover:text-text-primary hover:bg-surface-elevated'
            )}
          >
            <span className="mr-2">{tab.icon}</span>
            {tab.label}
          </button>
        ))}
      </nav>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-6">
        {activeTab === 'tools' && (
          <ToolsMcpTab
            sessionId={sessionId}
            connected={connected}
            tools={tools}
            setTools={setTools}
          />
        )}
        {activeTab === 'providers' && <ProvidersTab defaultProvider={apiInfo?.llm?.default} />}
        {activeTab === 'my-providers' && <UserProvidersTab />}
        {activeTab === 'agents' && (
          <>
            <AgentProviderPanel
              sessionId={sessionId}
              connected={connected}
              agentRoster={agentRoster}
            />
            <div className="mt-6 pt-6 border-t border-border-subtle">
              <h4 className="text-xs font-mono text-text-dimmed uppercase tracking-wider mb-4">
                Legacy Agent Provider Config
              </h4>
              <AgentsTab
                sessionId={sessionId}
                connected={connected}
                agentRoster={agentRoster}
                availableProviders={apiInfo?.llm?.providers || []}
              />
            </div>
          </>
        )}
        {activeTab === 'advanced' && <AdvancedTab />}
      </div>
    </div>
  )
}

export default SettingsPanel
