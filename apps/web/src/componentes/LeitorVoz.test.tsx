import { describe, expect, it, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LeitorVoz } from './LeitorVoz'
import { ProvedorAcessibilidade } from '../lib/acessibilidade'
import { BotaoAcessibilidade } from './PainelAcessibilidade'

class Fala { lang = ''; rate = 1; voice: unknown = null; onend: (() => void) | null = null; onerror: (() => void) | null = null; constructor(public text: string) {} }

describe('Leitura por voz (síntese local do aparelho)', () => {
  let falas: Fala[]
  beforeEach(() => {
    falas = []
    Object.assign(window, {
      SpeechSynthesisUtterance: Fala,
      speechSynthesis: {
        speak: vi.fn((u: Fala) => falas.push(u)), cancel: vi.fn(), pause: vi.fn(), resume: vi.fn(),
        getVoices: () => [{ lang: 'pt-BR', localService: true, name: 'Luciana' }],
      },
    })
  })

  it('fala em pt-BR, pausa, continua, repete e para; respeita a velocidade escolhida', () => {
    render(<ProvedorAcessibilidade><LeitorVoz texto="Há um aviso para sua região." rotulo="Ouvir aviso" /></ProvedorAcessibilidade>)
    fireEvent.change(screen.getByRole('combobox'), { target: { value: '0.75' } })
    fireEvent.click(screen.getByRole('button', { name: /Ouvir aviso/ }))
    expect(falas[0].text).toBe('Há um aviso para sua região.')
    expect(falas[0].lang).toBe('pt-BR')
    expect(falas[0].rate).toBe(0.75)
    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    expect(window.speechSynthesis.pause).toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Continuar' }))
    expect(window.speechSynthesis.resume).toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Repetir' }))
    expect(falas).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: 'Parar' }))
    expect(screen.getByRole('button', { name: /Ouvir aviso/ })).toBeTruthy()
  })

  it('painel de acessibilidade amplia o texto e ativa alto contraste no documento', () => {
    render(<ProvedorAcessibilidade><BotaoAcessibilidade /></ProvedorAcessibilidade>)
    fireEvent.click(screen.getByRole('button', { name: /Acessibilidade/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Aumentar texto' }))
    expect(document.documentElement.style.getPropertyValue('zoom')).toBe('1.15')
    fireEvent.click(screen.getByRole('checkbox', { name: /Alto contraste/ }))
    expect(document.documentElement.dataset.contraste).toBe('alto')
  })
})
