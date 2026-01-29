// ============================================================
//  Timeline View - Event Timeline with Virtual Scrolling
// ============================================================

import React, { useMemo, useState } from 'react'
import { Search, Filter, ChevronDown } from 'lucide-react'
import { cn } from '@/lib/utils'
import EventCard from './event-card'
import type { ClassifiedEvent, EventCategory } from '@/types'

interface TimelineViewProps {
  events: ClassifiedEvent[]
  sessionId: string  // Reserved for future event filtering
}

const CATEGORY_OPTIONS: { value: EventCategory; label: string; color: string }[] = [
  { value: 'consensus', label: 'Consensus', color: 'text-neon-green' },
  { value: 'proposal', label: 'Proposal', color: 'text-neon-cyan' },
  { value: 'vote', label: 'Vote', color: 'text-neon-gold' },
  { value: 'tool_call', label: 'Tool Call', color: 'text-neon-rose' },
  { value: 'red_flag', label: 'Red Flag', color: 'text-red-500' },
  { value: 'parallel', label: 'Parallel', color: 'text-neon-purple' },
  { value: 'llm', label: 'LLM', color: 'text-blue-400' },
]

const TimelineView: React.FC<TimelineViewProps> = ({ events, sessionId: _sessionId }) => {
  void _sessionId // Reserved for future event filtering
  const [search, setSearch] = useState('')
  const [selectedCategories, setSelectedCategories] = useState<EventCategory[]>([])
  const [showFilters, setShowFilters] = useState(false)

  // Filter events
  const filteredEvents = useMemo(() => {
    return events.filter(event => {
      // Category filter
      if (selectedCategories.length > 0 && !selectedCategories.includes(event.category)) {
        return false
      }
      // Search filter
      if (search) {
        const searchLower = search.toLowerCase()
        return (
          event.title.toLowerCase().includes(searchLower) ||
          event.message.toLowerCase().includes(searchLower)
        )
      }
      return true
    })
  }, [events, selectedCategories, search])

  // Group events by time (5-minute intervals)
  const groupedEvents = useMemo(() => {
    const groups: { time: string; events: ClassifiedEvent[] }[] = []
    let currentGroup: ClassifiedEvent[] = []
    let currentTime = ''

    filteredEvents.forEach(event => {
      const date = new Date(event.timestamp)
      const timeKey = `${date.getHours().toString().padStart(2, '0')}:${Math.floor(date.getMinutes() / 5) * 5}`
      
      if (timeKey !== currentTime) {
        if (currentGroup.length > 0) {
          groups.push({ time: currentTime, events: currentGroup })
        }
        currentGroup = [event]
        currentTime = timeKey
      } else {
        currentGroup.push(event)
      }
    })

    if (currentGroup.length > 0) {
      groups.push({ time: currentTime, events: currentGroup })
    }

    return groups
  }, [filteredEvents])

  const toggleCategory = (category: EventCategory) => {
    setSelectedCategories(prev =>
      prev.includes(category)
        ? prev.filter(c => c !== category)
        : [...prev, category]
    )
  }

  return (
    <div className="h-full flex flex-col">
      {/* Filter Toolbar */}
      <div className="flex-shrink-0 px-4 py-3 border-b border-border-subtle bg-bg-surface/50">
        <div className="flex items-center gap-3">
          {/* Search */}
          <div className="relative flex-1 max-w-xs">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-text-muted" />
            <input
              type="text"
              placeholder="Search events..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="w-full pl-9 pr-3 py-2 text-sm bg-bg-elevated border border-border-subtle rounded-lg
                text-text-primary placeholder-text-muted focus:outline-none focus:border-neon-cyan/50"
            />
          </div>

          {/* Filter Toggle */}
          <button
            onClick={() => setShowFilters(!showFilters)}
            className={cn(
              "flex items-center gap-2 px-3 py-2 text-sm rounded-lg border transition-colors",
              showFilters
                ? "bg-neon-cyan/10 border-neon-cyan/40 text-neon-cyan"
                : "bg-bg-elevated border-border-subtle text-text-muted hover:text-text-primary"
            )}
          >
            <Filter className="w-4 h-4" />
            <span>Filter</span>
            {selectedCategories.length > 0 && (
              <span className="px-1.5 py-0.5 text-[10px] bg-neon-cyan/20 text-neon-cyan rounded">
                {selectedCategories.length}
              </span>
            )}
            <ChevronDown className={cn("w-4 h-4 transition-transform", showFilters && "rotate-180")} />
          </button>

          {/* Event Count */}
          <div className="text-xs text-text-muted font-mono">
            {filteredEvents.length} events
          </div>
        </div>

        {/* Category Filters */}
        {showFilters && (
          <div className="flex flex-wrap gap-2 mt-3 pt-3 border-t border-border-subtle">
            {CATEGORY_OPTIONS.map(option => (
              <button
                key={option.value}
                onClick={() => toggleCategory(option.value)}
                className={cn(
                  "px-2.5 py-1 text-xs font-mono rounded-md border transition-colors",
                  selectedCategories.includes(option.value)
                    ? "bg-neon-cyan/10 border-neon-cyan/40 text-neon-cyan"
                    : "bg-bg-elevated border-border-subtle text-text-muted hover:text-text-primary"
                )}
              >
                <span className={option.color}>●</span> {option.label}
              </button>
            ))}
            {selectedCategories.length > 0 && (
              <button
                onClick={() => setSelectedCategories([])}
                className="px-2.5 py-1 text-xs font-mono text-text-muted hover:text-text-primary"
              >
                Clear all
              </button>
            )}
          </div>
        )}
      </div>

      {/* Event List */}
      <div className="flex-1 overflow-y-auto px-4 py-4">
        {filteredEvents.length === 0 ? (
          <EmptyState search={search} hasFilters={selectedCategories.length > 0} />
        ) : (
          <div className="space-y-6">
            {groupedEvents.map((group, groupIndex) => (
              <div key={groupIndex} className="relative">
                {/* Time Label */}
                <div className="sticky top-0 z-10 flex items-center gap-2 mb-3">
                  <div className="text-[10px] font-mono text-text-muted bg-bg-base px-2 py-0.5 rounded">
                    {group.time}
                  </div>
                  <div className="flex-1 h-px bg-border-subtle" />
                </div>

                {/* Events */}
                <div className="space-y-2 pl-4 border-l border-border-subtle">
                  {group.events.map(event => (
                    <EventCard key={event.id} event={event} />
                  ))}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

// Empty State Component
const EmptyState: React.FC<{ search: string; hasFilters: boolean }> = ({ search, hasFilters }) => (
  <div className="flex flex-col items-center justify-center h-full text-center">
    <div className="w-16 h-16 rounded-full bg-bg-elevated flex items-center justify-center mb-4">
      <Search className="w-8 h-8 text-text-muted" />
    </div>
    <h3 className="text-lg font-display text-text-primary mb-2">No events found</h3>
    <p className="text-sm text-text-muted max-w-xs">
      {search
        ? `No events match "${search}"`
        : hasFilters
          ? "No events match the selected filters"
          : "Events will appear here as the workflow executes"}
    </p>
  </div>
)

export default TimelineView
