/**
 * Shiori's shaders. The point shader moves every point of every skin; the post shader adds the
 * glow, the ASCII dissolve, the glitch and, in pixel mode, snaps colours to the world's palette.
 *
 * The 3D simplex noise is Ashima Arts' (Ian McEwan, Stefan Gustavson), MIT licence,
 * https://github.com/ashima/webgl-noise.
 */
const NOISE = /* glsl */ `
vec3 mod289(vec3 x){return x-floor(x*(1.0/289.0))*289.0;}
vec4 mod289(vec4 x){return x-floor(x*(1.0/289.0))*289.0;}
vec4 permute(vec4 x){return mod289(((x*34.0)+10.0)*x);}
vec4 taylorInvSqrt(vec4 r){return 1.79284291400159-0.85373472095314*r;}
float snoise(vec3 v){
  const vec2 C=vec2(1.0/6.0,1.0/3.0);
  const vec4 D=vec4(0.0,0.5,1.0,2.0);
  vec3 i=floor(v+dot(v,C.yyy));
  vec3 x0=v-i+dot(i,C.xxx);
  vec3 g=step(x0.yzx,x0.xyz);
  vec3 l=1.0-g;
  vec3 i1=min(g.xyz,l.zxy);
  vec3 i2=max(g.xyz,l.zxy);
  vec3 x1=x0-i1+C.xxx;
  vec3 x2=x0-i2+C.yyy;
  vec3 x3=x0-D.yyy;
  i=mod289(i);
  vec4 p=permute(permute(permute(i.z+vec4(0.0,i1.z,i2.z,1.0))+i.y+vec4(0.0,i1.y,i2.y,1.0))+i.x+vec4(0.0,i1.x,i2.x,1.0));
  float n_=0.142857142857;
  vec3 ns=n_*D.wyz-D.xzx;
  vec4 j=p-49.0*floor(p*ns.z*ns.z);
  vec4 x_=floor(j*ns.z);
  vec4 y_=floor(j-7.0*x_);
  vec4 x=x_*ns.x+ns.yyyy;
  vec4 y=y_*ns.x+ns.yyyy;
  vec4 h=1.0-abs(x)-abs(y);
  vec4 b0=vec4(x.xy,y.xy);
  vec4 b1=vec4(x.zw,y.zw);
  vec4 s0=floor(b0)*2.0+1.0;
  vec4 s1=floor(b1)*2.0+1.0;
  vec4 sh=-step(h,vec4(0.0));
  vec4 a0=b0.xzyw+s0.xzyw*sh.xxyy;
  vec4 a1=b1.xzyw+s1.xzyw*sh.zzww;
  vec3 p0=vec3(a0.xy,h.x);
  vec3 p1=vec3(a0.zw,h.y);
  vec3 p2=vec3(a1.xy,h.z);
  vec3 p3=vec3(a1.zw,h.w);
  vec4 norm=taylorInvSqrt(vec4(dot(p0,p0),dot(p1,p1),dot(p2,p2),dot(p3,p3)));
  p0*=norm.x;p1*=norm.y;p2*=norm.z;p3*=norm.w;
  vec4 m=max(0.5-vec4(dot(x0,x0),dot(x1,x1),dot(x2,x2),dot(x3,x3)),0.0);
  m=m*m;
  return 105.0*dot(m*m,vec4(dot(p0,x0),dot(p1,x1),dot(p2,x2),dot(p3,x3)));
}
`

export const POINT_VS = /* glsl */ `#version 300 es
precision highp float;
in vec3 aPos;
in vec4 aData;
in vec2 aExtra;
uniform float uSkin, uPhase, uTime, uEnergy, uVoice, uEyes, uOpen, uPleased, uUnsure, uLean, uHeat, uLid, uLidTilt, uWide;
uniform float uAspect, uScale, uPoint, uPixel, uWorm, uLift, uBodyScale, uDim;
uniform vec2 uGaze, uOffset;
uniform mat3 uRot, uEyeRot;
uniform vec3 uEyeC, uCool, uHot, uEye;
uniform vec3 uEyeSize; // x: half width, y: half height, z: half distance between the eyes
uniform sampler2D uSprite; // the portrait, drawn at its art size: its points take their light from it
uniform float uArtPx;      // one art pixel of the portrait, in target pixels
uniform vec2 uRes;         // the target's size in pixels, to snap points to it in pixel mode
out vec3 vColor;
out float vDash;
${NOISE}

// The membrane of the swarm, displaced by noise and by the voice.
vec3 swarmAt(vec3 p, out float n){
  n = snoise(p * 1.5 + vec3(0.0, 0.0, uPhase * 0.3));
  float ripple = snoise(p * 4.0 + vec3(uPhase * 1.3));
  float r = 1.0 + (0.07 + 0.12 * uEnergy) * n + uVoice * 0.08 * ripple;
  r *= 1.0 - 0.08 * uLean;
  return p * r;
}

// The medusa's pulse: a quick squeeze, a slow release.
float pulse(){
  float ph = fract(uPhase * 0.22);
  return smoothstep(0.0, 0.12, ph) * (1.0 - smoothstep(0.12, 0.85, ph));
}

vec3 bellAt(vec3 p, float u, out float n){
  float c = pulse();
  n = snoise(p * 3.0 + vec3(uPhase * 0.5));
  vec3 q = p;
  q.xz *= 1.0 - 0.22 * c * u;
  q.y += 0.06 * c * u;
  q += p * 0.04 * n * (0.4 + uEnergy) + p * uVoice * 0.05 * snoise(p * 6.0 + uTime * 3.0);
  q.y += 0.07 * c;
  return q;
}

vec3 strandAt(vec3 root, float s, float seed, float inner, out float sig){
  float c = pulse();
  vec3 r = root;
  r.xz *= 1.0 - 0.22 * c;
  r.y += 0.07 * c;
  float len = mix(1.35 + 0.35 * fract(seed * 7.0), 0.95, inner) * (1.0 - 0.12 * c);
  vec2 out_ = normalize(root.xz + 1e-4);
  vec3 q = r + vec3(0.0, -s * len, 0.0);
  q.xz += out_ * s * mix(0.16, 0.05, inner) * (1.0 + 0.8 * c);
  float lag = uPhase * (1.2 + seed * 0.6) - s * (5.0 + seed * 3.0) + seed * 20.0;
  q.x += (sin(lag) * 0.12 + sin(lag * 2.3) * 0.04) * s;
  q.z += cos(lag * 0.8 + seed * 13.0) * 0.1 * s;
  q.xz += inner * vec2(sin(s * 28.0 + uPhase * 3.0), cos(s * 24.0 + uPhase * 2.6)) * 0.035 * s;
  float d = abs(fract(s * 1.5 - uPhase * (0.35 + uHeat * 0.6) + seed) - 0.5);
  sig = smoothstep(0.08, 0.0, d);
  return q;
}

void main(){
  float pLum = 0.0;
  float kind = aData.x;
  float seed = aData.y;
  vec3 body;
  float n = 0.0;
  float glow = 0.0;
  float alpha = 1.0;
  vDash = 0.0;
  bool eye = kind > 1.5;

  if (uWorm > 0.5) {
    body = swarmAt(aPos, n);
    float along = aData.z;
    glow = 1.6 * (1.0 - along) + 0.4;
  } else if (uSkin < 0.5) {
    body = swarmAt(aPos, n);
  } else if (uSkin < 1.5) {
    if (kind > 0.5 && kind < 1.5) {
      body = strandAt(aPos, aData.z, seed, aData.w, glow);
      glow *= 1.8;
      alpha = 1.0 - aData.z * 0.55;
    } else {
      body = bellAt(aPos, eye ? 1.0 : aData.z, n);
      alpha = eye ? 1.0 : 0.55 + 0.45 * aData.z;
    }
  } else {
    // The portrait as a cloud of points: one per art pixel, lit by the drawing underneath, on a
    // gently curved surface so turning gives depth. The membrane breathes like the others.
    // When an art pixel is smaller than a screen pixel, read the drawing averaged to that size,
    // so the points that land on the same pixel agree instead of flickering.
    vec4 c = textureLod(uSprite, aData.zw, max(0.0, -log2(max(uArtPx, 0.01))));
    pLum = dot(c.rgb, vec3(0.299, 0.587, 0.114));
    n = snoise(aPos * 5.0 + vec3(0.0, 0.0, uPhase * 0.5));
    body = aPos + vec3(0.0, 0.0, 0.012 * n * (0.5 + uEnergy));
    body.x += snoise(vec3(aPos.y * 30.0, uTime * 6.0, 0.0)) * 0.006 * uVoice;
    // The base comes apart: its points drift up and out and fade.
    float fade = smoothstep(0.78, 1.0, aData.w);
    float drift = fract(seed * 7.0 + uTime * (0.08 + seed * 0.1));
    body.y += fade * drift * 0.45;
    body.x += fade * (seed - 0.5) * 0.5 * drift;
    alpha = step(0.5, c.a) * (1.0 - fade * drift);
  }

  vec3 v = uRot * (body * uBodyScale);
  v.y += uLift;

  float m = 0.0;
  if (eye) {
    float side = kind < 2.5 ? -1.0 : 1.0;
    vec2 q = aData.zw;
    q.y *= uOpen;
    float tilt = side * 0.38 * uUnsure;
    q = mat2(cos(tilt), -sin(tilt), sin(tilt), cos(tilt)) * (q * (1.0 - 0.25 * uUnsure));
    q = mix(q, aExtra * vec2(1.0, 1.15), uPleased);
    // The upper lid: a slanted line; points above it gather on it, so the top of the eye goes flat.
    // Positive tilt drops the outer corner (soft, sad); negative drops the inner one (intent).
    float lidLine = 1.0 - 2.0 * uLid * (1.0 - uPleased) - uLidTilt * q.x * side * 0.6;
    q.y = min(q.y, lidLine * uOpen);
    q *= 1.0 + 0.18 * uWide;
    vec3 local = vec3(side * uEyeSize.z + q.x * uEyeSize.x + uGaze.x * 0.08,
                      q.y * uEyeSize.y - uGaze.y * 0.06 - 0.03 * uUnsure, 0.0);
    local.z -= local.x * local.x * 0.6;
    vec3 target = uEyeRot * (uEyeC + local) + vec3(0.0, uLift, 0.0);
    m = smoothstep(0.0, 1.0, clamp(uEyes * 1.7 - seed * 0.7, 0.0, 1.0));
    v = mix(v, target, m);
    alpha = mix(alpha * 0.6, 1.0, m);
    vDash = mix(vDash, 0.0, m);
  }

  float persp = 3.0 / (3.0 - v.z);
  vec2 clip = vec2(v.x * persp * uScale / uAspect, v.y * persp * uScale) + uOffset;
  // In pixel mode every point sits on a whole pixel, so nothing shimmers as she moves or resizes.
  if (uPixel > 0.5) clip = (floor((clip * 0.5 + 0.5) * uRes) + 0.5) / uRes * 2.0 - 1.0;
  gl_Position = vec4(clip, 0.0, 1.0);

  float depth = clamp(v.z * 0.5 + 0.5, 0.0, 1.0);
  float bright = (0.28 + 0.72 * depth) * (0.55 + 0.6 * uEnergy);
  float size = uPoint * (0.7 + 0.6 * depth);
  if (uSkin > 0.5 && uSkin < 1.5) {
    if (kind > 0.5 && kind < 1.5) { size *= 0.75; bright *= 0.7; }
    else if (!eye) bright *= 0.62;
  }
  vec3 col = mix(uCool, uHot, clamp(uHeat * 0.55 + n * 0.25 + glow * 0.5, 0.0, 1.0));
  col += vec3(glow) * 0.35;
  if (uWorm > 0.5) {
    col = mix(uHot, vec3(1.0), 0.35 * (1.0 - aData.z));
    size *= 1.0 + 0.9 * (1.0 - aData.z);
  }
  if (uSkin > 1.5) {
    // Contrast, so features stay dark against the skin; the glow adds the rest.
    float k = smoothstep(0.06, 0.98, pLum);
    bright = (0.1 + 0.95 * k) * (0.7 + 0.35 * depth) * (0.6 + 0.5 * uEnergy);
    // Smooth: soft dots a little larger where she is lit. Pixel: whole pixels a little larger than
    // their spacing, so no row or column is ever left empty; overlaps keep the brightest.
    size = uPixel > 0.5 ? max(1.0, ceil(uArtPx * 1.2)) : max(1.0, uArtPx) * (0.9 + 0.5 * k);
    // Pixel mode snaps to the world's few colours: keep her below white, whatever the mode's heat.
    if (uPixel > 0.5) {
      bright = min(bright * 0.42, 0.5);
      col = mix(uCool, uHot, clamp(uHeat * 0.3 + (1.0 - k) * 0.25, 0.0, 1.0));
    }
    col = mix(uCool, uHot, clamp(uHeat * 0.4 + n * 0.2 + (1.0 - k) * 0.25, 0.0, 1.0)) + vec3(0.25, 0.3, 0.4) * k * k;
  }
  float rim = eye ? smoothstep(0.55, 0.95, length(aData.zw)) : 0.0;
  col = mix(col, uEye, m);
  bright = mix(bright, 0.42 + 0.5 * rim, m);
  size = mix(size, uPoint * 0.95, m);
  if (uPixel > 0.5) size = max(1.0, floor(size + 0.5));
  gl_PointSize = size;
  vColor = col * bright * alpha * uDim;
}
`

export const POINT_FS = /* glsl */ `#version 300 es
precision highp float;
in vec3 vColor;
in float vDash;
uniform float uPixel;
out vec4 o;
void main(){
  vec2 c = gl_PointCoord - 0.5;
  float a;
  if (vDash > 0.5) {
    a = uPixel > 0.5 ? step(abs(c.y), 0.17) : smoothstep(0.22, 0.1, abs(c.y)) * smoothstep(0.5, 0.3, abs(c.x));
  } else if (uPixel > 0.5) {
    a = step(length(c), 0.5);
  } else {
    a = exp(-dot(c, c) * 14.0);
  }
  o = vec4(vColor * a, 1.0);
}
`

export const QUAD_VS = /* glsl */ `#version 300 es
precision highp float;
const vec2 P[3] = vec2[3](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
void main(){ gl_Position = vec4(P[gl_VertexID], 0.0, 1.0); }
`

export const POST_FS = /* glsl */ `#version 300 es
precision highp float;
uniform sampler2D uScene;
uniform sampler2D uGlyph;
uniform vec2 uRes, uCell;
uniform float uAscii, uGlitch, uPixel, uTime, uBloom, uBloomR, uPalN;
uniform vec3 uPal[8];
out vec4 o;

float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float bayer(vec2 p){
  ivec2 i = ivec2(mod(p, 4.0));
  int k = i.x + i.y * 4;
  int m[16] = int[16](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
  return (float(m[k]) + 0.5) / 16.0;
}

vec3 scene(vec2 uv){ return texture(uScene, uv).rgb; }

void main(){
  vec2 uv = gl_FragCoord.xy / uRes;
  float tick = floor(uTime * 24.0);
  float band = floor(uv.y * 28.0);
  float on = step(1.0 - 0.55 * uGlitch, hash(vec2(band, tick)));
  uv.x += (hash(vec2(band, tick + 7.0)) - 0.5) * 0.16 * uGlitch * on;
  float split = (0.006 * uGlitch + 0.0012) * (uPixel > 0.5 ? 0.0 : 1.0) + uGlitch * on * 0.01;
  vec3 col = vec3(scene(uv + vec2(split, 0.0)).r, scene(uv).g, scene(uv - vec2(split, 0.0)).b);

  vec2 px = 1.0 / uRes;
  vec3 glow = vec3(0.0);
  // Eight directions at two radii, turned a little each pixel row so the ring never shows.
  float spin = hash(gl_FragCoord.xy) * 0.785;
  for (int i = 0; i < 8; i++) {
    float a = float(i) * 0.785 + spin;
    vec2 d = vec2(cos(a), sin(a)) * px * uBloomR;
    glow += scene(uv + d * 1.6) + scene(uv + d * 4.2) * 0.6;
  }
  col += glow / 8.0 * uBloom * 0.95;

  if (uAscii > 0.001) {
    vec2 cell = floor(gl_FragCoord.xy / uCell);
    vec2 cuv = (cell + 0.5) * uCell / uRes;
    vec3 cc = scene(cuv) * 1.3 + scene(cuv + vec2(uCell.x, 0.0) * px * 0.5) * 0.5;
    float l = clamp(max(cc.r, max(cc.g, cc.b)), 0.0, 1.0);
    float gi = min(13.0, floor(l * 14.0));
    vec2 g = fract(gl_FragCoord.xy / uCell);
    float glyph = texture(uGlyph, vec2((gi + g.x) / 16.0, 1.0 - g.y)).r;
    vec3 tint = cc / max(0.001, max(cc.r, max(cc.g, cc.b)));
    vec3 asc = tint * glyph * step(0.03, l) * (0.6 + l);
    col = mix(col, asc, uAscii);
  }

  if (uPixel > 0.5 && uPalN > 0.5) {
    vec3 c = col + (bayer(gl_FragCoord.xy) - 0.5) * 0.12;
    vec3 best = uPal[0];
    float bd = 1e9;
    for (int i = 0; i < 8; i++) {
      if (float(i) >= uPalN) break;
      vec3 d = c - uPal[i];
      float dd = dot(d, d * vec3(0.9, 1.2, 0.8));
      if (dd < bd) { bd = dd; best = uPal[i]; }
    }
    col = best;
  }
  o = vec4(col, 1.0);
}
`

export const BLIT_FS = /* glsl */ `#version 300 es
precision highp float;
uniform sampler2D uTex;
uniform vec2 uRes;
out vec4 o;
void main(){ o = texture(uTex, gl_FragCoord.xy / uRes); }
`
