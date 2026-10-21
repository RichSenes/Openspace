/** One step per line; blank lines and leading list markers ("1.", "-", "*") are dropped. */
export function parseSteps(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.replace(/^\s*(\d+[.)]|[-*])\s*/, '').trim())
    .filter((line) => line.length > 0)
}

export const formatSteps = (steps: string[]) => steps.join('\n')
