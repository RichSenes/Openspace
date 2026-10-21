/**
 * How long to wait before the next attempt to reach the server: 1s, 2s, 4s, …
 * capped at 10s, and never giving up. The API may be restarting or rebuilding.
 */
export function retryDelay(previousAttempts: number): number {
  return Math.min(10_000, 1000 * 2 ** Math.min(previousAttempts, 4))
}
