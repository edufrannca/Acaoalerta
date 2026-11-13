/**
 * Gerenciador ÚNICO de leitura por voz (um só leitor ativo no app).
 *
 * Problemas conhecidos do Safari/iOS que este módulo trata:
 *  - speak() só é aceito dentro de um gesto do usuário até a primeira fala; depois de esperas assíncronas o gesto se perde.
 *    → a primeira chamada a speechSynthesis.speak acontece de forma SÍNCRONA no toque.
 *  - cancel() seguido imediatamente de speak() descarta a nova fala. → só cancelamos se algo estiver tocando, e
 *    nesse caso a nova fala começa num tique seguinte (a página já está "destravada" pelo gesto anterior).
 *  - getVoices() vem vazio no início e voiceschanged nem sempre dispara. → carregamos as vozes ao iniciar e no evento;
 *    sem lista, definimos apenas lang = 'pt-BR' (o sistema escolhe a voz).
 *  - Utterances perdidas por coleta de lixo (onend nunca chega). → referências mantidas neste módulo.
 *  - Textos longos param no meio; pause/resume são instáveis. → texto em frases curtas; "pausar" guarda a frase atual
 *    e "continuar" retoma a partir dela.
 *  - Sem voz pt-BR, com fala bloqueada ou no modo silencioso, a síntese pode "começar" sem som ou nem começar.
 *    → vigia de início (onstart em até 2,5 s) e voz de reserva do PRÓPRIO servidor (espeak-ng, /api/voz) tocada num
 *      <audio> desbloqueado no mesmo toque. Elementos <audio> tocam mesmo com o iPhone no silencioso.
 * Nada é enviado a serviços externos: a voz de reserva é sintetizada no servidor do Alerta em Ação, só com o texto da tela.
 */

export type EstadoFala = 'parado' | 'preparando' | 'falando' | 'pausado' | 'erro'
export type ModoFala = 'aparelho' | 'servidor'
export type PreferenciaVoz = 'auto' | 'aparelho' | 'servidor'

export interface Situacao {
  dono: string | null
  estado: EstadoFala
  modo: ModoFala | null
  erro: string | null
  aviso: string | null
}

let situacao: Situacao = { dono: null, estado: 'parado', modo: null, erro: null, aviso: null }
const ouvintes = new Set<() => void>()
const publicar = (s: Partial<Situacao>) => { situacao = { ...situacao, ...s }; ouvintes.forEach((f) => f()) }
export const assinar = (f: () => void) => { ouvintes.add(f); return () => { ouvintes.delete(f) } }
export const obter = () => situacao

const temSintese = () => typeof window !== 'undefined' && 'speechSynthesis' in window && 'SpeechSynthesisUtterance' in window
export const ehIOS = () => typeof navigator !== 'undefined' && (/iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1))

// ---------- Vozes ----------
let vozes: SpeechSynthesisVoice[] = []
function carregarVozes() { try { vozes = window.speechSynthesis.getVoices() } catch { vozes = [] } }
if (temSintese()) {
  carregarVozes()
  window.speechSynthesis.addEventListener?.('voiceschanged', carregarVozes)
}
export function vozPortugues(): SpeechSynthesisVoice | undefined {
  if (!vozes.length) carregarVozes()
  return vozes.find((v) => v.lang.replace('_', '-') === 'pt-BR' && v.localService)
    ?? vozes.find((v) => v.lang.replace('_', '-') === 'pt-BR')
    ?? vozes.find((v) => v.lang.toLowerCase().startsWith('pt'))
}
export const listaDeVozesConhecida = () => vozes.length > 0

// ---------- Divisão do texto ----------
export function dividir(texto: string, max = 180): string[] {
  const frases = texto.replace(/\s+/g, ' ').trim().split(/(?<=[.!?;:])\s+/)
  const partes: string[] = []
  for (const f of frases) {
    if (!f) continue
    if (f.length <= max) { partes.push(f); continue }
    let resto = f
    while (resto.length > max) {
      const corte = resto.lastIndexOf(',', max) > 40 ? resto.lastIndexOf(',', max) + 1 : resto.lastIndexOf(' ', max) > 40 ? resto.lastIndexOf(' ', max) : max
      partes.push(resto.slice(0, corte).trim()); resto = resto.slice(corte).trim()
    }
    if (resto) partes.push(resto)
  }
  return partes
}

// ---------- <audio> de reserva (desbloqueado no gesto) ----------
let audio: HTMLAudioElement | null = null
let urlAtual: string | null = null
function silencioWav(): string {
  const n = 800, buf = new ArrayBuffer(44 + n * 2), v = new DataView(buf)
  const s = (o: number, t: string) => [...t].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)))
  s(0, 'RIFF'); v.setUint32(4, 36 + n * 2, true); s(8, 'WAVE'); s(12, 'fmt '); v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true)
  v.setUint32(24, 8000, true); v.setUint32(28, 16000, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true); s(36, 'data'); v.setUint32(40, n * 2, true)
  let bin = ''; new Uint8Array(buf).forEach((b) => { bin += String.fromCharCode(b) })
  return 'data:audio/wav;base64,' + btoa(bin)
}
/** Deve ser chamado de forma síncrona dentro do toque: libera o <audio> para tocar depois de uma busca assíncrona. */
function desbloquearAudio() {
  if (typeof Audio === 'undefined') return
  if (!audio) { audio = new Audio(); audio.preload = 'auto'; audio.setAttribute('playsinline', '') }
  if (!audio.src || audio.src.startsWith('data:')) {
    audio.src = silencioWav()
    audio.play().catch(() => { /* sem gesto: ignorado */ })
  }
}

// ---------- Sessão de fala ----------
interface Sessao { id: number; dono: string; partes: string[]; indice: number; velocidade: number; modo: ModoFala; texto: string }
let sessao: Sessao | null = null
let contador = 0
const vivas: SpeechSynthesisUtterance[] = [] // evita coleta de lixo (onend perdido no Safari)
let vigia: ReturnType<typeof setTimeout> | null = null
let tempoVigiaMs = 2500
/** Somente para testes automatizados. */
export const definirTempoVigia = (ms: number) => { tempoVigiaMs = ms }
const limparVigia = () => { if (vigia) { clearTimeout(vigia); vigia = null } }

export function falar(dono: string, texto: string, velocidade: number, preferencia: PreferenciaVoz = 'auto') {
  const partes = dividir(texto)
  if (!partes.length) return
  desbloquearAudio() // síncrono, dentro do gesto
  pararInterno()
  const id = ++contador
  const modo: ModoFala = preferencia === 'servidor' || (!temSintese()) ? 'servidor' : 'aparelho'
  sessao = { id, dono, partes, indice: 0, velocidade, modo, texto }
  publicar({ dono, estado: 'preparando', modo, erro: null, aviso: null })
  if (modo === 'aparelho') falarAparelho(id, true)
  else void falarServidor(id, 'Voz do aparelho indisponível; usando a voz do servidor.')
}

function falarAparelho(id: number, noGesto: boolean) {
  const s = sessao
  if (!s || s.id !== id) return
  const synth = window.speechSynthesis
  const ocupado = synth.speaking || synth.pending
  const iniciar = () => {
    if (!sessao || sessao.id !== id) return
    const u = new SpeechSynthesisUtterance(s.partes[s.indice])
    u.lang = 'pt-BR'
    const voz = vozPortugues()
    if (voz) u.voice = voz
    u.rate = s.velocidade
    u.volume = 1
    let comecou = false
    u.onstart = () => {
      comecou = true; limparVigia()
      if (sessao?.id === id) publicar({ estado: 'falando', modo: 'aparelho', aviso: listaDeVozesConhecida() && !voz ? 'Voz em português não instalada neste aparelho; usando a voz padrão.' : null })
    }
    u.onend = () => {
      if (!sessao || sessao.id !== id || situacao.estado === 'pausado') return
      sessao.indice++
      if (sessao.indice < sessao.partes.length) falarAparelho(id, false)
      else { sessao = null; publicar({ estado: 'parado', dono: null }) }
    }
    u.onerror = (e) => {
      limparVigia()
      if (!sessao || sessao.id !== id) return
      if (e.error === 'interrupted' || e.error === 'canceled') return
      void falarServidor(id, `A voz do aparelho falhou (${e.error}). Usando a voz do servidor.`)
    }
    vivas.push(u); if (vivas.length > 40) vivas.splice(0, vivas.length - 40)
    synth.speak(u)
    if (synth.paused) synth.resume() // Safari às vezes fica "pausado" após cancelamentos
    limparVigia()
    vigia = setTimeout(() => {
      if (!comecou && sessao?.id === id) {
        synth.cancel()
        void falarServidor(id, 'A voz do aparelho não iniciou. Usando a voz do servidor.')
      }
    }, tempoVigiaMs)
  }
  // No primeiro toque falamos de forma síncrona; se já havia fala, cancelamos e iniciamos no próximo tique.
  if (ocupado) { synth.cancel(); setTimeout(iniciar, 80) }
  else if (noGesto) iniciar()
  else setTimeout(iniciar, 0)
}

async function falarServidor(id: number, aviso: string | null) {
  const s = sessao
  if (!s || s.id !== id) return
  s.modo = 'servidor'
  if (temSintese()) { try { window.speechSynthesis.cancel() } catch { /* ignore */ } }
  if (!navigator.onLine) {
    sessao = null
    publicar({ estado: 'erro', erro: 'Não foi possível reproduzir a voz do aparelho e não há internet para a voz do servidor.', dono: s.dono })
    return
  }
  publicar({ estado: 'preparando', modo: 'servidor', aviso })
  try {
    const texto = s.partes.slice(s.indice).join(' ').slice(0, 1500)
    const r = await fetch('/api/voz', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ texto, velocidade: s.velocidade }) })
    if (!r.ok) throw new Error(`HTTP ${r.status}`)
    const blob = await r.blob()
    if (!sessao || sessao.id !== id) return
    if (urlAtual) URL.revokeObjectURL(urlAtual)
    urlAtual = URL.createObjectURL(blob)
    if (!audio) { audio = new Audio(); audio.setAttribute('playsinline', '') }
    audio.src = urlAtual
    audio.onended = () => { if (sessao?.id === id) { sessao = null; publicar({ estado: 'parado', dono: null }) } }
    audio.onerror = () => { if (sessao?.id === id) { sessao = null; publicar({ estado: 'erro', erro: 'O áudio não pôde ser reproduzido neste aparelho.' }) } }
    await audio.play()
    publicar({ estado: 'falando', modo: 'servidor' })
  } catch (e) {
    if (sessao?.id !== id) return
    sessao = null
    publicar({ estado: 'erro', erro: `Não foi possível reproduzir a leitura (${e instanceof Error ? e.message : 'erro'}). Toque em "Tentar novamente".` })
  }
}

export function pausar() {
  if (!sessao) return
  if (sessao.modo === 'servidor') audio?.pause()
  else { limparVigia(); try { window.speechSynthesis.cancel() } catch { /* ignore */ } } // retomaremos da frase atual
  publicar({ estado: 'pausado' })
}

export function continuar() {
  if (!sessao) return
  if (sessao.modo === 'servidor') { desbloquearAudioSeParado(); audio?.play().then(() => publicar({ estado: 'falando' })).catch(() => publicar({ estado: 'erro', erro: 'Não foi possível retomar o áudio.' })); return }
  publicar({ estado: 'preparando' })
  falarAparelho(sessao.id, true)
}
function desbloquearAudioSeParado() { /* o elemento já foi desbloqueado no primeiro toque */ }

function pararInterno() {
  limparVigia()
  if (temSintese()) { try { if (window.speechSynthesis.speaking || window.speechSynthesis.pending || window.speechSynthesis.paused) window.speechSynthesis.cancel() } catch { /* ignore */ } }
  if (audio && urlAtual && audio.src === urlAtual) { audio.pause(); audio.currentTime = 0 }
  sessao = null
}

export function parar() {
  pararInterno()
  publicar({ estado: 'parado', dono: null, erro: null, aviso: null })
}

/** Para a leitura ao trocar de página ou quando o app vai para segundo plano. */
export function instalarParadaAutomatica() {
  if (typeof document === 'undefined') return
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden' && situacao.estado !== 'parado') pausar() })
}
