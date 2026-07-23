import { describe, it, expect } from 'vitest'
import { logoColors } from './TenonLogo'

describe('logoColors', () => {
  it('暗色', () => {
    expect(logoColors(true)).toEqual({ bg: '#16181D', mark: '#7A81FF', markOpacity: 0.45 })
  })
  it('亮色', () => {
    expect(logoColors(false)).toEqual({ bg: '#646CFF', mark: '#FFFFFF', markOpacity: 0.5 })
  })
})
