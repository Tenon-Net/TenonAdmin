import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import type { useSiteStore as UseSiteStore } from './site'

vi.mock('@/api', () => ({ configApi: { siteInfo: vi.fn() } }))
import { configApi } from '@/api'

const siteInfoMock = vi.mocked(configApi.siteInfo)

/**
 * 在途缓存 `inflight` 是**模块级变量**,`setState` 够不着,所以每条用例都重新求值本模块。
 * (不为此在生产代码上开一个 `__resetInflight` 测试后门 —— `resetModules` 是本仓库其余 spec
 * 已经在用的同一个模式,见 `stores/app-media.spec.ts` 与 `api/client.spec.ts`。)
 */
let useSiteStore: typeof UseSiteStore

async function fresh() {
  vi.resetModules()
  ;({ useSiteStore } = await import('./site'))
}

const FULL = {
  title: '榫卯后台',
  subtitle: '副标题',
  copyright: '© 2026',
  copyrightUrl: 'https://example.com',
  logo: '/files/logo.png',
  captchaEnabled: true,
  smsLoginEnabled: true,
}

beforeEach(async () => {
  siteInfoMock.mockReset()
  await fresh()
})

afterEach(() => {
  vi.resetModules()
})

describe('默认值', () => {
  it('未加载时 title 是内置名,其余为空 —— 首帧品牌词不能空白', () => {
    const s = useSiteStore.getState().site
    expect(s.title).toBe('TenonAdmin')
    expect(s.subtitle).toBe('')
    expect(s.logo).toBe('')
    expect(s.captchaEnabled).toBe(false)
  })
})

describe('load', () => {
  it('成功后字段全部落地', async () => {
    siteInfoMock.mockResolvedValue(FULL)
    await useSiteStore.getState().load()
    expect(useSiteStore.getState().site).toEqual(FULL)
  })

  it('只拉一次:重复 load 不重打接口', async () => {
    siteInfoMock.mockResolvedValue(FULL)
    await useSiteStore.getState().load()
    await useSiteStore.getState().load()
    await useSiteStore.getState().load()
    expect(siteInfoMock).toHaveBeenCalledTimes(1)
  })

  it('并发 load 合流成一次请求', async () => {
    siteInfoMock.mockResolvedValue(FULL)
    await Promise.all([
      useSiteStore.getState().load(),
      useSiteStore.getState().load(),
      useSiteStore.getState().load(),
    ])
    expect(siteInfoMock).toHaveBeenCalledTimes(1)
  })

  it('force 强制重取(改配置保存后即时生效)', async () => {
    siteInfoMock.mockResolvedValueOnce(FULL).mockResolvedValueOnce({ ...FULL, title: '改过了' })
    await useSiteStore.getState().load()
    await useSiteStore.getState().load(true)
    expect(siteInfoMock).toHaveBeenCalledTimes(2)
    expect(useSiteStore.getState().site.title).toBe('改过了')
  })

  it('后端把 title 配成空串 → 回落内置名(其余字段空串原样留空)', async () => {
    siteInfoMock.mockResolvedValue({ ...FULL, title: '', subtitle: '' })
    await useSiteStore.getState().load()
    expect(useSiteStore.getState().site.title).toBe('TenonAdmin')
    expect(useSiteStore.getState().site.subtitle).toBe('') // 空串不回落,它就是"没配"
  })

  it('字段缺失(后端老版本没这几项)按类型兜底,不出现 undefined', async () => {
    siteInfoMock.mockResolvedValue({ title: '只有标题' } as never)
    await useSiteStore.getState().load()
    const s = useSiteStore.getState().site
    expect(s).toEqual({
      title: '只有标题',
      subtitle: '',
      copyright: '',
      copyrightUrl: '',
      logo: '',
      captchaEnabled: false,
      smsLoginEnabled: false,
    })
  })

  it('失败不抛给调用方,保留内置默认', async () => {
    siteInfoMock.mockRejectedValue(new Error('后端没起来'))
    await expect(useSiteStore.getState().load()).resolves.toBeUndefined()
    expect(useSiteStore.getState().site.title).toBe('TenonAdmin')
  })

  /**
   * **失败后不自动重试** —— 这是照搬 Vue 侧的**有意选择**,不是漏写,所以要有条用例记着它。
   * 与 `stores/dict.ts` 相反(那边失败清 pending → 下次访问自然重试)。
   * 哪天决定改语义,这条会红,提醒把两个模板一起改。
   */
  it('失败后再 load 不会自动重试(与 dict store 语义相反,已知且有意)', async () => {
    siteInfoMock.mockRejectedValue(new Error('后端没起来'))
    await useSiteStore.getState().load()
    await useSiteStore.getState().load()
    expect(siteInfoMock).toHaveBeenCalledTimes(1)

    // 只有 force 能把它救回来。
    siteInfoMock.mockResolvedValue(FULL)
    await useSiteStore.getState().load(true)
    expect(useSiteStore.getState().site.title).toBe('榫卯后台')
  })
})
