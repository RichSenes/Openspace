/** The three modes of a session with Shiori, from least to most power. */
export type Mode = 'brainstorm' | 'decide' | 'organise'

export interface ModeInfo {
  label: string
  /** What the mode lets Shiori do, said under the switch. */
  allows: string
  /** The composer's placeholder: what sending a message in this mode leads to. */
  placeholder: string
  /** The line written into the transcript when the person switches to it. */
  switched: string
  /** CSS colour token the session quietly takes. */
  color: string
  /** The same colour for canvas and WebGL faces. */
  hex: string
  /** 1, 2 or 3: how much the mode may change. */
  power: number
}

export const MODES: Record<Mode, ModeInfo> = {
  brainstorm: {
    label: 'Brainstorm',
    allows: 'Asks and suggests. Nothing is created.',
    placeholder: 'Brainstorm: nothing will be created',
    switched: 'Shiori can ask and suggest; nothing will be created',
    color: 'var(--st-queued)',
    hex: '#82a8f5',
    power: 1,
  },
  decide: {
    label: 'Decide',
    allows: 'May propose decisions and rules. You approve each one.',
    placeholder: 'Decide: Shiori may propose decisions for you to approve',
    switched: 'Shiori may now propose decisions and rules',
    color: 'var(--accent)',
    hex: '#f2b84b',
    power: 2,
  },
  organise: {
    label: 'Organise',
    allows: 'Turns the session into plans and tasks across offices, for your approval.',
    placeholder: 'Organise: plans and tasks wait for your approval',
    switched: 'Shiori may now turn the session into plans and tasks',
    color: 'var(--st-running)',
    hex: '#8fd282',
    power: 3,
  },
}

export const MODE_ORDER: Mode[] = ['brainstorm', 'decide', 'organise']
