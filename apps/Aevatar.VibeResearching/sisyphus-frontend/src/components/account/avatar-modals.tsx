import React, { useState, useRef } from "react"
import { X, CloudUpload, ZoomIn, ZoomOut, RotateCcw } from "lucide-react"
import { Dialog, DialogContent } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"

// ============================================================
//  Avatar Upload Modal
// ============================================================

interface AvatarUploadModalProps {
  open: boolean
  onClose: () => void
  onUpload: (file: File) => void
}

export const AvatarUploadModal: React.FC<AvatarUploadModalProps> = ({
  open,
  onClose,
  onUpload,
}) => {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [isDragging, setIsDragging] = useState(false)

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(true)
  }

  const handleDragLeave = () => {
    setIsDragging(false)
  }

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    const files = e.dataTransfer.files
    if (files.length > 0) {
      onUpload(files[0])
    }
  }

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files
    if (files && files.length > 0) {
      onUpload(files[0])
    }
  }

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[480px]">
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-border-subtle">
          <h2 className="text-lg font-semibold text-text-primary">Upload Avatar</h2>
          <button onClick={onClose} className="text-text-muted hover:text-text-primary">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="p-5">
          <div
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            onClick={() => fileInputRef.current?.click()}
            className={`
              border-2 border-dashed rounded-xl p-10 text-center cursor-pointer
              transition-all duration-200
              ${isDragging
                ? "border-neon-cyan bg-neon-cyan/10"
                : "border-border-default hover:border-border-strong hover:bg-surface-elevated"
              }
            `}
          >
            <div className="mx-auto w-16 h-16 rounded-full bg-neon-cyan/10 border border-neon-cyan/30 flex items-center justify-center mb-4">
              <CloudUpload className="w-8 h-8 text-neon-cyan" />
            </div>
            <p className="text-text-primary font-medium mb-1">
              Drag and drop your image here
            </p>
            <p className="text-sm text-text-muted mb-4">
              or click to browse files
            </p>
            <p className="text-xs text-text-dimmed">
              Supports: JPG, PNG, WebP (max 2MB)
            </p>
          </div>

          <input
            ref={fileInputRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            onChange={handleFileSelect}
            className="hidden"
          />
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 p-5 border-t border-border-subtle">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Avatar Crop Modal
// ============================================================

interface AvatarCropModalProps {
  open: boolean
  onClose: () => void
  onSave: (croppedImage: Blob) => void | Promise<void>
  imageUrl: string
}

export const AvatarCropModal: React.FC<AvatarCropModalProps> = ({
  open,
  onClose,
  onSave,
  imageUrl,
}) => {
  const [zoom, setZoom] = useState(1)
  const [isLoading, setIsLoading] = useState(false)

  const handleZoomIn = () => {
    setZoom(prev => Math.min(prev + 0.1, 2))
  }

  const handleZoomOut = () => {
    setZoom(prev => Math.max(prev - 0.1, 0.5))
  }

  const handleReset = () => {
    setZoom(1)
  }

  const handleSave = async () => {
    setIsLoading(true)
    try {
      // Convert image URL to blob
      const response = await fetch(imageUrl)
      const blob = await response.blob()
      // Call onSave (may be async for API upload)
      await onSave(blob)
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[520px]">
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-border-subtle">
          <h2 className="text-lg font-semibold text-text-primary">Crop Avatar</h2>
          <button onClick={onClose} className="text-text-muted hover:text-text-primary">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="p-5 space-y-4">
          {/* Crop Area */}
          <div className="relative w-64 h-64 mx-auto rounded-full overflow-hidden border-4 border-neon-cyan/30 bg-surface-elevated">
            <img
              src={imageUrl}
              alt="Crop preview"
              className="absolute inset-0 w-full h-full object-cover"
              style={{ transform: `scale(${zoom})` }}
            />
            {/* Crop Overlay */}
            <div className="absolute inset-0 border-4 border-dashed border-neon-cyan/50 rounded-full pointer-events-none" />
          </div>

          {/* Zoom Controls */}
          <div className="flex items-center justify-center gap-4">
            <button
              onClick={handleZoomOut}
              className="p-2 rounded-md bg-surface-elevated hover:bg-surface text-text-muted hover:text-text-primary transition-colors"
            >
              <ZoomOut className="w-5 h-5" />
            </button>
            
            <input
              type="range"
              min="0.5"
              max="2"
              step="0.1"
              value={zoom}
              onChange={(e) => setZoom(parseFloat(e.target.value))}
              className="w-32 accent-neon-cyan"
            />
            
            <button
              onClick={handleZoomIn}
              className="p-2 rounded-md bg-surface-elevated hover:bg-surface text-text-muted hover:text-text-primary transition-colors"
            >
              <ZoomIn className="w-5 h-5" />
            </button>
            
            <button
              onClick={handleReset}
              className="p-2 rounded-md bg-surface-elevated hover:bg-surface text-text-muted hover:text-text-primary transition-colors"
            >
              <RotateCcw className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 p-5 border-t border-border-subtle">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={handleSave} disabled={isLoading}>
            {isLoading ? "Saving..." : "Save Avatar"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}
