import { describe, expect, it } from 'vitest'
import { takeRecoveryCodes } from './bindComplete'

describe('takeRecoveryCodes', () => {
  it('rejects absent recoveryCodes', () => {
    expect(takeRecoveryCodes({})).toBeNull()
    expect(takeRecoveryCodes({ recoveryCodes: undefined })).toBeNull()
    expect(takeRecoveryCodes({ recoveryCodes: null })).toBeNull()
  })

  it('rejects an empty array', () => {
    expect(takeRecoveryCodes({ recoveryCodes: [] })).toBeNull()
  })

  it('rejects whitespace-only entries so the recovery screen never renders blank codes', () => {
    expect(takeRecoveryCodes({ recoveryCodes: ['', '  ', '\t'] })).toBeNull()
  })

  it('returns trimmed non-empty codes for a successful complete response', () => {
    expect(takeRecoveryCodes({ recoveryCodes: ['  abcd-1111  ', 'efgh-2222'] })).toEqual([
      'abcd-1111',
      'efgh-2222',
    ])
  })
})
