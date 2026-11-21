import { useEffect, useRef, useState } from 'react'
import { hexToRgb, type FaceLook, type FaceState } from '../faceMachine'
import { fitCanvas, useFaceLoop, type FaceProps } from './useFaceLoop'

const VERT = `attribute vec2 p; void main(){ gl_Position = vec4(p, 0.0, 1.0); }`

/**
 * A sphere of light: noise swirling inside, a Fresnel rim, two tilted rings and particles on
 * orbits that pass in front of and behind it. All in one fragment shader, no 3D library.
 */
const FRAG = `precision highp float;
uniform vec2 res; uniform float time; uniform vec3 col; uniform vec3 mode;
uniform float energy; uniform float speed; uniform float turb; uniform float level;
uniform float doubt; uniform vec2 gaze; uniform float joy;

float hash(vec3 p){ p = fract(p*0.3183099+0.1); p*=17.0; return fract(p.x*p.y*p.z*(p.x+p.y+p.z)); }
float noise(vec3 x){ vec3 i=floor(x); vec3 f=fract(x); f=f*f*(3.0-2.0*f);
  return mix(mix(mix(hash(i),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),
             mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z); }
float fbm(vec3 p){ float a=0.5, s=0.0; for(int i=0;i<4;i++){ s+=a*noise(p); p*=2.03; a*=0.5; } return s; }
mat2 rot(float a){ float c=cos(a), s=sin(a); return mat2(c,-s,s,c); }

void main(){
  vec2 uv = (gl_FragCoord.xy - 0.5*res) / min(res.x, res.y) * 2.0;
  float t = time;
  vec2 c = gaze * 0.06;
  float r = 0.5 + 0.05*energy + 0.05*level;
  vec2 q = uv - c;
  float d = length(q);
  vec3 acc = vec3(0.0);
  float alpha = 0.0;

  // Halo
  float halo = exp(-max(d - r, 0.0) * (5.5 - 2.0*energy)) * (0.25 + 0.45*energy);
  halo *= smoothstep(1.0, 0.62, d);
  acc += col * halo * 0.55; alpha += halo * 0.55;

  // The sphere
  if (d < r) {
    float z = sqrt(r*r - d*d);
    vec3 n = vec3(q, z) / r;
    float swirl = fbm(n*2.2*(1.0+turb) + vec3(0.0, t*0.22*speed, t*0.15*speed));
    float veins = smoothstep(0.45, 0.95, fbm(n*4.0 + vec3(t*0.3*speed, 0.0, swirl*2.0)));
    float rim = pow(1.0 - n.z, 2.2);
    vec3 deep = col * 0.18;
    vec3 inner = mix(deep, col, swirl*0.9) + vec3(1.0)*veins*0.35*(0.4+energy);
    inner += mix(col, vec3(1.0), 0.5) * rim * (0.8 + 0.6*energy);
    inner += vec3(1.0) * pow(max(dot(n, normalize(vec3(-0.4, 0.5, 0.8))), 0.0), 24.0) * 0.35;
    acc = mix(acc, inner, 0.94); alpha = 1.0;
  }

  // Rings: ellipses in a tilted plane, hidden where they pass behind the sphere.
  for (int k = 0; k < 2; k++) {
    float fk = float(k);
    float tilt = 0.32 + fk*0.22 + 0.1*sin(t*0.2*speed + fk);
    vec2 p = rot(0.35 - fk*0.9 + gaze.x*0.25) * q;
    vec2 e = vec2(p.x, p.y / tilt);
    float R = r * (1.38 + fk*0.22 + joy*0.12);
    float ring = abs(length(e) - R);
    float line = smoothstep(0.018, 0.0, ring * tilt) * (0.35 + 0.4*energy);
    float ang = atan(e.y, e.x);
    // Gaps open in the rings when Shiori is not sure.
    line *= mix(1.0, step(0.0, sin(ang*5.0 + t*0.6*speed)), doubt);
    bool behind = e.y > 0.0 && d < r;
    if (!behind) { acc += mode * line; alpha = max(alpha, line); }
  }

  // Particles on orbits: notes it is reading, in front of and behind the sphere.
  for (int i = 0; i < 18; i++) {
    float fi = float(i);
    float orbit = r * (1.15 + 0.5*fract(fi*0.618));
    float w = (0.25 + fract(fi*0.37)*0.5) * speed * (mod(fi, 2.0) < 1.0 ? 1.0 : -1.0);
    float a = t*w + fi*2.39;
    vec3 o = vec3(cos(a)*orbit, sin(a)*orbit*0.3, sin(a)*orbit);
    o.xy = rot(0.35 - fract(fi*0.21)*1.4) * o.xy;
    vec2 pp = o.xy + c;
    float behind = (o.z < 0.0 && length(o.xy) < r) ? 0.0 : 1.0;
    float size = 0.012 + 0.012*fract(fi*0.71) + 0.008*energy;
    float dot_ = smoothstep(size, 0.0, length(uv - pp)) * behind;
    float glow = exp(-length(uv - pp) * 38.0) * 0.5 * behind;
    vec3 pc = mix(mode, vec3(1.0), 0.45);
    acc += pc * (dot_ + glow) * (0.5 + 0.6*energy); alpha = max(alpha, dot_ + glow*0.6);
  }

  gl_FragColor = vec4(acc, clamp(alpha, 0.0, 1.0));
}`

function compile(gl: WebGLRenderingContext, type: number, src: string) {
  const s = gl.createShader(type)!
  gl.shaderSource(s, src)
  gl.compileShader(s)
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s) ?? 'shader')
  return s
}

interface Gl {
  gl: WebGLRenderingContext
  u: Record<string, WebGLUniformLocation | null>
}

function setupGl(canvas: HTMLCanvasElement): Gl | null {
  try {
    const gl = canvas.getContext('webgl', { antialias: true })
    if (!gl) return null
    const prog = gl.createProgram()!
    gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, VERT))
    gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, FRAG))
    gl.linkProgram(prog)
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) return null
    gl.useProgram(prog)
    const buf = gl.createBuffer()
    gl.bindBuffer(gl.ARRAY_BUFFER, buf)
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW)
    const loc = gl.getAttribLocation(prog, 'p')
    gl.enableVertexAttribArray(loc)
    gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0)
    const names = ['res', 'time', 'col', 'mode', 'energy', 'speed', 'turb', 'level', 'doubt', 'gaze', 'joy']
    return { gl, u: Object.fromEntries(names.map((n) => [n, gl.getUniformLocation(prog, n)])) }
  } catch {
    return null
  }
}

const rgb = (hex: string) => hexToRgb(hex).map((v) => v / 255) as [number, number, number]

interface Params {
  turb: number
  doubt: number
  joy: number
  level: number
}

function paramsOf(state: FaceState, time: number, reduced: boolean): Params {
  const level = state === 'speaking' && !reduced ? 0.5 + 0.5 * Math.sin(time * 8.5) * Math.sin(time * 3.1 + 1) : 0
  return {
    turb: state === 'thinking' ? 0.9 : state === 'unsure' ? 0.5 : 0.15,
    doubt: state === 'unsure' ? 1 : 0,
    joy: state === 'pleased' ? 1 : 0,
    level,
  }
}

/** The same orb drawn with canvas gradients, for browsers without WebGL. */
function draw2d(ctx: CanvasRenderingContext2D, w: number, h: number, time: number, look: FaceLook, modeHex: string, p: Params, speed: number) {
  ctx.clearRect(0, 0, w, h)
  const m = Math.min(w, h) / 2
  const cx = w / 2 + look.gazeX * m * 0.06
  const cy = h / 2 - look.gazeY * m * 0.06
  const r = m * (0.5 + 0.05 * look.energy + 0.05 * p.level)
  const [cr, cg, cb] = hexToRgb(look.color)
  const halo = ctx.createRadialGradient(cx, cy, r * 0.8, cx, cy, r * 2)
  halo.addColorStop(0, `rgba(${cr},${cg},${cb},${0.25 + 0.35 * look.energy})`)
  halo.addColorStop(1, `rgba(${cr},${cg},${cb},0)`)
  ctx.fillStyle = halo
  ctx.fillRect(0, 0, w, h)
  const orb = ctx.createRadialGradient(cx - r * 0.3, cy - r * 0.35, r * 0.05, cx, cy, r)
  orb.addColorStop(0, 'rgba(255,255,255,0.9)')
  orb.addColorStop(0.35, `rgb(${cr},${cg},${cb})`)
  orb.addColorStop(0.85, `rgba(${cr * 0.35},${cg * 0.35},${cb * 0.35},1)`)
  orb.addColorStop(1, `rgba(${Math.min(255, cr + 60)},${Math.min(255, cg + 60)},${Math.min(255, cb + 60)},1)`)
  ctx.fillStyle = orb
  ctx.beginPath()
  ctx.arc(cx, cy, r, 0, Math.PI * 2)
  ctx.fill()
  const [mr, mg, mb] = hexToRgb(modeHex)
  for (let k = 0; k < 2; k++) {
    ctx.save()
    ctx.translate(cx, cy)
    ctx.rotate(-(0.35 - k * 0.9))
    ctx.strokeStyle = `rgba(${mr},${mg},${mb},${0.35 + 0.4 * look.energy})`
    ctx.lineWidth = Math.max(1, m * 0.012)
    if (p.doubt > 0) ctx.setLineDash([m * 0.12, m * 0.08])
    ctx.beginPath()
    ctx.ellipse(0, 0, r * (1.38 + k * 0.22 + p.joy * 0.12), r * (1.38 + k * 0.22) * (0.32 + k * 0.22), 0, Math.PI, Math.PI * 2, true)
    ctx.stroke()
    ctx.restore()
  }
  for (let i = 0; i < 18; i++) {
    const orbit = r * (1.15 + 0.5 * ((i * 0.618) % 1))
    const wv = (0.25 + ((i * 0.37) % 1) * 0.5) * speed * (i % 2 ? -1 : 1)
    const a = time * wv + i * 2.39
    const x = Math.cos(a) * orbit
    const y = Math.sin(a) * orbit * 0.3
    const z = Math.sin(a)
    const ang = 0.35 - ((i * 0.21) % 1) * 1.4
    const px = cx + x * Math.cos(ang) - y * Math.sin(ang)
    const py = cy - (x * Math.sin(ang) + y * Math.cos(ang))
    if (z < 0 && Math.hypot(px - cx, py - cy) < r) continue
    ctx.fillStyle = `rgba(${Math.round(mr * 0.55 + 115)},${Math.round(mg * 0.55 + 115)},${Math.round(mb * 0.55 + 115)},0.95)`
    ctx.beginPath()
    ctx.arc(px, py, m * (0.012 + 0.01 * ((i * 0.71) % 1)), 0, Math.PI * 2)
    ctx.fill()
  }
}

/** Direction B: a glowing orb, WebGL when the browser has it, canvas gradients when it does not. */
export function HoloOrb({ state, modeHex, reduced, onRenderer }: FaceProps & { onRenderer?: (r: 'webgl' | '2d') => void }) {
  const ref = useRef<HTMLCanvasElement>(null)
  const glRef = useRef<Gl | null | undefined>(undefined)
  const [renderer, setRenderer] = useState<'webgl' | '2d' | null>(null)

  useEffect(() => {
    const canvas = ref.current
    if (!canvas || glRef.current !== undefined) return
    const forced2d = new URLSearchParams(location.search).has('no-webgl')
    glRef.current = forced2d ? null : setupGl(canvas)
    const r = glRef.current ? 'webgl' : '2d'
    setRenderer(r)
    onRenderer?.(r)
  }, [onRenderer])

  useFaceLoop(state, (f) => {
    const canvas = ref.current
    if (!canvas || renderer === null) return
    const { w, h } = fitCanvas(canvas)
    const speed = f.look.speed * (reduced ? 0.25 : 1)
    const time = f.time
    const p = paramsOf(state, time, reduced)
    const g = glRef.current
    if (!g) {
      const ctx = canvas.getContext('2d')
      if (ctx) draw2d(ctx, w, h, time, f.look, modeHex, p, speed)
      return
    }
    const { gl, u } = g
    gl.viewport(0, 0, w, h)
    gl.clearColor(0, 0, 0, 0)
    gl.clear(gl.COLOR_BUFFER_BIT)
    gl.uniform2f(u.res, w, h)
    gl.uniform1f(u.time, time)
    gl.uniform3fv(u.col, rgb(f.look.color))
    gl.uniform3fv(u.mode, rgb(modeHex))
    gl.uniform1f(u.energy, f.look.energy)
    gl.uniform1f(u.speed, speed)
    gl.uniform1f(u.turb, p.turb)
    gl.uniform1f(u.level, p.level)
    gl.uniform1f(u.doubt, p.doubt)
    gl.uniform1f(u.joy, p.joy)
    gl.uniform2f(u.gaze, f.look.gazeX, -f.look.gazeY)
    gl.drawArrays(gl.TRIANGLES, 0, 3)
  })

  return <canvas ref={ref} className="face-canvas face-orb" data-renderer={renderer ?? ''} aria-hidden="true" />
}
