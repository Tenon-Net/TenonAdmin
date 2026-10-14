import { describe, expect, it } from 'vitest'
import { isKnownPagePath } from './knownPage'

describe('isKnownPagePath', () => {
  it('treats the designer and other real pages as known, and typos as unknown', () => {
    expect(isKnownPagePath('/workflow/definition/designer')).toBe(true)
    expect(isKnownPagePath('/workflow/definition')).toBe(true)
    expect(isKnownPagePath('/workflow/definition/')).toBe(true)
    expect(isKnownPagePath('/no-such-page')).toBe(false)
    expect(isKnownPagePath('/workflow/definition/components/WfNodeCard')).toBe(false)
  })
})
