// 表格批量删除的样板收敛(React 版,对应 Vue 侧 composables/useBatchDelete.ts):
// 维护勾选态 + 二次确认(useConfirm 的挂起守卫防连点)+ 成功后清选并刷新。
// selectedKeys/setSelectedKeys 绑到 <DataTable> 的受控 rowSelection。仅限组件内调用
// (内部用 useConfirm → App.useApp / useTranslation)。
import { useState, type Key } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirm } from './useConfirm'

export interface UseBatchDeleteOptions {
  /** 批量删除接口(收 number[] 主键)。 */
  remove: (ids: number[]) => Promise<unknown>
  /** 删除成功后刷新数据。 */
  refresh: () => void
  /** 成功 toast 文案;false = 不弹(另有提示)。缺省用 useConfirm 的 common.success。 */
  successMsg?: string | false
  /**
   * 自定义确认文案(可异步:删前要先查点东西才能把话说清,如"这些角色关联了 N 个用户")。
   * 缺省是按条数的通用文案。有此需求的页面若只给行内单删加警告,勾选+批量删就能绕过去。
   */
  content?: (ids: number[]) => string | Promise<string>
}

export function useBatchDelete(opts: UseBatchDeleteOptions) {
  const { t } = useTranslation()
  const { confirm } = useConfirm()
  /** 勾选的行主键(绑到 DataTable 的 rowSelection.selectedRowKeys)。 */
  const [selectedKeys, setSelectedKeys] = useState<Key[]>([])
  const hasSelection = selectedKeys.length > 0

  // 有意不 memo:run 每次渲染重建以闭合最新 selectedKeys(对应 Vue 侧读 checkedKeys.value 的"永远新鲜")。
  // 批删按钮低频,重建无成本;若 memo 则要把 selectedKeys 进依赖数组,反成 stale-closure footgun。
  async function run() {
    const ids = selectedKeys.map(Number)
    if (ids.length === 0) return // 空选不弹框
    const ok = await confirm({
      content: (await opts.content?.(ids)) ?? t('common.batchDeleteConfirm', { count: ids.length }),
      action: () => opts.remove(ids),
      successMsg: opts.successMsg,
    })
    if (ok) {
      // 仅成功才清选 + 刷新:失败保留勾选让用户重试(与 Vue 侧一致)。
      setSelectedKeys([])
      opts.refresh()
    }
  }

  return { selectedKeys, setSelectedKeys, hasSelection, run }
}
