import { useRoute } from 'vue-router'
import { useTabsStore } from '@/stores/tabs'

// 详情页动态标签标题:数据加载后调用返回的 setter,把当前标签标题改成记录名(如「张三」「工单 #123」)。
// 内部走 tabsStore.setTitle → 置 titleFixed,addTab 复访不再用静态 meta.title 覆盖它;标题随 tab 持久化,F5 无闪复原。
// 就地态(列表页内切换详情)不要调用它:那时 route.path 是列表标签,改名会改错标签。
export function useTabTitle() {
  const route = useRoute()
  const tabs = useTabsStore()
  return (title: string) => tabs.setTitle(route.path, title)
}
