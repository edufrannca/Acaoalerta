/**
 * Alarme de emergência com o app ABERTO: sirene em loop + vibração.
 * No iPhone (Safari 17+) a sessão de áudio "playback" faz tocar mesmo com a chave do silencioso.
 * Com o app fechado, quem avisa é a notificação push (segue as regras do sistema; não fura o silencioso).
 */
let audio: HTMLAudioElement | null = null
let urlSirene: string | null = null
let vibrando: number | null = null
let desligar: number | null = null

function gerarSirene(): string {
  // WAV PCM 16 bits, 8 kHz, 2 s: 0,5 s em 880 Hz / 0,5 s em 660 Hz (repetido).
  const taxa = 8000, dur = 2, n = taxa * dur
  const buf = new ArrayBuffer(44 + n * 2), v = new DataView(buf)
  const txt = (o: number, s: string) => { for (let i = 0; i < s.length; i++) v.setUint8(o + i, s.charCodeAt(i)) }
  txt(0, 'RIFF'); v.setUint32(4, 36 + n * 2, true); txt(8, 'WAVE'); txt(12, 'fmt ')
  v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true); v.setUint32(24, taxa, true)
  v.setUint32(28, taxa * 2, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true); txt(36, 'data'); v.setUint32(40, n * 2, true)
  let fase = 0
  for (let i = 0; i < n; i++) {
    const f = Math.floor(i / (taxa / 2)) % 2 === 0 ? 880 : 660
    fase += (2 * Math.PI * f) / taxa
    v.setInt16(44 + i * 2, Math.sign(Math.sin(fase)) * 0.6 * 32767, true)
  }
  return URL.createObjectURL(new Blob([buf], { type: 'audio/wav' }))
}

function elemento(): HTMLAudioElement | null {
  if (typeof Audio === 'undefined') return null
  if (!audio) {
    urlSirene ??= gerarSirene()
    audio = new Audio(urlSirene)
    audio.loop = true
    audio.preload = 'auto'
  }
  return audio
}

function sessaoReproducao() {
  try { const s = (navigator as unknown as { audioSession?: { type: string } }).audioSession; if (s) s.type = 'playback' } catch { /* sem suporte */ }
}

/** Navegadores só deixam tocar som depois de um toque: o 1º toque em qualquer lugar "destrava" o alarme. */
export function prepararAlarme() {
  const destravar = () => {
    const a = elemento(); if (!a) return
    sessaoReproducao()
    a.muted = true
    void a.play().then(() => { a.pause(); a.currentTime = 0; a.muted = false }).catch(() => { a.muted = false })
    window.removeEventListener('pointerdown', destravar)
    window.removeEventListener('keydown', destravar)
  }
  window.addEventListener('pointerdown', destravar)
  window.addEventListener('keydown', destravar)
}

export function tocarAlarme(opcoes: { som: boolean; duracaoMs?: number }) {
  pararAlarme()
  if (opcoes.som) {
    const a = elemento()
    if (a) { sessaoReproducao(); a.currentTime = 0; a.volume = 1; void a.play().catch(() => { /* bloqueado até um toque */ }) }
  }
  if ('vibrate' in navigator) {
    const vibrar = () => { try { navigator.vibrate([700, 300, 700, 300, 700]) } catch { /* sem suporte */ } }
    vibrar(); vibrando = window.setInterval(vibrar, 3000)
  }
  desligar = window.setTimeout(pararAlarme, opcoes.duracaoMs ?? 60_000)
}

export function pararAlarme() {
  if (audio) { audio.pause(); audio.currentTime = 0 }
  if (vibrando != null) { clearInterval(vibrando); vibrando = null }
  if (desligar != null) { clearTimeout(desligar); desligar = null }
  try { navigator.vibrate?.(0) } catch { /* sem suporte */ }
}

/** Quais avisos disparam alarme: pedido de socorro em local acompanhado, aviso oficial para sua região e atenção a alagamento elevada/crítica. */
export function deveAlarmar(n: { tipo?: string; evento?: string; nivel?: string; avisoId?: string; avisoOficial?: boolean }): boolean {
  if (n.tipo === 'familia') return n.evento === 'pedido'
  if (n.tipo === 'preventivo') return n.nivel === 'Elevado' || n.nivel === 'Critico'
  return !!n.avisoId // aviso meteorológico para a região/locais da pessoa
}
