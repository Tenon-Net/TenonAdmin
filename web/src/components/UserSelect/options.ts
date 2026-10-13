/** 办理人选项。disabledIds 里的人仍列出,但不可选,并在标签后附上原因。 */
export function toParticipantOption(
  user: { id: number | string; name: string; account: string },
  disabledIds?: ReadonlyArray<number | string | null | undefined>,
  disabledSuffix?: string,
): { label: string; value: number | string; disabled?: boolean } {
  const blocked = (disabledIds ?? []).some((id) => id != null && id !== '' && String(id) === String(user.id))
  const base = `${user.name}(${user.account})`
  return {
    label: blocked && disabledSuffix ? `${base}（${disabledSuffix}）` : base,
    value: user.id,
    ...(blocked ? { disabled: true } : {}),
  }
}
