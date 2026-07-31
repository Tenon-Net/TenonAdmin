import { createHmac } from 'node:crypto'

/** RFC 6238 TOTP (SHA1, 30s, 6 digits) — mirrors backend TotpService. */
export function computeTotp(seedBase32: string, nowMs = Date.now()): string {
  const key = fromBase32(seedBase32)
  const counter = Math.floor(nowMs / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeBigUInt64BE(BigInt(counter))
  const hash = createHmac('sha1', key).update(buf).digest()
  const offset = hash[hash.length - 1]! & 0x0f
  const binary =
    ((hash[offset]! & 0x7f) << 24)
    | ((hash[offset + 1]! & 0xff) << 16)
    | ((hash[offset + 2]! & 0xff) << 8)
    | (hash[offset + 3]! & 0xff)
  return String(binary % 1_000_000).padStart(6, '0')
}

function fromBase32(encoded: string): Buffer {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  const normalized = encoded.trim().replace(/\s+/g, '').replace(/=+$/, '').toUpperCase()
  let buffer = 0
  let bits = 0
  const out: number[] = []
  for (const c of normalized) {
    const val = alphabet.indexOf(c)
    if (val < 0) throw new Error(`invalid base32: ${c}`)
    buffer = (buffer << 5) | val
    bits += 5
    if (bits >= 8) {
      bits -= 8
      out.push((buffer >> bits) & 0xff)
    }
  }
  return Buffer.from(out)
}
