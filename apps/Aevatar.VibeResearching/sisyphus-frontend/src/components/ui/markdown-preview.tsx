import { useState, useRef, useCallback } from 'react'
import ReactMarkdown, { type Components } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import rehypeRaw from 'rehype-raw'
import { Copy, Download, Check } from 'lucide-react'
import { cn } from '@/lib/utils'

// ============================================================
//  Markdown Preview Component with Copy/Download
//  Used for displaying node explanations and research summaries
// ============================================================

interface MarkdownPreviewProps {
  content: string
  title?: string
  /** Custom filename for download (without extension). If not provided, uses sanitized title. */
  downloadFilename?: string
  className?: string
  maxHeight?: string
}

export function MarkdownPreview({
  content,
  title = 'content',
  downloadFilename,
  className,
  maxHeight = 'max-h-[60vh]'
}: MarkdownPreviewProps) {
  const [copied, setCopied] = useState(false)
  const contentRef = useRef<HTMLDivElement>(null)

  // Handle internal anchor link clicks - scroll within the container
  const handleAnchorClick = useCallback((e: React.MouseEvent<HTMLAnchorElement>, href: string) => {
    if (href.startsWith('#')) {
      e.preventDefault()
      const targetId = href.slice(1) // Remove the '#'
      const container = contentRef.current
      if (!container) return

      // Find the target element by id
      const targetElement = container.querySelector(`#${CSS.escape(targetId)}`) ||
                           container.querySelector(`[id="${targetId}"]`) ||
                           container.querySelector(`a[id="${targetId}"]`)

      if (targetElement) {
        // Scroll the target into view within the container
        targetElement.scrollIntoView({ behavior: 'smooth', block: 'start' })
        // Add a brief highlight effect
        targetElement.classList.add('anchor-highlight')
        setTimeout(() => targetElement.classList.remove('anchor-highlight'), 2000)
      }
    }
  }, [])

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(content)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch (err) {
      console.error('Failed to copy:', err)
    }
  }

  const handleDownload = () => {
    const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    // Use custom filename if provided, otherwise sanitize title
    const filename = downloadFilename || title.replace(/[^a-z0-9]/gi, '_').toLowerCase()
    a.download = filename.endsWith('.md') ? filename : `${filename}.md`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  if (!content) {
    return (
      <div className="text-xs text-text-muted text-center py-4">
        No content available
      </div>
    )
  }

  return (
    <div className={cn(
      "rounded-lg border border-border-subtle bg-bg-elevated/50 overflow-hidden",
      className
    )}>
      {/* Toolbar */}
      <div className="flex items-center justify-end gap-1 px-3 py-2 border-b border-border-subtle bg-surface-elevated/50">
        <button
          onClick={handleCopy}
          className={cn(
            "flex items-center gap-1.5 px-2 py-1 text-[10px] font-mono rounded",
            "border border-border-subtle text-text-muted",
            "hover:text-neon-cyan hover:border-neon-cyan/40 transition-colors",
            copied && "text-neon-green border-neon-green/40"
          )}
          title="Copy to clipboard"
        >
          {copied ? <Check className="size-3" /> : <Copy className="size-3" />}
          {copied ? 'Copied!' : 'Copy'}
        </button>
        <button
          onClick={handleDownload}
          className={cn(
            "flex items-center gap-1.5 px-2 py-1 text-[10px] font-mono rounded",
            "border border-border-subtle text-text-muted",
            "hover:text-neon-gold hover:border-neon-gold/40 transition-colors"
          )}
          title="Download as Markdown"
        >
          <Download className="size-3" />
          Download
        </button>
      </div>

      {/* Markdown Content */}
      <div
        ref={contentRef}
        className={cn(
          "p-4 overflow-auto scroll-smooth",
          maxHeight
        )}
      >
        <div className="prose prose-sm prose-invert max-w-none leading-relaxed
          prose-headings:text-neon-cyan prose-headings:font-display prose-headings:text-sm
          prose-h1:text-base prose-h2:text-sm prose-h3:text-xs
          prose-a:text-neon-cyan prose-a:no-underline hover:prose-a:underline
          prose-code:text-neon-gold prose-code:bg-bg-elevated prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded prose-code:text-[10px]
          prose-pre:bg-bg-void prose-pre:border prose-pre:border-border-subtle prose-pre:text-[10px]
          prose-strong:text-text-primary
          prose-p:text-text-secondary prose-p:text-[11px]
          prose-li:text-text-secondary prose-li:text-[11px]
          prose-table:text-[10px]
          prose-th:text-text-primary prose-th:bg-surface-elevated prose-th:px-2 prose-th:py-1
          prose-td:text-text-secondary prose-td:px-2 prose-td:py-1
        ">
          <ReactMarkdown
            remarkPlugins={[remarkGfm]}
            rehypePlugins={[rehypeRaw]}
            components={{
              a: ({ href, children, ...props }) => {
                const hrefStr = href || ''
                // Handle internal anchor links
                if (hrefStr.startsWith('#')) {
                  return (
                    <a
                      {...props}
                      href={hrefStr}
                      onClick={(e) => handleAnchorClick(e, hrefStr)}
                      className="text-neon-cyan hover:underline cursor-pointer"
                    >
                      {children}
                    </a>
                  )
                }
                // External links open in new tab
                return (
                  <a
                    {...props}
                    href={hrefStr}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-neon-cyan hover:underline"
                  >
                    {children}
                  </a>
                )
              },
            } as Components}
          >
            {content}
          </ReactMarkdown>
        </div>
      </div>
    </div>
  )
}

export default MarkdownPreview
