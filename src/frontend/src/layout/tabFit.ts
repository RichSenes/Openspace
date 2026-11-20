/** Which tabs sit in the top bar and which go behind "More". Both lists keep the tabs' order. */
export interface TabSplit {
  shown: number[]
  more: number[]
}

/**
 * Splits the tabs by the room they have: all of them when they fit, otherwise as many as fit
 * beside the More button, from the first. The current tab is always among the shown ones, so
 * the person sees where they are: when it would fall behind More it takes the place of the
 * last tabs that fit.
 *
 * @param widths   width of each tab, gap included
 * @param room     width the tabs have in the bar
 * @param moreWidth width of the More button, gap included
 * @param current  index of the current tab, or -1
 */
export function fitTabs(widths: number[], room: number, moreWidth: number, current: number): TabSplit {
  const all = widths.map((_, i) => i)
  const total = widths.reduce((sum, w) => sum + w, 0)
  if (total <= room) return { shown: all, more: [] }

  const budget = room - moreWidth
  const shown: number[] = []
  let used = 0
  for (const i of all) {
    if (used + widths[i] > budget) break
    shown.push(i)
    used += widths[i]
  }

  if (current >= 0 && current < widths.length && !shown.includes(current)) {
    // Drop tabs from the end until the current one fits; it stays even when nothing else does.
    while (shown.length > 0 && used + widths[current] > budget) used -= widths[shown.pop()!]
    shown.push(current)
  }

  return { shown, more: all.filter((i) => !shown.includes(i)) }
}
