import { describe, it, expect } from 'vitest'
import { blank, typeLabelKey, typeTagColor, receiverValid } from './noticeForm'
import { NoticeType, ReceiverType } from '@/types/api'

describe('blank', () => {
  it('默认 通知类型 + 全体广播 + 空接收列表', () => {
    expect(blank()).toEqual({ title: '', content: '', type: NoticeType.Notice, receiverType: ReceiverType.All, receiverIds: [] })
  })
})

describe('typeLabelKey 三类各自映射', () => {
  it('Notice/Announcement/Message → 各自 i18n 键', () => {
    expect(typeLabelKey(NoticeType.Notice)).toBe('notice.typeNotice')
    expect(typeLabelKey(NoticeType.Announcement)).toBe('notice.typeAnnouncement')
    expect(typeLabelKey(NoticeType.Message)).toBe('notice.typeMessage')
  })
})

describe('typeTagColor 三类各自映射', () => {
  it('Announcement→warning / Message→success / Notice→processing', () => {
    expect(typeTagColor(NoticeType.Announcement)).toBe('warning')
    expect(typeTagColor(NoticeType.Message)).toBe('success')
    expect(typeTagColor(NoticeType.Notice)).toBe('processing')
  })
})

describe('receiverValid 条件必填', () => {
  it('All 广播无需选接收对象', () => {
    expect(receiverValid(ReceiverType.All, [])).toBe(true)
    expect(receiverValid(ReceiverType.All, undefined)).toBe(true)
  })
  it('定向(Role)但空选 → 无效', () => {
    expect(receiverValid(ReceiverType.Role, [])).toBe(false)
    expect(receiverValid(ReceiverType.User, undefined)).toBe(false)
  })
  it('定向且选了 → 有效', () => {
    expect(receiverValid(ReceiverType.Role, [1])).toBe(true)
  })
})
