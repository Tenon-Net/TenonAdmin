import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { ExportColumnsModal } from './ExportColumnsModal'
import type { ExportColumnDef } from '@/types/api'

const cols: ExportColumnDef[] = [
  { key: 'Account', title: '登录账号' },
  { key: 'Name', title: '姓名' },
  { key: 'IsSuperAdmin', title: '超级管理员', defaultSelected: false },
]

function mount(props: Partial<ComponentProps<typeof ExportColumnsModal>> = {}) {
  const onConfirm = props.onConfirm ?? vi.fn()
  const onOpenChange = props.onOpenChange ?? vi.fn()
  render(
    <AntdApp>
      <ExportColumnsModal
        open
        onOpenChange={onOpenChange}
        columns={cols}
        onConfirm={onConfirm}
        {...props}
      />
    </AntdApp>,
  )
  return { onConfirm, onOpenChange }
}

afterEach(cleanup)

describe('ExportColumnsModal', () => {
  it('打开时按 DefaultSelected 预选(缺省 true;显式 false 不选)', () => {
    mount()
    // 全选复选框 + 三列 = 4 个 checkbox
    const boxes = screen.getAllByRole('checkbox')
    // Account / Name 勾选,IsSuperAdmin 不勾;全选为 indeterminate
    expect((boxes[1] as HTMLInputElement).checked).toBe(true) // Account
    expect((boxes[2] as HTMLInputElement).checked).toBe(true) // Name
    expect((boxes[3] as HTMLInputElement).checked).toBe(false) // IsSuperAdmin
  })

  it('确认按档案声明顺序回传勾选 Key,不含未勾选', () => {
    const { onConfirm } = mount()
    // antd 两字中文按钮会插空格成「导 出」(autoInsertSpaceInButton)
    fireEvent.click(screen.getByText((txt) => txt.replace(/\s/g, '') === '导出'))
    expect(onConfirm).toHaveBeenCalledWith(['Account', 'Name'])
  })

  it('零勾选时确认钮禁用', () => {
    mount()
    // indeterminate 点全选会先变成全选;再点一次才清空
    const selectAll = screen.getAllByRole('checkbox')[0]
    fireEvent.click(selectAll)
    fireEvent.click(selectAll)
    const ok = screen.getByText((txt) => txt.replace(/\s/g, '') === '导出').closest('button') as HTMLButtonElement
    expect(ok.disabled).toBe(true)
  })
})
