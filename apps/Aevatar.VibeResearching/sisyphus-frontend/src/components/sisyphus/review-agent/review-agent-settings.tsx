import React, { useState, useEffect } from 'react';
import { Save, RefreshCw, AlertCircle, CheckCircle, Loader2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import { getReviewAgentSettings, updateReviewAgentSettings } from '@/lib/axiom-client';
import type { ReviewAgentSettings, ReviewAgentSettingsUpdate } from '@/types/review-agent';

interface ValidationErrors {
  iterationIntervalMinutes?: string;
  outOfDateThresholdMinutes?: string;
  toDeleteThresholdMinutes?: string;
  perNodeTimeoutSeconds?: string;
}

const ReviewAgentSettingsPanel: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [settings, setSettings] = useState<ReviewAgentSettings | null>(null);
  const [formData, setFormData] = useState<ReviewAgentSettingsUpdate>({});
  const [validationErrors, setValidationErrors] = useState<ValidationErrors>({});

  useEffect(() => {
    fetchSettings();
  }, []);

  const fetchSettings = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await getReviewAgentSettings();
      setSettings(data);
      setFormData({
        iterationIntervalMinutes: data.iterationIntervalMinutes,
        outOfDateThresholdMinutes: data.outOfDateThresholdMinutes,
        toDeleteThresholdMinutes: data.toDeleteThresholdMinutes,
        llmProviderName: data.llmProviderName,
        perNodeTimeoutSeconds: data.perNodeTimeoutSeconds,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  const validateForm = (): boolean => {
    const errors: ValidationErrors = {};

    if (formData.iterationIntervalMinutes !== undefined && formData.iterationIntervalMinutes < 1) {
      errors.iterationIntervalMinutes = 'Must be at least 1 minute';
    }

    if (formData.outOfDateThresholdMinutes !== undefined && formData.outOfDateThresholdMinutes < 1) {
      errors.outOfDateThresholdMinutes = 'Must be at least 1 minute';
    }

    if (formData.toDeleteThresholdMinutes !== undefined && formData.toDeleteThresholdMinutes < 60) {
      errors.toDeleteThresholdMinutes = 'Must be at least 60 minutes (1 hour)';
    }

    if (formData.perNodeTimeoutSeconds !== undefined && formData.perNodeTimeoutSeconds < 10) {
      errors.perNodeTimeoutSeconds = 'Must be at least 10 seconds';
    }

    setValidationErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSave = async () => {
    if (!validateForm()) return;

    setSaving(true);
    setError(null);
    setSuccess(false);

    try {
      const updated = await updateReviewAgentSettings(formData);
      setSettings(updated);
      setSuccess(true);
      setTimeout(() => setSuccess(false), 3000);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setSaving(false);
    }
  };

  const handleChange = (field: keyof ReviewAgentSettingsUpdate, value: string | number) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
    setValidationErrors((prev) => ({ ...prev, [field]: undefined }));
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-8">
        <Loader2 className="w-6 h-6 text-neon-cyan animate-spin" />
      </div>
    );
  }

  if (error && !settings) {
    return (
      <div className="p-4 rounded-lg bg-neon-red/10 border border-neon-red/30">
        <p className="text-neon-red text-sm">{error}</p>
        <button
          onClick={fetchSettings}
          className="mt-2 text-sm text-neon-cyan hover:underline"
        >
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Success Message */}
      {success && (
        <div className="p-3 rounded-lg bg-neon-green/10 border border-neon-green/30 flex items-center gap-2">
          <CheckCircle className="w-4 h-4 text-neon-green" />
          <p className="text-neon-green text-sm">Settings saved successfully!</p>
        </div>
      )}

      {/* Error Message */}
      {error && (
        <div className="p-3 rounded-lg bg-neon-red/10 border border-neon-red/30 flex items-center gap-2">
          <AlertCircle className="w-4 h-4 text-neon-red" />
          <p className="text-neon-red text-sm">{error}</p>
        </div>
      )}

      {/* Settings Form */}
      <div className="space-y-4">
        {/* Iteration Interval */}
        <div>
          <label className="block text-text-primary text-sm font-medium mb-1.5">
            Review Interval (minutes)
          </label>
          <p className="text-text-dimmed text-xs mb-2">
            How often the Review Agent runs. Minimum: 1 minute.
          </p>
          <input
            type="number"
            min={1}
            value={formData.iterationIntervalMinutes ?? ''}
            onChange={(e) => handleChange('iterationIntervalMinutes', parseInt(e.target.value) || 0)}
            className={cn(
              "w-full px-3 py-2 rounded-lg bg-surface-elevated border text-text-primary text-sm",
              "focus:outline-none focus:ring-2 focus:ring-neon-cyan/50",
              validationErrors.iterationIntervalMinutes ? "border-neon-red" : "border-border-subtle"
            )}
          />
          {validationErrors.iterationIntervalMinutes && (
            <p className="text-neon-red text-xs mt-1">{validationErrors.iterationIntervalMinutes}</p>
          )}
        </div>

        {/* Out of Date Threshold */}
        <div>
          <label className="block text-text-primary text-sm font-medium mb-1.5">
            Stale Threshold (minutes)
          </label>
          <p className="text-text-dimmed text-xs mb-2">
            Nodes not reviewed for this duration are marked stale. Minimum: 1 minute.
          </p>
          <input
            type="number"
            min={1}
            value={formData.outOfDateThresholdMinutes ?? ''}
            onChange={(e) => handleChange('outOfDateThresholdMinutes', parseInt(e.target.value) || 0)}
            className={cn(
              "w-full px-3 py-2 rounded-lg bg-surface-elevated border text-text-primary text-sm",
              "focus:outline-none focus:ring-2 focus:ring-neon-cyan/50",
              validationErrors.outOfDateThresholdMinutes ? "border-neon-red" : "border-border-subtle"
            )}
          />
          {validationErrors.outOfDateThresholdMinutes && (
            <p className="text-neon-red text-xs mt-1">{validationErrors.outOfDateThresholdMinutes}</p>
          )}
        </div>

        {/* To Delete Threshold */}
        <div>
          <label className="block text-text-primary text-sm font-medium mb-1.5">
            Delete Threshold (minutes)
          </label>
          <p className="text-text-dimmed text-xs mb-2">
            Deactivated nodes are deleted after this duration. Minimum: 60 minutes.
          </p>
          <input
            type="number"
            min={60}
            value={formData.toDeleteThresholdMinutes ?? ''}
            onChange={(e) => handleChange('toDeleteThresholdMinutes', parseInt(e.target.value) || 0)}
            className={cn(
              "w-full px-3 py-2 rounded-lg bg-surface-elevated border text-text-primary text-sm",
              "focus:outline-none focus:ring-2 focus:ring-neon-cyan/50",
              validationErrors.toDeleteThresholdMinutes ? "border-neon-red" : "border-border-subtle"
            )}
          />
          {validationErrors.toDeleteThresholdMinutes && (
            <p className="text-neon-red text-xs mt-1">{validationErrors.toDeleteThresholdMinutes}</p>
          )}
        </div>

        {/* Per Node Timeout */}
        <div>
          <label className="block text-text-primary text-sm font-medium mb-1.5">
            Per-Node Timeout (seconds)
          </label>
          <p className="text-text-dimmed text-xs mb-2">
            Maximum time for verifying a single node. Minimum: 10 seconds.
          </p>
          <input
            type="number"
            min={10}
            value={formData.perNodeTimeoutSeconds ?? ''}
            onChange={(e) => handleChange('perNodeTimeoutSeconds', parseInt(e.target.value) || 0)}
            className={cn(
              "w-full px-3 py-2 rounded-lg bg-surface-elevated border text-text-primary text-sm",
              "focus:outline-none focus:ring-2 focus:ring-neon-cyan/50",
              validationErrors.perNodeTimeoutSeconds ? "border-neon-red" : "border-border-subtle"
            )}
          />
          {validationErrors.perNodeTimeoutSeconds && (
            <p className="text-neon-red text-xs mt-1">{validationErrors.perNodeTimeoutSeconds}</p>
          )}
        </div>

        {/* LLM Provider */}
        <div>
          <label className="block text-text-primary text-sm font-medium mb-1.5">
            LLM Provider
          </label>
          <p className="text-text-dimmed text-xs mb-2">
            The LLM provider to use for verification (e.g., deepseek, claude, openai).
          </p>
          <input
            type="text"
            value={formData.llmProviderName ?? ''}
            onChange={(e) => handleChange('llmProviderName', e.target.value)}
            className={cn(
              "w-full px-3 py-2 rounded-lg bg-surface-elevated border text-text-primary text-sm",
              "focus:outline-none focus:ring-2 focus:ring-neon-cyan/50",
              "border-border-subtle"
            )}
            placeholder="deepseek"
          />
        </div>
      </div>

      {/* Actions */}
      <div className="flex items-center justify-end gap-3 pt-4 border-t border-border-subtle">
        <button
          onClick={fetchSettings}
          disabled={loading || saving}
          className="flex items-center gap-2 px-4 py-2 rounded-lg bg-surface-elevated hover:opacity-80 border border-border-subtle transition-colors text-sm disabled:opacity-50"
        >
          <RefreshCw className={cn("w-4 h-4", loading && "animate-spin")} />
          Reset
        </button>
        <button
          onClick={handleSave}
          disabled={saving}
          className="flex items-center gap-2 px-4 py-2 rounded-lg bg-neon-cyan/20 hover:bg-neon-cyan/30 border border-neon-cyan/50 text-neon-cyan transition-colors text-sm disabled:opacity-50"
        >
          {saving ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <Save className="w-4 h-4" />
          )}
          Save Settings
        </button>
      </div>
    </div>
  );
};

export default ReviewAgentSettingsPanel;
