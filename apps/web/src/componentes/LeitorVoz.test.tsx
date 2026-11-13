import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { LeitorVoz } from './LeitorVoz'
import { ProvedorAcessibilidade } from '../lib/acessibilidade'
import { BotaoAcessibilidade } from './PainelAcessibilidade'
import { definirTempoVigia, dividir, parar } from '../lib/fala'

class Fala {
  lang = ''; rate = 1; volume = 1; voice: unknown = null
  onstart: (() => void) | null = null; onend: (() => void) | null = null; onerror: ((e: { error: string }) => void) | null = null
  constructor(public text: string) {}
}

function instalarSintese({ iniciaSozinho }: { iniciaSozinho: boolean }) {
  const falas: Fala[] = []
  const synth = {
    speaking: false, pending: false, paused: false,
    speak: vi.fn((u: Fala) => { falas.push(u); if (iniciaSozinho) setTimeout(() => u.onstart?.(), 0) }),
    cancel: vi.fn(), pause: vi.fn(), resume: vi.fn(),
    getVoices: () => [{ lang: 'pt-BR', localService: true, name: 'Luciana' }],
    addEventListener: vi.fn(),
  }
  Object.assign(window, { SpeechSynthesisUtterance: Fala, speechSynthesis: synth })
  return { falas, synth }
}

class AudioFalso { src = ''; currentTime = 0; onended: (() => void) | null = null; onerror: (() => void) | null = null
  play = vi.fn(() => Promise.resolve()); pause = vi.fn(); setAttribute = vi.fn() }

const montar = (t = 'Há um aviso para sua região. Você e sua família estão bem?') =>
  render(<ProvedorAcessibilidade><LeitorVoz texto={t} rotulo="Ouvir aviso" /></ProvedorAcessibilidade>)

describe('Leitura por voz', () => {
  beforeEach(() => { vi.stubGlobal('Audio', AudioFalso) })
  afterEach(() => { act(() => parar()); cleanup(); vi.useRealTimers(); vi.unstubAllGlobals() })

  it('divide o texto em frases curtas (Safari interrompe textos longos)', () => {
    expect(dividir('Primeira frase. Segunda frase! Terceira?')).toEqual(['Primeira frase.', 'Segunda frase!', 'Terceira?'])
    expect(dividir('a '.repeat(300)).every((p) => p.length <= 180)).toBe(true)
  })

  it('se a voz do aparelho não iniciar (caso do iPhone), usa a voz do servidor num <audio> desbloqueado no toque', async () => {
    definirTempoVigia(30)
    instalarSintese({ iniciaSozinho: false })
    const fetchMock = vi.fn(() => Promise.resolve({ ok: true, status: 200, blob: async () => new Blob(['RIFF'], { type: 'audio/wav' }) }))
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: () => 'blob:voz', revokeObjectURL: () => {} }))
    Object.defineProperty(navigator, 'onLine', { value: true, configurable: true })
    montar()
    fireEvent.click(screen.getByRole('button', { name: /Ouvir aviso/ }))
    await act(async () => { await new Promise((r) => setTimeout(r, 120)) })
    expect(fetchMock).toHaveBeenCalledWith('/api/voz', expect.objectContaining({ method: 'POST' }))
    const corpo = JSON.parse((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].body as string)
    expect(corpo.texto).toContain('Há um aviso para sua região.')
    expect(screen.getByText(/Voz do servidor do Alerta em Ação/)).toBeTruthy()
  })

  it('fala em pt-BR no próprio toque, frase a frase, com pausa, retomada, repetição e velocidade', async () => {
    const { falas, synth } = instalarSintese({ iniciaSozinho: true })
    montar()
    fireEvent.change(screen.getByRole('combobox'), { target: { value: '0.75' } })
    fireEvent.click(screen.getByRole('button', { name: /Ouvir aviso/ }))
    // A primeira fala acontece de forma síncrona dentro do gesto (exigência do iOS).
    expect(synth.speak).toHaveBeenCalledTimes(1)
    expect(falas[0].text).toBe('Há um aviso para sua região.')
    expect(falas[0].lang).toBe('pt-BR')
    expect(falas[0].rate).toBe(0.75)
    await act(async () => { await new Promise((r) => setTimeout(r, 5)) })
    expect(screen.getByRole('button', { name: 'Pausar' })).toBeTruthy()
    await act(async () => { falas[0].onend?.(); await new Promise((r) => setTimeout(r, 5)) })
    expect(falas[1].text).toBe('Você e sua família estão bem?')
    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    fireEvent.click(screen.getByRole('button', { name: 'Continuar' }))
    expect(falas[falas.length - 1].text).toBe('Você e sua família estão bem?') // retoma da frase atual
    fireEvent.click(screen.getByRole('button', { name: 'Repetir' }))
    expect(falas[falas.length - 1].text).toBe('Há um aviso para sua região.')
    fireEvent.click(screen.getByRole('button', { name: 'Parar' }))
    expect(screen.getByRole('button', { name: /Ouvir aviso/ })).toBeTruthy()
  })

  it('painel de acessibilidade amplia o texto, ativa alto contraste e escolhe a voz', () => {
    render(<ProvedorAcessibilidade><BotaoAcessibilidade /></ProvedorAcessibilidade>)
    fireEvent.click(screen.getByRole('button', { name: /Acessibilidade/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Aumentar texto' }))
    expect(document.documentElement.style.getPropertyValue('zoom')).toBe('1.15')
    fireEvent.click(screen.getByRole('checkbox', { name: /Alto contraste/ }))
    expect(document.documentElement.dataset.contraste).toBe('alto')
    expect(screen.getByRole('combobox', { name: /Voz da leitura/ })).toBeTruthy()
  })
})
