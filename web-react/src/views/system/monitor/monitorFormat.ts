// 监控页的**纯逻辑**:字节/运行时长格式化、占用百分比与染色阈值。抽出便于 monitorFormat.spec 变异钉,
// 页面 index.tsx 只做接线(卡片 + 手动刷新)。运行时长的单位文案在页面 t() 拼(本层只出数值分解)。

/** 字节 → 人类可读(二进制单位 B..TB;0 及以下回 '0 B',超 TB 封顶 TB,非 B 档保留 2 位)。 */
export function fmtBytes(n: number): string {
  if (n <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(n) / Math.log(1024)), units.length - 1)
  return `${(n / 1024 ** i).toFixed(i === 0 ? 0 : 2)} ${units[i]}`
}

/** 运行秒数 → {天,时,分} 分解(页面按需拼单位文案)。 */
export function uptimeParts(s: number): { days: number; hours: number; minutes: number } {
  return {
    days: Math.floor(s / 86400),
    hours: Math.floor((s % 86400) / 3600),
    minutes: Math.floor((s % 3600) / 60),
  }
}

/** 占用百分比(整数 0–100;total≤0 时回 0,防除零得 Infinity/NaN)。 */
export function pct(used: number, total: number): number {
  return total > 0 ? Math.round((used / total) * 100) : 0
}

/** 占用染色:≥90 危险 / ≥75 警告 / 否则正常(返回主题令牌,明暗自适应)。 */
export function usageColor(p: number): string {
  return p >= 90 ? 'var(--color-danger)' : p >= 75 ? 'var(--color-warning)' : 'var(--color-success)'
}
