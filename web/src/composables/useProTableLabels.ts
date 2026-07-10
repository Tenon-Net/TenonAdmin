import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ProTableLabels } from 'tenon-naive-pro-table'

/**
 * ProTable 的 labels 适配:包内不带 i18n 框架,文案由宿主注入(iconPicker 同款模式)。
 * computed 保证切换语言即时生效;查询/重置/密度复用既有 common/app 键,其余走 proTable.*。
 */
export function useProTableLabels(): ComputedRef<ProTableLabels> {
  const { t } = useI18n()
  return computed(() => ({
    search: t('common.search'),
    reset: t('common.reset'),
    refresh: t('proTable.refresh'),
    density: t('app.density'),
    densityComfortable: t('app.comfortable'),
    densityCompact: t('app.compact'),
    columnSettings: t('proTable.columnSettings'),
    columnSettingsReset: t('proTable.columnSettingsReset'),
    fixedLeft: t('proTable.fixedLeft'),
    fixedRight: t('proTable.fixedRight'),
    fixedNone: t('proTable.fixedNone'),
  }))
}
