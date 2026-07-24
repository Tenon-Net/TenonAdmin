import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, useRoutes } from 'react-router-dom'
import { buildDetailRoutes, detailMetaForPath, type DetailViewGlob } from './detailRoutes'

const glob: DetailViewGlob = {
  '/src/views/system/user/detail.tsx': async () => ({ default: () => <div>USER DETAIL</div> }),
  '/src/views/system/log/op/detail.tsx': async () => ({ default: () => <div>LOG DETAIL</div> }),
}

function Routed() {
  return useRoutes(buildDetailRoutes(glob))
}

describe('约定式详情路由', () => {
  it('detail.tsx 映射为带 id 参数的懒加载路由和稳定名称', () => {
    const routes = buildDetailRoutes(glob)
    expect(routes.map((route) => ({ path: route.path, name: (route.handle as { name: string }).name }))).toEqual([
      { path: '/system/log/op/:id/detail', name: 'detail-system-log-op' },
      { path: '/system/user/:id/detail', name: 'detail-system-user' },
    ])
  })

  it('深链路径匹配详情页面', async () => {
    render(<MemoryRouter initialEntries={['/system/user/42/detail']}><Routed /></MemoryRouter>)
    expect(await screen.findByText('USER DETAIL')).toBeTruthy()
  })

  it('详情标签以实际 pathname 区分记录，默认不缓存且支持动态标题覆盖', () => {
    expect(detailMetaForPath('/system/user/42/detail', glob)).toEqual({
      title: 'common.detail',
      noCache: true,
    })
    expect(detailMetaForPath('/system/user/99/detail', glob)).toEqual({
      title: 'common.detail',
      noCache: true,
    })
    expect(detailMetaForPath('/system/user/detail', glob)).toBeUndefined()
  })
})
