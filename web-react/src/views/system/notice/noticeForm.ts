// 通知发布表单的**纯逻辑**:默认值、类型枚举→展示映射、接收范围条件必填校验。抽出便于 noticeForm.spec
// 变异钉死 —— 页面 index.tsx 只做接线(弹窗/表格/查看)。
import { NoticeType, ReceiverType, type NoticePublishInput } from '@/types/api'

/** 发布空表单:默认 通知类型 + 全体广播(receiverIds 空)。 */
export const blank = (): NoticePublishInput => ({
  title: '',
  content: '',
  type: NoticeType.Notice,
  receiverType: ReceiverType.All,
  receiverIds: [],
})

/** 类型 → i18n 键(页面再 t())。 */
export function typeLabelKey(type: NoticeType): string {
  return type === NoticeType.Announcement
    ? 'notice.typeAnnouncement'
    : type === NoticeType.Message
      ? 'notice.typeMessage'
      : 'notice.typeNotice'
}

/** 类型 → antd Tag 预设色(Vue 侧 info 无 antd 对应,用 processing 蓝)。 */
export function typeTagColor(type: NoticeType): string {
  return type === NoticeType.Announcement ? 'warning' : type === NoticeType.Message ? 'success' : 'processing'
}

/**
 * 接收对象条件必填:**全体广播(All)无需选**;定向(Role/User)必须选≥1。**会静默出错的核心**——
 * 校验写反会放行「定向但没选人」的空发,或挡住正常的全体广播。
 */
export function receiverValid(receiverType: ReceiverType, receiverIds?: number[]): boolean {
  return receiverType === ReceiverType.All || (receiverIds?.length ?? 0) > 0
}
