import type { ApiId } from '@/types/api'

/** URL/持久化文本中的正十进制 long；安全整数还原为 number，超出范围时保留字符串。 */
export function parseQueryApiId(value: unknown): ApiId | undefined {
  if (typeof value !== 'string' || !/^\d+$/.test(value) || !/[1-9]/.test(value)) return undefined
  const number = Number(value)
  return Number.isSafeInteger(number) ? number : value
}
