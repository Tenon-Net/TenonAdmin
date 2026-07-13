// 表格批量删除的样板收敛:维护勾选态 + 二次确认(挂起对话框防连点)+ 成功后清选并刷新。
// 用在 ProTable(经 $attrs 把 checked-row-keys 透传到内层 n-data-table)与裸 n-data-table 两处。
// 仅限 setup 中调用(内部用 useConfirm → useI18n/useDialog)。
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useConfirm } from '@/composables/useConfirm'

export interface UseBatchDeleteOptions {
  /** 批量删除接口(收 number[] 主键)。 */
  remove: (ids: number[]) => Promise<unknown>
  /** 删除成功后刷新数据。 */
  refresh: () => void
  /** 成功 toast 文案;false = 不弹(另有提示)。缺省用 common.success。 */
  successMsg?: string | false
  /**
   * 自定义确认文案(可异步:删前要先查点东西才能把话说清楚时用,如"这些角色关联了 N 个用户")。
   * 缺省是按条数的通用文案。有此需求的页面若只给行内单删加警告,勾选+批量删就能绕过去。
   */
  content?: (ids: number[]) => string | Promise<string>
}

export function useBatchDelete(opts: UseBatchDeleteOptions) {
  const { t } = useI18n()
  const { confirm } = useConfirm()
  /** 勾选的行主键(绑到表格的 checked-row-keys)。 */
  const checkedKeys = ref<Array<string | number>>([])
  const hasSelection = computed(() => checkedKeys.value.length > 0)

  async function run() {
    const ids = checkedKeys.value.map(Number)
    if (ids.length === 0) return
    const ok = await confirm({
      content: (await opts.content?.(ids)) ?? t('common.batchDeleteConfirm', { count: ids.length }),
      action: () => opts.remove(ids),
      successMsg: opts.successMsg,
    })
    if (ok) {
      checkedKeys.value = []
      opts.refresh()
    }
  }

  return { checkedKeys, hasSelection, run }
}
