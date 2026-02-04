// ============================================================================
//  Unified Logger - Production-safe logging utility
//  Replaces scattered console.* calls with controllable log levels
// ============================================================================

type LogLevel = 'debug' | 'info' | 'warn' | 'error' | 'silent'

interface LoggerConfig {
  level: LogLevel
  prefix: string
  enabled: boolean
}

const LOG_LEVELS: Record<LogLevel, number> = {
  debug: 0,
  info: 1,
  warn: 2,
  error: 3,
  silent: 4,
}

// Default: production = warn, development = debug
const DEFAULT_LEVEL: LogLevel = import.meta.env.PROD ? 'warn' : 'debug'

class Logger {
  private config: LoggerConfig

  constructor(prefix: string, level?: LogLevel) {
    this.config = {
      level: level ?? DEFAULT_LEVEL,
      prefix,
      enabled: true,
    }
  }

  private shouldLog(level: LogLevel): boolean {
    if (!this.config.enabled) return false
    return LOG_LEVELS[level] >= LOG_LEVELS[this.config.level]
  }

  private format(level: string, ...args: unknown[]): unknown[] {
    const timestamp = new Date().toISOString().slice(11, 23)
    return [`[${timestamp}] [${this.config.prefix}] [${level.toUpperCase()}]`, ...args]
  }

  debug(...args: unknown[]): void {
    if (this.shouldLog('debug')) {
      console.debug(...this.format('debug', ...args))
    }
  }

  info(...args: unknown[]): void {
    if (this.shouldLog('info')) {
      console.info(...this.format('info', ...args))
    }
  }

  log(...args: unknown[]): void {
    if (this.shouldLog('info')) {
      console.log(...this.format('info', ...args))
    }
  }

  warn(...args: unknown[]): void {
    if (this.shouldLog('warn')) {
      console.warn(...this.format('warn', ...args))
    }
  }

  error(...args: unknown[]): void {
    if (this.shouldLog('error')) {
      console.error(...this.format('error', ...args))
    }
  }

  setLevel(level: LogLevel): void {
    this.config.level = level
  }

  setEnabled(enabled: boolean): void {
    this.config.enabled = enabled
  }

  child(subPrefix: string): Logger {
    return new Logger(`${this.config.prefix}:${subPrefix}`, this.config.level)
  }
}

// ============================================================================
//  Pre-configured loggers for different modules
// ============================================================================

export const logger = new Logger('app')
export const apiLogger = new Logger('api')
export const streamLogger = new Logger('stream')
export const reviewLogger = new Logger('review')
export const storeLogger = new Logger('store')

// Factory for custom loggers
export function createLogger(prefix: string, level?: LogLevel): Logger {
  return new Logger(prefix, level)
}

// Global log level control
export function setGlobalLogLevel(level: LogLevel): void {
  logger.setLevel(level)
  apiLogger.setLevel(level)
  streamLogger.setLevel(level)
  reviewLogger.setLevel(level)
  storeLogger.setLevel(level)
}

// Disable all logging (useful for tests)
export function disableAllLogs(): void {
  setGlobalLogLevel('silent')
}

export type { LogLevel, Logger }
