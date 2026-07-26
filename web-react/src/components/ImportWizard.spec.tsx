import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { ImportWizard, type ImportWizardApi } from './ImportWizard'
import type { ImportPreview, ImportRow } from '@/types/api'

function emptyPreview(over: Partial<ImportPreview> = {}): ImportPreview {
  return {
    headers: ['登录账号', '姓名'],
    mapping: { 登录账号: 'Account', 姓名: 'Name' },
    columns: [
      { key: 'Account', title: '登录账号', required: true },
      { key: 'Name', title: '姓名', required: true },
    ],
    rows: [
      {
        index: 1,
        cells: { Account: 'a1', Name: '' },
        errors: [{ columnKey: 'Name', code: 46005 }],
      },
    ],
    total: 1,
    errorRows: 1,
    columnErrors: [],
    ...over,
  }
}

function makeApi(over: Partial<ImportWizardApi> = {}): ImportWizardApi {
  return {
    downloadTemplate: vi.fn().mockResolvedValue(new Blob(['x'])),
    preview: vi.fn().mockResolvedValue(emptyPreview()),
    validate: vi.fn().mockResolvedValue(emptyPreview({ errorRows: 0, rows: [{ index: 1, cells: { Account: 'a1', Name: 'n' }, errors: [] }] })),
    commit: vi.fn().mockResolvedValue({ total: 1, inserted: 1, updated: 0, skipped: 0, failed: 0, failures: [] }),
    errorReport: vi.fn().mockResolvedValue(new Blob(['e'])),
    ...over,
  }
}

function mount(api: ImportWizardApi = makeApi()) {
  render(
    <AntdApp>
      <ImportWizard open onOpenChange={vi.fn()} api={api} />
    </AntdApp>,
  )
  return api
}

afterEach(cleanup)

describe('ImportWizard', () => {
  it('上传一步:无文件时下一步禁用', () => {
    mount()
    const next = screen.getByRole('button', { name: '下一步' }) as HTMLButtonElement
    expect(next.disabled).toBe(true)
  })

  it('重新校验必须把当前 rows 交给 api.validate(变异判据)', async () => {
    const api = makeApi()
    // 直接把状态推进到预览步:先选文件再点两次下一步
    mount(api)
    const file = new File(['xlsx'], 't.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    })
    // Upload.Dragger 的 input[type=file]
    const input = document.querySelector('input[type="file"]') as HTMLInputElement
    expect(input).toBeTruthy()
    fireEvent.change(input, { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    await waitFor(() => expect(api.preview).toHaveBeenCalled())
    // 列映射步 → 预览
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    await waitFor(() => expect(screen.getByRole('button', { name: '重新校验' })).toBeTruthy())

    fireEvent.click(screen.getByRole('button', { name: '重新校验' }))
    await waitFor(() => expect(api.validate).toHaveBeenCalled())
    const rowsArg = (api.validate as ReturnType<typeof vi.fn>).mock.calls[0][0] as ImportRow[]
    expect(Array.isArray(rowsArg)).toBe(true)
    expect(rowsArg.length).toBeGreaterThan(0)
    expect(rowsArg[0].cells).toBeDefined()
  })

  it('错误格带 import-cell--error 类与 --color-danger-bg 内联底色(坑 12)', async () => {
    const api = makeApi()
    mount(api)
    const file = new File(['xlsx'], 't.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    })
    const input = document.querySelector('input[type="file"]') as HTMLInputElement
    fireEvent.change(input, { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    await waitFor(() => expect(api.preview).toHaveBeenCalled())
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    await waitFor(() => {
      const errCell = document.querySelector('.import-cell--error') as HTMLElement | null
      expect(errCell).toBeTruthy()
      expect(errCell!.style.background).toContain('var(--color-danger-bg)')
    })
  })
})
