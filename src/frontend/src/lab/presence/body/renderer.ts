import { BLIT_FS, POINT_FS, POINT_VS, POST_FS, QUAD_VS } from './shaders'
import { buildSkin, KIND, PORTRAIT_UNITS, SKIN_INDEX, type PointSet, type SkinId } from './shapes'

/** Everything a frame needs from the page; the renderer keeps no state of its own about Shiori. */
export interface FrameUniforms {
  skin: SkinId
  phase: number
  time: number
  energy: number
  voice: number
  eyes: number
  open: number
  pleased: number
  unsure: number
  lean: number
  heat: number
  gaze: [number, number]
  rot: Float32Array
  eyeRot: Float32Array
  eyeCenter: [number, number, number]
  eyeSize: [number, number, number]
  lift: number
  bodyScale: number
  scale: number
  offset: [number, number]
  cool: [number, number, number]
  hot: [number, number, number]
  eye: [number, number, number]
  ascii: number
  glitch: number
  /** Present: the worm trails, as xyz per point. */
  worms?: Float32Array
  /** The portrait's drawing, which its points take their light from. */
  sprite?: HTMLCanvasElement
  /** Pixel mode, small: one point per stride×stride art pixels, averaged. */
  stride?: number
  wormLength: number
}

export interface Look {
  /** 0 smooth; otherwise how many screen pixels make one art pixel. */
  pixel: number
  /** The world's palette for pixel mode, darkest first (up to 8). */
  palette: [number, number, number][]
}

type Prog = { p: WebGLProgram; u: Record<string, WebGLUniformLocation | null> }

function compile(gl: WebGL2RenderingContext, vs: string, fs: string): Prog {
  const make = (type: number, src: string) => {
    const s = gl.createShader(type)!
    gl.shaderSource(s, src)
    gl.compileShader(s)
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s) ?? 'shader')
    return s
  }
  const p = gl.createProgram()!
  gl.attachShader(p, make(gl.VERTEX_SHADER, vs))
  gl.attachShader(p, make(gl.FRAGMENT_SHADER, fs))
  gl.bindAttribLocation(p, 0, 'aPos')
  gl.bindAttribLocation(p, 1, 'aData')
  gl.bindAttribLocation(p, 2, 'aExtra')
  gl.linkProgram(p)
  if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) ?? 'link')
  const u: Prog['u'] = {}
  const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS) as number
  for (let i = 0; i < n; i++) {
    const info = gl.getActiveUniform(p, i)!
    const name = info.name.replace(/\[0\]$/, '')
    u[name] = gl.getUniformLocation(p, info.name)
  }
  return { p, u }
}

interface Target {
  fb: WebGLFramebuffer
  tex: WebGLTexture
  w: number
  h: number
}

/**
 * The glyphs of the ASCII dissolve, from empty to full: 5×7 bitmaps drawn into cells of 6×8,
 * so they stay sharp on the pixel grid at any size.
 */
export const GLYPHS: string[][] = [
  ['.....', '.....', '.....', '.....', '.....', '.....', '.....'],
  ['.....', '.....', '.....', '.....', '.....', '.....', '..#..'],
  ['.....', '.....', '..#..', '.....', '.....', '..#..', '.....'],
  ['.....', '.....', '.....', '.###.', '.....', '.....', '.....'],
  ['.....', '.....', '#####', '.....', '#####', '.....', '.....'],
  ['.....', '..#..', '..#..', '#####', '..#..', '..#..', '.....'],
  ['.....', '#.#.#', '.###.', '#####', '.###.', '#.#.#', '.....'],
  ['.....', '.###.', '#...#', '#...#', '#...#', '.###.', '.....'],
  ['#...#', '.#.#.', '..#..', '.#.#.', '#...#', '.....', '.....'],
  ['##..#', '##.#.', '..#..', '.#...', '#..##', '...##', '.....'],
  ['#...#', '.#..#', '....#', '...#.', '..#..', '##...', '.....'],
  ['.#.#.', '#####', '.#.#.', '#####', '.#.#.', '.....', '.....'],
  ['#####', '#...#', '#.#.#', '#...#', '#####', '.....', '.....'],
  ['.###.', '#...#', '#.###', '#.#.#', '#.##.', '#....', '.###.'],
  ['#.#.#', '.#.#.', '#.#.#', '.#.#.', '#.#.#', '.#.#.', '#.#.#'],
  ['#####', '#####', '#####', '#####', '#####', '#####', '#####'],
]

function glyphAtlas(gl: WebGL2RenderingContext): WebGLTexture {
  const cw = 6
  const ch = 8
  const data = new Uint8Array(cw * 16 * ch * 4)
  GLYPHS.forEach((rows, i) =>
    rows.forEach((row, y) =>
      [...row].forEach((c, x) => {
        if (c !== '#') return
        const o = (y * cw * 16 + i * cw + x) * 4
        data[o] = data[o + 1] = data[o + 2] = data[o + 3] = 255
      }),
    ),
  )
  const tex = gl.createTexture()!
  gl.bindTexture(gl.TEXTURE_2D, tex)
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, cw * 16, ch, 0, gl.RGBA, gl.UNSIGNED_BYTE, data)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
  return tex
}

/**
 * Draws Shiori into a canvas with WebGL2: the points into an offscreen target (small in pixel
 * mode), the post pass into a second one, then the result onto the canvas without smoothing.
 */
export class BodyRenderer {
  private gl: WebGL2RenderingContext
  private points: Prog
  private post: Prog
  private blit: Prog
  private spriteTex: WebGLTexture
  private glyphs: WebGLTexture
  private body: { vao: WebGLVertexArrayObject; count: number; skin: SkinId } | null = null
  private worm: { vao: WebGLVertexArrayObject; buf: WebGLBuffer; count: number } | null = null
  private scene: Target | null = null
  private out: Target | null = null
  private empty: WebGLVertexArrayObject
  private canvas: HTMLCanvasElement

  constructor(canvas: HTMLCanvasElement) {
    this.canvas = canvas
    const gl = canvas.getContext('webgl2', { antialias: false, alpha: false, premultipliedAlpha: false })
    if (!gl) throw new Error('WebGL2 is not available')
    this.gl = gl
    this.points = compile(gl, POINT_VS, POINT_FS)
    this.post = compile(gl, QUAD_VS, POST_FS)
    this.blit = compile(gl, QUAD_VS, BLIT_FS)
    this.spriteTex = gl.createTexture()!
    gl.bindTexture(gl.TEXTURE_2D, this.spriteTex)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
    this.glyphs = glyphAtlas(gl)
    this.empty = gl.createVertexArray()!
  }

  private upload(set: PointSet): WebGLVertexArrayObject {
    const gl = this.gl
    const vao = gl.createVertexArray()!
    gl.bindVertexArray(vao)
    const attr = (i: number, data: Float32Array, size: number) => {
      const b = gl.createBuffer()!
      gl.bindBuffer(gl.ARRAY_BUFFER, b)
      gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW)
      gl.enableVertexAttribArray(i)
      gl.vertexAttribPointer(i, size, gl.FLOAT, false, 0, 0)
      return b
    }
    attr(0, set.pos, 3)
    attr(1, set.data, 4)
    attr(2, set.extra, 2)
    gl.bindVertexArray(null)
    return vao
  }

  private ensureSkin(skin: SkinId, wormCount: number, wormLength: number) {
    if (this.body?.skin !== skin) {
      const set = buildSkin(skin)
      this.body = { vao: this.upload(set), count: set.count, skin }
    }
    const count = wormCount * wormLength
    if (skin === 'swarm' && count > 0 && this.worm?.count !== count) {
      const data = new Float32Array(count * 4)
      for (let i = 0; i < count; i++) {
        data[i * 4] = KIND.strand
        data[i * 4 + 1] = (Math.floor(i / wormLength) * 0.37) % 1
        data[i * 4 + 2] = (i % wormLength) / (wormLength - 1)
      }
      const gl = this.gl
      const vao = this.upload({ count, pos: new Float32Array(count * 3), data, extra: new Float32Array(count * 2) })
      gl.bindVertexArray(vao)
      const buf = gl.createBuffer()!
      gl.bindBuffer(gl.ARRAY_BUFFER, buf)
      gl.bufferData(gl.ARRAY_BUFFER, count * 12, gl.DYNAMIC_DRAW)
      gl.enableVertexAttribArray(0)
      gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0)
      gl.bindVertexArray(null)
      this.worm = { vao, buf, count }
    }
  }

  private target(old: Target | null, w: number, h: number): Target {
    if (old && old.w === w && old.h === h) return old
    const gl = this.gl
    if (old) {
      gl.deleteFramebuffer(old.fb)
      gl.deleteTexture(old.tex)
    }
    const tex = gl.createTexture()!
    gl.bindTexture(gl.TEXTURE_2D, tex)
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
    const fb = gl.createFramebuffer()!
    gl.bindFramebuffer(gl.FRAMEBUFFER, fb)
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0)
    return { fb, tex, w, h }
  }

  render(f: FrameUniforms, look: Look, wormCount: number) {
    const gl = this.gl
    const cw = this.canvas.width
    const ch = this.canvas.height
    const pixel = look.pixel > 0
    const w = pixel ? Math.max(1, Math.round(cw / look.pixel)) : cw
    const h = pixel ? Math.max(1, Math.round(ch / look.pixel)) : ch
    this.scene = this.target(this.scene, w, h)
    this.out = this.target(this.out, w, h)
    this.ensureSkin(f.skin, f.skin === 'swarm' ? wormCount : 0, f.wormLength)

    // 1. The points, added light on black.
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.scene.fb)
    gl.viewport(0, 0, w, h)
    gl.clearColor(0, 0, 0, 1)
    gl.clear(gl.COLOR_BUFFER_BIT)
    gl.enable(gl.BLEND)
    gl.blendFunc(gl.ONE, gl.ONE)
    const P = this.points
    gl.useProgram(P.p)
    const u1 = (n: string, v: number) => gl.uniform1f(P.u[n], v)
    const u2 = (n: string, v: [number, number]) => gl.uniform2f(P.u[n], v[0], v[1])
    const u3 = (n: string, v: [number, number, number]) => gl.uniform3f(P.u[n], v[0], v[1], v[2])
    const minSide = Math.min(w, h)
    u1('uSkin', SKIN_INDEX[f.skin])
    u2('uRes', [w, h])
    u1('uPhase', f.phase)
    u1('uTime', f.time)
    u1('uEnergy', f.energy)
    u1('uVoice', f.voice)
    u1('uEyes', f.eyes)
    u1('uOpen', f.open)
    u1('uPleased', f.pleased)
    u1('uUnsure', f.unsure)
    u1('uLean', f.lean)
    u1('uHeat', f.heat)
    u1('uAspect', w / h)
    u1('uScale', f.scale)
    u1('uLift', f.lift)
    u1('uBodyScale', f.bodyScale)
    u1('uPixel', pixel ? 1 : 0)
    // Point size follows the body's size on screen, so density holds at every width.
    u1('uPoint', Math.max(1, (minSide * f.scale) / (pixel ? 150 : 95)))
    u2('uGaze', f.gaze)
    u2('uOffset', f.offset)
    gl.uniformMatrix3fv(P.u.uRot, false, f.rot)
    gl.uniformMatrix3fv(P.u.uEyeRot, false, f.eyeRot)
    u3('uEyeC', f.eyeCenter)
    u3('uEyeSize', f.eyeSize)
    u3('uCool', f.cool)
    u3('uHot', f.hot)
    u3('uEye', f.eye)
    u1('uWorm', 0)
    if (f.sprite) {
      gl.activeTexture(gl.TEXTURE0)
      gl.bindTexture(gl.TEXTURE_2D, this.spriteTex)
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, f.sprite)
      gl.generateMipmap(gl.TEXTURE_2D)
      gl.uniform1i(P.u.uSprite, 0)
      gl.uniform1f(P.u.uStride, f.stride ?? 1)
      gl.uniform2f(P.u.uArt, f.sprite.width, f.sprite.height)
      // One art pixel of the portrait on the target: its height spans PORTRAIT_UNITS of body space.
      gl.uniform1f(P.u.uArtPx, Math.max(1, ((PORTRAIT_UNITS * f.bodyScale * f.scale * h) / 2 / f.sprite.height) * (f.stride ?? 1) * (pixel ? 1 : 1.05)))
    }
    if (this.body!.count) {
      if (f.sprite && pixel) gl.blendEquation(gl.MAX)
      gl.bindVertexArray(this.body!.vao)
      gl.drawArrays(gl.POINTS, 0, this.body!.count)
      gl.blendEquation(gl.FUNC_ADD)
    }

    if (f.skin === 'swarm' && f.worms && this.worm) {
      gl.bindBuffer(gl.ARRAY_BUFFER, this.worm.buf)
      gl.bufferSubData(gl.ARRAY_BUFFER, 0, f.worms.subarray(0, this.worm.count * 3))
      u1('uWorm', 1)
      gl.bindVertexArray(this.worm.vao)
      gl.drawArrays(gl.POINTS, 0, this.worm.count)
    }
    gl.disable(gl.BLEND)

    // 2. Glow, ASCII, glitch, palette.
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.out.fb)
    gl.viewport(0, 0, w, h)
    const Q = this.post
    gl.useProgram(Q.p)
    gl.activeTexture(gl.TEXTURE0)
    gl.bindTexture(gl.TEXTURE_2D, this.scene.tex)
    gl.uniform1i(Q.u.uScene, 0)
    gl.activeTexture(gl.TEXTURE1)
    gl.bindTexture(gl.TEXTURE_2D, this.glyphs)
    gl.uniform1i(Q.u.uGlyph, 1)
    gl.uniform2f(Q.u.uRes, w, h)
    // One glyph cell is 6×8 art pixels; in smooth mode each of those is two CSS pixels.
    const k = pixel ? 1 : Math.max(1, Math.round(2 * (cw / (this.canvas.clientWidth || cw))))
    const cell = [6 * k, 8 * k]
    gl.uniform2f(Q.u.uCell, cell[0], cell[1])
    gl.uniform1f(Q.u.uAscii, f.ascii)
    gl.uniform1f(Q.u.uGlitch, f.glitch)
    gl.uniform1f(Q.u.uPixel, pixel ? 1 : 0)
    gl.uniform1f(Q.u.uTime, f.time)
    gl.uniform1f(Q.u.uBloom, pixel ? 0.9 : 1.3)
    gl.uniform1f(Q.u.uBloomR, pixel ? 1.2 : Math.max(2, minSide / 220))
    const pal = look.palette.slice(0, 8)
    gl.uniform1f(Q.u.uPalN, pal.length)
    if (pal.length) gl.uniform3fv(Q.u.uPal, new Float32Array([...pal.flat(), ...new Array((8 - pal.length) * 3).fill(0)]))
    gl.bindVertexArray(this.empty)
    gl.drawArrays(gl.TRIANGLES, 0, 3)

    // 3. Onto the canvas, pixels kept square.
    gl.bindFramebuffer(gl.FRAMEBUFFER, null)
    gl.viewport(0, 0, cw, ch)
    const B = this.blit
    gl.useProgram(B.p)
    gl.activeTexture(gl.TEXTURE0)
    gl.bindTexture(gl.TEXTURE_2D, this.out.tex)
    gl.uniform1i(B.u.uTex, 0)
    gl.uniform2f(B.u.uRes, cw, ch)
    gl.drawArrays(gl.TRIANGLES, 0, 3)
  }

  /** Frees the GPU objects; the context stays, since React may mount the same canvas again. */
  dispose() {
    const gl = this.gl
    for (const t of [this.scene, this.out]) {
      if (!t) continue
      gl.deleteFramebuffer(t.fb)
      gl.deleteTexture(t.tex)
    }
    gl.deleteTexture(this.glyphs)
    gl.deleteTexture(this.spriteTex)
    for (const p of [this.points, this.post, this.blit]) gl.deleteProgram(p.p)
  }
}
