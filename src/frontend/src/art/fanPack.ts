import { PROPS, type PropDef, type PropName } from './sheets/props'

/**
 * A module of the local fan pack: faithful copies of artwork we do not own, kept on
 * this machine only (git ignores the fanpack folder). Each one redraws existing props
 * with its own colours; whatever it leaves out keeps the prop's own values.
 */
export interface FanModule {
  colors: Record<string, string>
  props: Partial<Record<PropName, Partial<PropDef> & Pick<PropDef, 'sprite'>>>
}

/** Merges fan modules over the props: a redrawn sprite drops the lights of the old one. */
export function withFanPack(props: Record<PropName, PropDef>, modules: FanModule[]): Record<PropName, PropDef> {
  const merged = { ...props }
  for (const module of modules)
    for (const [name, fan] of Object.entries(module.props) as [PropName, FanModule['props'][PropName]][])
      if (fan && name in props) merged[name] = { ...props[name], lights: undefined, ...fan }
  return merged
}

const modules = Object.values(import.meta.glob<{ default: FanModule }>('./fanpack/*.ts', { eager: true })).map((m) => m.default)

/** Every prop as this machine draws it: the fan pack, where there is one, wins. */
export const FURNITURE = withFanPack(PROPS, modules)

/** The colours the fan pack brings. */
export const FAN_COLORS: Record<string, string> = Object.assign({}, ...modules.map((m) => m.colors))
