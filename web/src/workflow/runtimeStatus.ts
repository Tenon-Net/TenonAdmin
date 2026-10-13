/** 列表和详情把「审批中」拆成阻断、部分阻断、已退回。 */
export function runtimeStatusKey(row: {
  status?: number | string | null
  returnedToStarter?: boolean | null
  blockedNodeIds?: readonly string[] | null
  hasPendingTask?: boolean | null
}): 'returned' | 'blocked' | 'blockedPartial' | null {
  if (row.returnedToStarter) return 'returned'
  if ((row.blockedNodeIds?.length ?? 0) > 0 && Number(row.status) === 1) {
    return row.hasPendingTask ? 'blockedPartial' : 'blocked'
  }
  return null
}
