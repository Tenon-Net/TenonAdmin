export interface SerialPollerOptions<T> {
  intervalMs: number
  fetch: () => Promise<T>
  apply: (value: T) => void
  onError: (error: unknown, silent: boolean) => void
}

/** 串行轮询；停用会让当前请求失效，重新启用时等待它结束后立即拉取新快照。 */
export function createSerialPoller<T>(options: SerialPollerOptions<T>) {
  let active = false
  let generation = 0
  let inFlight = false
  let queuedSilent: boolean | null = null
  let timer: number | null = null

  function clearTimer() {
    if (timer == null) return
    window.clearTimeout(timer)
    timer = null
  }

  function schedule() {
    clearTimer()
    timer = window.setTimeout(() => void refresh(true), options.intervalMs)
  }

  async function refresh(silent = true): Promise<void> {
    if (!active) return
    if (inFlight) {
      queuedSilent = queuedSilent == null ? silent : queuedSilent && silent
      return
    }

    clearTimer()
    inFlight = true
    const requestGeneration = generation
    try {
      const value = await options.fetch()
      if (active && requestGeneration === generation) options.apply(value)
    } catch (error) {
      if (active && requestGeneration === generation) options.onError(error, silent)
    } finally {
      inFlight = false
      if (active) {
        if (requestGeneration !== generation || queuedSilent != null) {
          const nextSilent = queuedSilent ?? true
          queuedSilent = null
          void refresh(nextSilent)
        } else {
          schedule()
        }
      }
    }
  }

  function start(silent = false) {
    if (active) return
    active = true
    ++generation
    void refresh(silent)
  }

  function stop() {
    if (!active) return
    active = false
    ++generation
    queuedSilent = null
    clearTimer()
  }

  return { start, stop, refresh }
}
